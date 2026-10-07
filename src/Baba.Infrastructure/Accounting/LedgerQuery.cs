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

    public async Task<IReadOnlyList<PartyTotal>> PartyTotalsAsync(DateOnly? to, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var entries = context.LedgerEntries.AsNoTracking().Where(e => e.PartyId != null);
        if (to is { } end) entries = entries.Where(e => e.Date <= end);

        var rows = await entries
            .GroupBy(e => e.PartyId!.Value)
            .Select(g => new { PartyId = g.Key, Debit = g.Sum(e => e.BaseDebitScaled), Credit = g.Sum(e => e.BaseCreditScaled) })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new PartyTotal(r.PartyId, Scaled.ToDecimal(r.Debit), Scaled.ToDecimal(r.Credit))).ToList();
    }

    public async Task<IReadOnlyList<PartyEntry>> PartyEntriesAsync(Guid? partyId, DateOnly? to, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var entries = context.LedgerEntries.AsNoTracking().Where(e => e.PartyId != null);
        if (partyId is { } id) entries = entries.Where(e => e.PartyId == id);
        if (to is { } end) entries = entries.Where(e => e.Date <= end);

        var rows = await (
            from e in entries
            join v in context.Vouchers.AsNoTracking() on e.VoucherId equals v.Id
            orderby e.Date, v.Number, e.Sequence
            select new
            {
                e.Date, e.VoucherId, v.Kind, v.Number, e.Description, VoucherMemo = v.Memo, e.AccountId, PartyId = e.PartyId!.Value,
                Debit = e.BaseDebitScaled, Credit = e.BaseCreditScaled,
            }).ToListAsync(cancellationToken);

        return rows.Select(r => new PartyEntry(
            r.Date, r.VoucherId, r.Kind, r.Number, r.Description ?? r.VoucherMemo, r.AccountId, r.PartyId,
            Scaled.ToDecimal(r.Debit), Scaled.ToDecimal(r.Credit))).ToList();
    }

    public async Task<IReadOnlyList<AccountTotal>> TotalsForCostCenterAsync(
        Guid costCenterId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var entries = context.LedgerEntries.AsNoTracking().Where(e => e.CostCenterId == costCenterId);
        if (from is { } start) entries = entries.Where(e => e.Date >= start);
        if (to is { } end) entries = entries.Where(e => e.Date <= end);

        var rows = await entries
            .GroupBy(e => e.AccountId)
            .Select(g => new { AccountId = g.Key, Debit = g.Sum(e => e.BaseDebitScaled), Credit = g.Sum(e => e.BaseCreditScaled) })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new AccountTotal(r.AccountId, Scaled.ToDecimal(r.Debit), Scaled.ToDecimal(r.Credit))).ToList();
    }

    public async Task<IReadOnlyList<CostCenterTotal>> CostCenterTotalsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var entries = context.LedgerEntries.AsNoTracking().Where(e => e.CostCenterId != null);
        if (from is { } start) entries = entries.Where(e => e.Date >= start);
        if (to is { } end) entries = entries.Where(e => e.Date <= end);

        var rows = await entries
            .GroupBy(e => new { CostCenterId = e.CostCenterId!.Value, e.AccountId })
            .Select(g => new { g.Key.CostCenterId, g.Key.AccountId, Debit = g.Sum(e => e.BaseDebitScaled), Credit = g.Sum(e => e.BaseCreditScaled) })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new CostCenterTotal(r.CostCenterId, r.AccountId, Scaled.ToDecimal(r.Debit), Scaled.ToDecimal(r.Credit))).ToList();
    }
}
