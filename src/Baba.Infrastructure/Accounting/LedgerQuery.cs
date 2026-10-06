using Baba.Application.Accounting;
using Baba.Domain.Accounting;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Accounting;

/// <summary>Reads the ledger. Sums are done in SQL over the scaled integer columns, so they are exact (ADR 0002).</summary>
public sealed class LedgerQuery(ICompanyDbContextFactory contexts) : ILedgerQuery
{
    public async Task<IReadOnlyList<AccountTotal>> TotalsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var entries = context.LedgerEntries.AsNoTracking().AsQueryable();
        if (from is { } start) entries = entries.Where(e => e.Date >= start);
        if (to is { } end) entries = entries.Where(e => e.Date <= end);

        var rows = await entries
            .GroupBy(e => e.AccountId)
            .Select(g => new { AccountId = g.Key, Debit = g.Sum(e => e.BaseDebitScaled), Credit = g.Sum(e => e.BaseCreditScaled) })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new AccountTotal(r.AccountId, Scaled.ToDecimal(r.Debit), Scaled.ToDecimal(r.Credit))).ToList();
    }

    public async Task<IReadOnlyList<LedgerLine>> LinesAsync(
        IReadOnlyCollection<Guid>? accountIds, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var entries = context.LedgerEntries.AsNoTracking().AsQueryable();
        if (accountIds is not null) entries = entries.Where(e => accountIds.Contains(e.AccountId));
        if (from is { } start) entries = entries.Where(e => e.Date >= start);
        if (to is { } end) entries = entries.Where(e => e.Date <= end);

        var rows = await (
            from e in entries
            join v in context.Vouchers.AsNoTracking() on e.VoucherId equals v.Id
            orderby e.Date, v.Number, e.Sequence
            select new
            {
                e.Date, e.VoucherId, v.Kind, v.Number, VoucherMemo = v.Memo, e.AccountId, e.Description,
                Debit = e.BaseDebitScaled, Credit = e.BaseCreditScaled,
            }).ToListAsync(cancellationToken);

        return rows.Select(r => new LedgerLine(
            r.Date, r.VoucherId, r.Kind, r.Number, r.VoucherMemo, r.AccountId, r.Description,
            Scaled.ToDecimal(r.Debit), Scaled.ToDecimal(r.Credit))).ToList();
    }
}
