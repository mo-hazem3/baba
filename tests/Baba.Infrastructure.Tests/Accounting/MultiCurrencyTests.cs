using Baba.Application;
using Baba.Application.Accounting;
using Baba.Domain.Accounting;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>The exchange-rate table and vouchers in a foreign currency (brief section 5). The company's currency here is the three-decimal dinar.</summary>
public class MultiCurrencyTests : AccountingFixture
{
    // ------------------------------------------------------------------ The rate table

    [Fact]
    public async Task A_rate_is_stored_per_currency_and_date_and_setting_it_again_changes_it()
    {
        var e = await NewEnvAsync();

        var first = await e.Rates.SetAsync(new CurrencyRateInput("usd", Oct1, 0.3075m));
        var again = await e.Rates.SetAsync(new CurrencyRateInput("USD", Oct1, 0.3080m));

        Assert.Equal(first.Id, again.Id); // same day, same currency: one row
        Assert.Equal(["USD"], (await e.Rates.ListAsync()).Select(r => r.CurrencyCode));
        Assert.Equal(0.3080m, (await e.Rates.ListAsync("USD")).Single().Rate);
    }

    [Fact]
    public async Task The_latest_rate_on_or_before_a_date_is_used_and_the_companys_own_currency_is_always_one()
    {
        var e = await NewEnvAsync();
        await e.Rates.SetAsync(new CurrencyRateInput("USD", new DateOnly(2026, 9, 1), 0.300m));
        await e.Rates.SetAsync(new CurrencyRateInput("USD", new DateOnly(2026, 10, 1), 0.310m));

        Assert.Equal(new RateOnDate(0.300m, new DateOnly(2026, 9, 1), true), await e.Rates.RateOnAsync("USD", new DateOnly(2026, 9, 30)));
        Assert.Equal(new RateOnDate(0.310m, new DateOnly(2026, 10, 1), true), await e.Rates.RateOnAsync("USD", new DateOnly(2026, 10, 20)));
        Assert.False((await e.Rates.RateOnAsync("USD", new DateOnly(2026, 8, 1))).Found); // before the first rate
        Assert.False((await e.Rates.RateOnAsync("EUR", Oct1)).Found);
        Assert.Equal(new RateOnDate(1m, null, true), await e.Rates.RateOnAsync("KWD", Oct1));
    }

    [Fact]
    public async Task A_rate_needs_a_known_foreign_currency_a_date_and_a_sensible_number()
    {
        var e = await NewEnvAsync();

        Assert.Contains("rate.currency-unknown", Codes(await RefusedAsync(() => e.Rates.SetAsync(new CurrencyRateInput("XYZ", Oct1, 1m)))));
        Assert.Contains("rate.currency-is-base", Codes(await RefusedAsync(() => e.Rates.SetAsync(new CurrencyRateInput("KWD", Oct1, 1m)))));
        Assert.Contains("rate.date-required", Codes(await RefusedAsync(() => e.Rates.SetAsync(new CurrencyRateInput("USD", default, 1m)))));
        Assert.Contains("rate.rate-invalid", Codes(await RefusedAsync(() => e.Rates.SetAsync(new CurrencyRateInput("USD", Oct1, 0m)))));
        Assert.Contains("rate.rate-invalid", Codes(await RefusedAsync(() => e.Rates.SetAsync(new CurrencyRateInput("USD", Oct1, -2m)))));
        Assert.Contains("rate.rate-invalid", Codes(await RefusedAsync(() => e.Rates.SetAsync(new CurrencyRateInput("USD", Oct1, 5_000_000m)))));
    }

    [Fact]
    public async Task A_rate_can_be_deleted()
    {
        var e = await NewEnvAsync();
        var rate = await e.Rates.SetAsync(new CurrencyRateInput("USD", Oct1, 0.3m));

        await e.Rates.DeleteAsync(rate.Id);

        Assert.Empty(await e.Rates.ListAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => e.Rates.DeleteAsync(rate.Id));
    }

    // ------------------------------------------------------------------ Vouchers in a foreign currency

    [Fact]
    public async Task A_receipt_in_dollars_keeps_both_amounts_and_the_books_are_in_dinars()
    {
        var e = await NewEnvAsync();
        var input = Receipt(e, Oct6, ("511", 100m)) with { CurrencyCode = "USD", ExchangeRate = 0.30755m };

        var voucher = await e.Vouchers.SaveAndPostAsync(null, input);

        Assert.Equal(("USD", 0.30755m, 100m), (voucher.CurrencyCode, voucher.ExchangeRate, voucher.Total));
        // 100 dollars at 0.30755 = 30.755 dinars: the bank and the sales both show dinars, to the dinar's three decimals.
        Assert.Equal(30.755m, await BalanceAsync(e, "112"));
        Assert.Equal(-30.755m, await BalanceAsync(e, "511"));
        var summary = (await e.Vouchers.ListAsync(new VoucherSearch(VoucherKind.Receipt))).Single();
        Assert.Equal(("USD", 100m), (summary.CurrencyCode, summary.Total)); // the list still says what the voucher was written in
    }

