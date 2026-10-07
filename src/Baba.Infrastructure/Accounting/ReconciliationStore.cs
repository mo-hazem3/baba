using Baba.Application.Banking;
using Baba.Domain.Accounting;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Accounting;

public sealed class ReconciliationStore(ICompanyDbContextFactory contexts) : IReconciliationStore
{
    public async Task<IReadOnlyList<BankEntry>> UnreconciledEntriesAsync(Guid accountId, DateOnly upTo, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var rows = await (
            from e in context.LedgerEntries.AsNoTracking()
            join v in context.Vouchers.AsNoTracking() on e.VoucherId equals v.Id
            where e.AccountId == accountId && e.Date <= upTo && !context.ReconciledEntries.Any(r => r.LedgerEntryId == e.Id)
            orderby e.Date, v.Number, e.Sequence
            select new { e.Id, e.Date, e.VoucherId, v.Number, e.Description, VoucherMemo = v.Memo, e.BaseDebitScaled, e.BaseCreditScaled })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new BankEntry(
            r.Id, r.Date, r.VoucherId, r.Number, r.Description ?? r.VoucherMemo, Scaled.ToDecimal(r.BaseDebitScaled - r.BaseCreditScaled))).ToList();
    }

    public async Task<decimal> ReconciledBalanceAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var net = await (
            from r in context.ReconciledEntries.AsNoTracking()
            join e in context.LedgerEntries.AsNoTracking() on r.LedgerEntryId equals e.Id
            where e.AccountId == accountId
            select e.BaseDebitScaled - e.BaseCreditScaled).SumAsync(cancellationToken);
        return Scaled.ToDecimal(net);
    }

    public async Task<IReadOnlyDictionary<Guid, (int Unreconciled, BankReconciliation? Last)>> SummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var counts = await context.LedgerEntries.AsNoTracking()
            .Where(e => !context.ReconciledEntries.Any(r => r.LedgerEntryId == e.Id))
            .GroupBy(e => e.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var all = await context.BankReconciliations.AsNoTracking().ToListAsync(cancellationToken);
        var last = all.GroupBy(r => r.AccountId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.StatementDate).ThenByDescending(r => r.CompletedAt).First());

        var result = new Dictionary<Guid, (int, BankReconciliation?)>();
        foreach (var c in counts)
            result[c.AccountId] = (c.Count, last.GetValueOrDefault(c.AccountId));
        foreach (var (accountId, reconciliation) in last.Where(l => !result.ContainsKey(l.Key)))
            result[accountId] = (0, reconciliation);
        return result;
    }

    public async Task<IReadOnlyList<BankReconciliation>> ListAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.BankReconciliations.AsNoTracking().Where(r => r.AccountId == accountId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> UsableEntryIdsAsync(
        Guid accountId, DateOnly upTo, IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var ids = await context.LedgerEntries.AsNoTracking()
            .Where(e => entryIds.Contains(e.Id) && e.AccountId == accountId && e.Date <= upTo && !context.ReconciledEntries.Any(r => r.LedgerEntryId == e.Id))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public async Task CompleteAsync(
        BankReconciliation reconciliation, IReadOnlyCollection<Guid> entryIds, IReadOnlyCollection<Guid> statementLineIds,
        CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        context.BankReconciliations.Add(reconciliation);
        foreach (var entryId in entryIds)
            context.ReconciledEntries.Add(new ReconciledEntry { ReconciliationId = reconciliation.Id, LedgerEntryId = entryId });

        if (statementLineIds.Count > 0)
        {
            var lines = await context.BankStatementLines.Where(l => statementLineIds.Contains(l.Id) && l.AccountId == reconciliation.AccountId && l.ReconciliationId == null)
                .ToListAsync(cancellationToken);
            foreach (var line in lines)
                line.ReconciliationId = reconciliation.Id;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> UndoLastAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var last = (await context.BankReconciliations.Where(r => r.AccountId == accountId).ToListAsync(cancellationToken))
            .OrderByDescending(r => r.StatementDate).ThenByDescending(r => r.CompletedAt).FirstOrDefault();
        if (last is null)
            return false;

        await context.ReconciledEntries.Where(r => r.ReconciliationId == last.Id).ExecuteDeleteAsync(cancellationToken);
        foreach (var line in await context.BankStatementLines.Where(l => l.ReconciliationId == last.Id).ToListAsync(cancellationToken))
            line.ReconciliationId = null;
        context.BankReconciliations.Remove(last);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> HasReconciledEntriesAsync(Guid voucherId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await (
            from r in context.ReconciledEntries.AsNoTracking()
            join e in context.LedgerEntries.AsNoTracking() on r.LedgerEntryId equals e.Id
            where e.VoucherId == voucherId
            select r.Id).AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BankStatementLine>> StatementLinesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.BankStatementLines.AsNoTracking().Where(l => l.AccountId == accountId).ToListAsync(cancellationToken);
    }

    public async Task AddStatementLinesAsync(IReadOnlyCollection<BankStatementLine> lines, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.BankStatementLines.AddRange(lines);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteOpenStatementLinesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var lines = await context.BankStatementLines.Where(l => l.AccountId == accountId && l.ReconciliationId == null).ToListAsync(cancellationToken);
        context.BankStatementLines.RemoveRange(lines);
        await context.SaveChangesAsync(cancellationToken);
        return lines.Count;
    }
}
