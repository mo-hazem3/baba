using Baba.Domain;
using Baba.Domain.Accounting;

namespace Baba.Application.Accounting;

/// <summary>Reads and writes the chart of accounts. Implemented by Infrastructure; the rules live in the domain and the services.</summary>
public interface IAccountStore
{
    Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The accounts that have voucher lines (including drafts) or ledger entries, so they cannot simply be deleted.</summary>
    Task<IReadOnlySet<Guid>> AccountIdsWithEntriesAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Account account, CancellationToken cancellationToken = default);
    Task UpdateAsync(Account account, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>Points every voucher line, ledger entry and voucher bank account of one account at another. Returns how many vouchers were touched.</summary>
    Task<int> MoveEntriesAsync(Guid fromAccountId, Guid toAccountId, CancellationToken cancellationToken = default);
}

public sealed record VoucherSearch(
    VoucherKind? Kind = null,
    VoucherStatus? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Limit = 2000);

/// <summary>One row of a voucher list. The total is the money moved (the debit side for payments and journals, the credit side for receipts).</summary>
public sealed record VoucherSummary(
    Guid Id,
    VoucherKind Kind,
    string? Number,
    DateOnly Date,
    VoucherStatus Status,
    string? Reference,
    string? Memo,
    decimal Total,
    int LineCount);

public interface IVoucherStore
{
    /// <summary>The voucher with its lines (in line order), or null.</summary>
    Task<Voucher?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VoucherSummary>> SearchAsync(VoucherSearch search, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new or changed voucher and replaces its ledger entries with <paramref name="ledger"/> (empty for a draft), all in one
    /// transaction. A posted voucher without a number is given the next one for its kind and fiscal year; the number is also set on
    /// <paramref name="voucher"/>.
    /// </summary>
    Task SaveAsync(Voucher voucher, IReadOnlyList<LedgerEntry> ledger, CancellationToken cancellationToken = default);

    /// <summary>Deletes the voucher, its lines and its ledger entries.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IPeriodStore
{
    Task<IReadOnlyList<Period>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Locks or unlocks the month that starts on <paramref name="monthStart"/>.</summary>
    Task SetLockedAsync(DateOnly monthStart, bool locked, CancellationToken cancellationToken = default);
}

/// <summary>A total for one account in the base currency.</summary>
public sealed record AccountTotal(Guid AccountId, decimal Debit, decimal Credit);

/// <summary>What one customer or supplier has been debited and credited, in the base currency.</summary>
public sealed record PartyTotal(Guid PartyId, decimal Debit, decimal Credit);

/// <summary>One ledger entry that belongs to a customer or supplier, in the base currency.</summary>
public sealed record PartyEntry(
    DateOnly Date,
    Guid VoucherId,
    VoucherKind Kind,
    string? VoucherNumber,
    string? Description,
    Guid AccountId,
    Guid PartyId,
    decimal Debit,
    decimal Credit);

/// <summary>A total for one account within one cost center, in the base currency.</summary>
public sealed record CostCenterTotal(Guid CostCenterId, Guid AccountId, decimal Debit, decimal Credit);

/// <summary>One ledger entry with the voucher it came from, in the base currency.</summary>
public sealed record LedgerLine(
    DateOnly Date,
    Guid VoucherId,
    VoucherKind Kind,
    string? VoucherNumber,
    string? VoucherMemo,
    Guid AccountId,
    string? Description,
    decimal Debit,
    decimal Credit);

/// <summary>
/// Reads the ledger for reports. Every report is built from these two queries and nothing else: balances are never stored,
/// so reports are always correct.
/// </summary>
public interface ILedgerQuery
{
    /// <summary>The sum of debits and credits per account over a date range (inclusive). Open ends mean "from the beginning" / "to the end".</summary>
    Task<IReadOnlyList<AccountTotal>> TotalsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);

    /// <summary>The entries, in date, voucher number and position order, for some accounts (or all when null).</summary>
    Task<IReadOnlyList<LedgerLine>> LinesAsync(
        IReadOnlyCollection<Guid>? accountIds, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);

    /// <summary>What each customer or supplier has been debited and credited up to a date (all time when null).</summary>
    Task<IReadOnlyList<PartyTotal>> PartyTotalsAsync(DateOnly? to, CancellationToken cancellationToken = default);

    /// <summary>Every entry tagged with a party (or with one party) up to a date, in date, voucher number and position order.</summary>
    Task<IReadOnlyList<PartyEntry>> PartyEntriesAsync(Guid? partyId, DateOnly? to, CancellationToken cancellationToken = default);

    /// <summary>The totals per account of the entries tagged with one cost center, over a date range.</summary>
    Task<IReadOnlyList<AccountTotal>> TotalsForCostCenterAsync(Guid costCenterId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);

    /// <summary>The totals per cost center and account over a date range, for the entries that carry a cost center.</summary>
    Task<IReadOnlyList<CostCenterTotal>> CostCenterTotalsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
}