    [Fact]
    public async Task Without_a_typed_rate_the_latest_rate_on_the_date_is_used()
    {
        var e = await NewEnvAsync();
        await e.Rates.SetAsync(new CurrencyRateInput("USD", new DateOnly(2026, 10, 1), 0.3m));

        var voucher = await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 10m)) with { CurrencyCode = "USD" });

        Assert.Equal(0.3m, voucher.ExchangeRate);
        Assert.Equal(3m, await BalanceAsync(e, "112"));
    }

    [Fact]
    public async Task A_foreign_voucher_with_no_rate_at_all_is_refused()
    {
        var e = await NewEnvAsync();

        var refused = await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 10m)) with { CurrencyCode = "EUR" }));

        Assert.Contains("exchange-rate.invalid", Codes(refused));
        Assert.Contains("currency.unknown", Codes(await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 10m)) with { CurrencyCode = "ZZZ", ExchangeRate = 1m }))));
    }

    [Fact]
    public async Task Amounts_follow_the_decimals_of_the_vouchers_currency_not_the_companys()
    {
        var e = await NewEnvAsync();

        // Dollars have two decimals even though the company's dinars have three.
        var refused = await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 10.005m)) with { CurrencyCode = "USD", ExchangeRate = 0.3m }));
        Assert.Contains("line.amount-decimals", Codes(refused));

        await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 10.50m)) with { CurrencyCode = "USD", ExchangeRate = 0.3m });
        Assert.Equal(3.15m, await BalanceAsync(e, "112"));
    }

    [Fact]
    public async Task A_journal_in_dollars_must_balance_in_dinars_too()
    {
        var e = await NewEnvAsync();
        var journal = Journal(e, Oct6, ("111", 100m, 0), ("511", 0, 100m)) with { CurrencyCode = "USD", ExchangeRate = 0.30755m };

        await e.Vouchers.SaveAndPostAsync(null, journal);

        Assert.Equal(30.755m, await BalanceAsync(e, "111"));
        Assert.Equal(-30.755m, await BalanceAsync(e, "511"));
    }

    [Fact]
    public async Task Reports_show_dinars_for_dollar_vouchers_and_the_trial_balance_balances()
    {
        var e = await NewEnvAsync();
        await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 100m)) with { CurrencyCode = "USD", ExchangeRate = 0.30755m });
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 40m)) with { CurrencyCode = "EUR", ExchangeRate = 0.35m, CashAccountId = e.Id("112") });

        var trial = await e.Reports.TrialBalanceAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.True(trial.Checks.Single().Passed);
        var profit = await e.Reports.ProfitAndLossAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Assert.Equal(30.755m - 14m, profit.Rows.Last().Cells[^1].Amount); // 100 USD of sales less 40 EUR of rent, in dinars
    }

    [Fact]
    public async Task Editing_a_dollar_voucher_keeps_its_currency_and_rate_unless_changed()
    {
        var e = await NewEnvAsync();
        var posted = await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 100m)) with { CurrencyCode = "USD", ExchangeRate = 0.3m });

        var edited = await e.Vouchers.SaveAndPostAsync(posted.Id, Receipt(e, Oct6, ("511", 200m)) with { CurrencyCode = "USD", ExchangeRate = 0.31m });

        Assert.Equal((0.31m, 200m), (edited.ExchangeRate, edited.Total));
        Assert.Equal(62m, await BalanceAsync(e, "112"));
    }

    // ------------------------------------------------------------------ The exchange-difference account

    [Fact]
    public async Task The_default_chart_has_an_exchange_differences_account_and_only_income_or_expense_accounts_may_be_one()
    {
        var e = await NewEnvAsync();

        Assert.Equal(Baba.Domain.AccountRole.ExchangeDifference, e.ByCode["428"].Role);

        var bank = await RefusedAsync(() => e.Chart.UpdateAsync(e.Id("112"), new AccountInput("112", "x", "x", e.ByCode["112"].ParentId, Baba.Domain.AccountType.Asset, true, Baba.Domain.AccountRole.ExchangeDifference)));
        Assert.Contains("account.role-wrong-type", Codes(bank));

        var revenue = await e.Chart.CreateAsync(new AccountInput("529", "أرباح فروق العملة", "Exchange gains", e.Id("5"), Baba.Domain.AccountType.Revenue, true, Baba.Domain.AccountRole.ExchangeDifference));
        Assert.Equal(Baba.Domain.AccountRole.ExchangeDifference, revenue.Role); // a revenue account is fine too
    }
}
