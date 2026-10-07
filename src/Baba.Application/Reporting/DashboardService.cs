using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Localization;

namespace Baba.Application.Reporting;

/// <summary>The key balances on the Summary page, in plain numbers (brief sections 7.3 and 11).</summary>
public sealed record DashboardDto(
    string CurrencyCode,
    int MinorUnits,
    decimal Cash,
    decimal Receivables,
    decimal Payables,
    decimal ProfitThisMonth,
    DateOnly MonthStart,
    DateOnly MonthEnd,
    int DraftVouchers);

/// <summary>
/// Cash and bank, receivables, payables, and this month's profit, computed live from the ledger. Which accounts count is set by
/// the roles on the chart of accounts (a bank or cash account, a receivable, a payable), not by account codes.
/// </summary>
public sealed class DashboardService(IAccountStore accounts, ILedgerQuery ledger, IVoucherStore vouchers, ICompanyFiles files, TimeProvider clock)
{
    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var currency = CurrencyCatalog.Find(company.BaseCurrencyCode)?.Currency ?? new Currency(company.BaseCurrencyCode, 2);

        var all = await accounts.ListAsync(cancellationToken);
        var byId = all.ToDictionary(a => a.Id);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var (monthStart, monthEnd) = Period.MonthOf(today);

        decimal Net(IEnumerable<AccountTotal> totals, Func<Account, bool> pick, bool debitSide) =>
            totals.Where(t => byId.TryGetValue(t.AccountId, out var a) && pick(a))
                .Sum(t => debitSide ? t.Debit - t.Credit : t.Credit - t.Debit);

        var everything = await ledger.TotalsAsync(null, null, cancellationToken);
        var thisMonth = await ledger.OperatingTotalsAsync(monthStart, monthEnd, cancellationToken); // the year-end closing entry is not a loss
        var drafts = await vouchers.SearchAsync(new VoucherSearch(Status: VoucherStatus.Draft), cancellationToken);

        return new DashboardDto(
            company.BaseCurrencyCode, currency.MinorUnits,
            Cash: Net(everything, a => a.Role == AccountRole.CashOrBank, debitSide: true),
            Receivables: Net(everything, a => a.Role == AccountRole.Receivable, debitSide: true),
            Payables: Net(everything, a => a.Role == AccountRole.Payable, debitSide: false),
            ProfitThisMonth: Net(thisMonth, a => a.Type == AccountType.Revenue, debitSide: false) - Net(thisMonth, a => a.Type == AccountType.Expense, debitSide: true),
            monthStart, monthEnd, drafts.Count);
    }
}
