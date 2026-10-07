using Baba.Domain.Accounting;

namespace Baba.Application.Banking;

/// <summary>One ledger entry of a bank or cash account, as the reconciliation screen lists it. Positive amounts are money in.</summary>
public sealed record BankEntry(Guid EntryId, DateOnly Date, Guid VoucherId, string? VoucherNumber, string? Description, decimal Amount);

/// <summary>Reads and writes bank reconciliations and imported statement lines. Implemented by Infrastructure.</summary>
public interface IReconciliationStore
{
    /// <summary>The ledger entries of an account that have not been reconciled yet, up to a date, oldest first.</summary>
    Task<IReadOnlyList<BankEntry>> UnreconciledEntriesAsync(Guid accountId, DateOnly upTo, CancellationToken cancellationToken = default);

    /// <summary>The balance of the entries already reconciled (debits less credits), which is the last statement balance that was agreed.</summary>
    Task<decimal> ReconciledBalanceAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>For each account: how many entries are not reconciled yet, and its last finished reconciliation.</summary>
    Task<IReadOnlyDictionary<Guid, (int Unreconciled, BankReconciliation? Last)>> SummaryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BankReconciliation>> ListAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>The entry ids (among those given) that are already reconciled, or do not belong to the account.</summary>
    Task<IReadOnlySet<Guid>> UsableEntryIdsAsync(Guid accountId, DateOnly upTo, IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default);

    /// <summary>Saves a finished reconciliation, ticks off its entries, and marks the statement lines that were matched.</summary>
    Task CompleteAsync(
        BankReconciliation reconciliation, IReadOnlyCollection<Guid> entryIds, IReadOnlyCollection<Guid> statementLineIds,
        CancellationToken cancellationToken = default);

    /// <summary>Takes back the latest reconciliation of an account: its entries and statement lines become open again. Returns false if there is none.</summary>
    Task<bool> UndoLastAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>True when any ledger entry of the voucher has been reconciled.</summary>
    Task<bool> HasReconciledEntriesAsync(Guid voucherId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BankStatementLine>> StatementLinesAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task AddStatementLinesAsync(IReadOnlyCollection<BankStatementLine> lines, CancellationToken cancellationToken = default);

    /// <summary>Removes the imported lines of an account that no reconciliation has used. Returns how many.</summary>
    Task<int> DeleteOpenStatementLinesAsync(Guid accountId, CancellationToken cancellationToken = default);
}
