using Baba.Application;
using Baba.Application.Accounting;
using Baba.Domain.Accounting;
using Baba.Infrastructure.Accounting;
using Baba.Infrastructure.CompanyFiles;
using Baba.Infrastructure.Tests.CompanyFiles;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>A real encrypted company with the default chart, and the real accounting services running on it.</summary>
public abstract class AccountingFixture : CompanyFilesFixture
{
    protected sealed record Env(
        SqliteCompanyFiles Files,
        ChartOfAccountsService Chart,
        VoucherService Vouchers,
        PeriodService Periods,
        ILedgerQuery Ledger,
        IReadOnlyDictionary<string, AccountDto> ByCode)
    {
        public Guid Id(string code) => ByCode[code].Id;
    }

    protected static readonly DateOnly Oct6 = new(2026, 10, 6);

    protected async Task<Env> NewEnvAsync(string user = "accountant")
    {
        var files = NewManager(user);
        await files.CreateAsync(NewPath(), Password, NewCompany());

        var accounts = new AccountStore(files);
        var chart = new ChartOfAccountsService(accounts);
        var vouchers = new VoucherService(new VoucherStore(files), accounts, new PeriodStore(files), files, Clock);
        var periods = new PeriodService(new PeriodStore(files), files);
        var byCode = (await chart.ListAsync()).ToDictionary(a => a.Code);
        return new Env(files, chart, vouchers, periods, new LedgerQuery(files), byCode);
    }

    protected static VoucherInput Payment(Env e, DateOnly date, params (string Code, decimal Amount)[] lines) => new(
        VoucherKind.Payment, date, e.Id("111"), "CHQ-1", "payment memo",
        lines.Select(l => new VoucherLineInput(null, e.Id(l.Code), null, l.Amount, 0)).ToList());

    protected static VoucherInput Receipt(Env e, DateOnly date, params (string Code, decimal Amount)[] lines) => new(
        VoucherKind.Receipt, date, e.Id("112"), null, "receipt memo",
        lines.Select(l => new VoucherLineInput(null, e.Id(l.Code), null, 0, l.Amount)).ToList());

    protected static VoucherInput Journal(Env e, DateOnly date, params (string Code, decimal Debit, decimal Credit)[] lines) => new(
        VoucherKind.Journal, date, null, null, "journal memo",
        lines.Select(l => new VoucherLineInput(null, e.Id(l.Code), null, l.Debit, l.Credit)).ToList());

    protected static async Task<ValidationException> RefusedAsync(Func<Task> action) => await Assert.ThrowsAsync<ValidationException>(action);

    protected static IEnumerable<string> Codes(ValidationException e) => e.Issues.Select(i => i.Code);

    /// <summary>The balance of an account (debits minus credits) up to a date, from the ledger.</summary>
    protected static async Task<decimal> BalanceAsync(Env e, string code, DateOnly? to = null)
    {
        var total = (await e.Ledger.TotalsAsync(null, to)).FirstOrDefault(t => t.AccountId == e.Id(code));
        return total is null ? 0m : total.Debit - total.Credit;
    }
}
