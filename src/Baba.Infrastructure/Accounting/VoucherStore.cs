using Baba.Application.Accounting;
using Baba.Domain.Accounting;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Accounting;

public sealed class VoucherStore(ICompanyDbContextFactory contexts) : IVoucherStore
{
    public async Task<Voucher?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var voucher = await context.Vouchers.AsNoTracking().Include(v => v.Lines).SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        voucher?.Lines.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));
        return voucher;
    }

    public async Task<IReadOnlyList<VoucherSummary>> SearchAsync(VoucherSearch search, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var query = context.Vouchers.AsNoTracking().AsQueryable();
        if (search.Kind is { } kind) query = query.Where(v => v.Kind == kind);
        if (search.Status is { } status) query = query.Where(v => v.Status == status);
        if (search.From is { } from) query = query.Where(v => v.Date >= from);
        if (search.To is { } to) query = query.Where(v => v.Date <= to);

        var rows = await query
            .OrderByDescending(v => v.Date).ThenByDescending(v => v.CreatedAt)
            .Take(Math.Clamp(search.Limit, 1, 10_000))
            .Select(v => new
            {
                v.Id, v.Kind, v.Number, v.Date, v.Status, v.Reference, v.Memo,
                Debit = v.Lines.Sum(l => l.DebitScaled),
                Credit = v.Lines.Sum(l => l.CreditScaled),
                LineCount = v.Lines.Count,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new VoucherSummary(
            r.Id, r.Kind, r.Number, r.Date, r.Status, r.Reference, r.Memo,
            Scaled.ToDecimal(r.Kind == VoucherKind.Receipt ? r.Credit : r.Debit), r.LineCount)).ToList();
    }

    public async Task SaveAsync(Voucher voucher, IReadOnlyList<LedgerEntry> ledger, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var stored = await context.Vouchers.Include(v => v.Lines).SingleOrDefaultAsync(v => v.Id == voucher.Id, cancellationToken);
        if (stored is null)
        {
            context.Vouchers.Add(voucher);
            stored = voucher;
        }
        else
        {
            // Copy the new values onto the stored row, and match lines by id: unchanged rows stay untouched in the audit log.
            context.Entry(stored).CurrentValues.SetValues(voucher);
            var incoming = voucher.Lines.ToDictionary(l => l.Id);
            foreach (var old in stored.Lines.Where(l => !incoming.ContainsKey(l.Id)).ToList())
                context.VoucherLines.Remove(old);
            foreach (var line in voucher.Lines)
            {
                var existing = stored.Lines.FirstOrDefault(l => l.Id == line.Id);
                if (existing is null)
                    context.VoucherLines.Add(line);
                else
                    context.Entry(existing).CurrentValues.SetValues(line);
            }
        }

        if (stored.Status == VoucherStatus.Posted && string.IsNullOrEmpty(stored.Number))
            stored.Number = await NextNumberAsync(context, stored, cancellationToken);
        voucher.Number = stored.Number;

        // A voucher's entries are replaced as a set; a draft has none.
        await context.LedgerEntries.Where(e => e.VoucherId == voucher.Id).ExecuteDeleteAsync(cancellationToken);
        context.LedgerEntries.AddRange(ledger);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.LedgerEntries.Where(e => e.VoucherId == id).ExecuteDeleteAsync(cancellationToken);
        var stored = await context.Vouchers.Include(v => v.Lines).SingleAsync(v => v.Id == id, cancellationToken);
        context.Vouchers.Remove(stored); // tracked, so the voucher and its lines are written to the audit log

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>The next number for this kind of voucher in the fiscal year its date falls in: PV-2026-0001, PV-2026-0002 ...</summary>
    private static async Task<string> NextNumberAsync(Infrastructure.Persistence.CompanyDbContext context, Voucher voucher, CancellationToken cancellationToken)
    {
        var startMonth = await context.Companies.Select(c => c.FiscalYearStartMonth).SingleAsync(cancellationToken);
        var fiscalYear = FiscalYear.Of(voucher.Date, startMonth);
        var kind = voucher.Kind.ToString();

        var sequence = await context.NumberSequences.SingleOrDefaultAsync(s => s.Kind == kind && s.FiscalYear == fiscalYear, cancellationToken);
        if (sequence is null)
        {
            sequence = new NumberSequence { Kind = kind, FiscalYear = fiscalYear };
            context.NumberSequences.Add(sequence);
        }

        sequence.LastNumber++;
        return VoucherNumber.Format(voucher.Kind, fiscalYear, sequence.LastNumber);
    }
}
