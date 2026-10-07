using Baba.Application;
using Baba.Application.Accounting;
using Baba.Application.Reporting;
using Baba.Domain.Accounting;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Closing a fiscal year (brief section 10.2): profit into retained earnings, the year locked, and the close taken back.</summary>
public class FiscalYearTests : AccountingFixture
{
    private static readonly DateOnly Dec31 = new(2026, 12, 31);

    /// <summary>The seeded October 2026: revenue 4,000, expenses 1,870.5, so a profit of 2,129.5. Then it is January 2027.</summary>
    private async Task<Env> SeedEndedYearAsync()
    {
        var e = await SeedMonthAsync();
        Clock.Now = new DateTimeOffset(2027, 1, 15, 9, 0, 0, TimeSpan.Zero);
        return e;
    }

    private static decimal NetProfit(ReportResult report) => report.Rows.Last().Cells[^1].Amount!.Value;

    /// <summary>What an account moved during 2026 (debits less credits), from the ledger. (The seeded sale of October 2025 is a year before the books start.)</summary>
    private static async Task<decimal> Moved2026Async(Env e, string code)
    {
        var total = (await e.Ledger.TotalsAsync(new DateOnly(2026, 1, 1), Dec31)).FirstOrDefault(t => t.AccountId == e.Id(code));
        return total is null ? 0m : total.Debit - total.Credit;
    }

    // ------------------------------------------------------------------ The list

    [Fact]
    public async Task The_list_shows_each_fiscal_year_with_its_profit_newest_first()
    {
        var e = await SeedEndedYearAsync();

        var years = await e.Years.ListAsync();

        Assert.Equal(2, years.Count); // the company's first year is 2026, and it is January 2027 already: 2027 has begun
        Assert.Equal(2027, years[0].Year);
        Assert.Equal(2026, years[^1].Year);
        var y2026 = years.Single(y => y.Year == 2026);
        Assert.Equal((new DateOnly(2026, 1, 1), Dec31, true, false, 2_129.5m), (y2026.Start, y2026.End, y2026.HasEnded, y2026.IsClosed, y2026.Profit));
        Assert.False(years.Single(y => y.Year == 2027).HasEnded);
    }

    // ------------------------------------------------------------------ Closing

    [Fact]
    public async Task Closing_a_year_moves_the_profit_into_retained_earnings_and_brings_revenue_and_expenses_to_zero()
    {
        var e = await SeedEndedYearAsync();

        var result = await e.Years.CloseAsync(2026);

        Assert.Equal("CL-2026-0001", result.VoucherNumber);
        Assert.True(result.Year.IsClosed);
        Assert.Equal(2_129.5m, await BalanceAsync(e, "34") * -1); // retained earnings now holds the profit (a credit balance)
        foreach (var code in new[] { "511", "512", "421", "422", "423" })
            Assert.Equal(0m, await Moved2026Async(e, code)); // the year's revenue and expenses are all brought to zero

        // The closing entry is a balanced voucher dated the last day of the year: 5 accounts and retained earnings.
        var closing = (await e.Vouchers.GetAsync(result.Year.ClosingVoucherId!.Value))!;
        Assert.Equal((VoucherKind.Closing, Dec31, 6), (closing.Kind, closing.Date, closing.Lines.Count));
        Assert.Equal(closing.Lines.Sum(l => l.Debit), closing.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task A_closed_year_still_shows_its_profit_and_loss_and_the_balance_sheet_still_balances()
    {
        var e = await SeedEndedYearAsync();
        await e.Years.CloseAsync(2026);

        var profitAndLoss = await e.Reports.ProfitAndLossAsync(new DateOnly(2026, 1, 1), Dec31);
        Assert.Equal(2_129.5m, NetProfit(profitAndLoss)); // the closing entry is not counted as a loss

        var sheet = await e.Reports.BalanceSheetAsync(Dec31);
        Assert.True(sheet.Checks.Single().Passed);
        var notClosed = sheet.Rows.Single(r => r.Cells[1].Text?.StartsWith("Net profit") == true);
        Assert.Equal(400m, notClosed.Cells[^1].Amount); // only the 400 sale of October 2025 is still unclosed; 2026 is in retained earnings
        var retained = sheet.Rows.Single(r => r.Cells[0].Text == "34");
        Assert.Equal(2_129.5m, retained.Cells[^1].Amount);
    }

    [Fact]
    public async Task Closing_locks_the_twelve_months_so_nothing_can_be_added_to_the_year()
    {
        var e = await SeedEndedYearAsync();
        var result = await e.Years.CloseAsync(2026);

        Assert.All(await e.Periods.ListYearAsync(2026), p => Assert.True(p.IsLocked));
        var refused = await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, Receipt(e, new DateOnly(2026, 11, 3), ("511", 50m))));
        Assert.Contains("date.locked-period", Codes(refused));
        Assert.True(File.Exists(result.BackupPath)); // a copy of the file from just before the close
        Assert.Contains("before-close-2026", Path.GetFileName(result.BackupPath));
    }

    [Fact]
    public async Task A_loss_goes_into_retained_earnings_as_a_debit()
    {
        var e = await NewEnvAsync();
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, new DateOnly(2026, 6, 1), ("422", 300m)));
        Clock.Now = new DateTimeOffset(2027, 2, 1, 9, 0, 0, TimeSpan.Zero);

        await e.Years.CloseAsync(2026);

        Assert.Equal(300m, await BalanceAsync(e, "34")); // a debit balance: the business lost 300
        Assert.Equal(-300m, NetProfit(await e.Reports.ProfitAndLossAsync(new DateOnly(2026, 1, 1), Dec31)));
    }

    // ------------------------------------------------------------------ Refusals

    [Fact]
    public async Task A_year_cannot_be_closed_before_it_ends()
    {
        var e = await SeedMonthAsync(); // it is 6 October 2026

        Assert.Contains("year.not-ended", Codes(await RefusedAsync(() => e.Years.CloseAsync(2026))));
    }

    [Fact]
    public async Task A_year_cannot_be_closed_twice_or_with_drafts_or_without_retained_earnings_or_with_nothing_in_it()
    {
        var e = await SeedEndedYearAsync();
        await e.Vouchers.SaveDraftAsync(null, Payment(e, new DateOnly(2026, 12, 20), ("422", 5m)));
        Assert.Contains("year.drafts-exist", Codes(await RefusedAsync(() => e.Years.CloseAsync(2026))));

        var draft = (await e.Vouchers.ListAsync(new VoucherSearch(Status: VoucherStatus.Draft))).Single();
        await e.Vouchers.DeleteAsync(draft.Id);
        await e.Chart.SetActiveAsync(e.Id("34"), false);
        Assert.Contains("year.no-retained-earnings", Codes(await RefusedAsync(() => e.Years.CloseAsync(2026))));

        await e.Chart.SetActiveAsync(e.Id("34"), true);
        await e.Years.CloseAsync(2026);
        Assert.Contains("year.already-closed", Codes(await RefusedAsync(() => e.Years.CloseAsync(2026))));

        Assert.Equal(404, await StatusOfAsync(() => e.Years.CloseAsync(2019)));
    }

    [Fact]
    public async Task A_year_with_no_revenue_or_expenses_has_nothing_to_close()
    {
        var e = await NewEnvAsync();
        await e.Vouchers.SaveAndPostAsync(null, Receipt(e, new DateOnly(2026, 3, 1), ("31", 1_000m))); // only capital
        Clock.Now = new DateTimeOffset(2027, 2, 1, 9, 0, 0, TimeSpan.Zero);

        Assert.Contains("year.nothing-to-close", Codes(await RefusedAsync(() => e.Years.CloseAsync(2026))));
    }

    private static async Task<int> StatusOfAsync(Func<Task> action)
    {
        try
        {
            await action();
            return 200;
        }
        catch (NotFoundException)
        {
            return 404;
        }
    }

    // ------------------------------------------------------------------ Reopening

    [Fact]
    public async Task Reopening_a_year_deletes_the_closing_entry_and_unlocks_the_months()
    {
        var e = await SeedEndedYearAsync();
        await e.Years.CloseAsync(2026);

        var reopened = await e.Years.ReopenAsync(2026);

        Assert.False(reopened.IsClosed);
        Assert.Equal(2_129.5m, reopened.Profit);
        Assert.All(await e.Periods.ListYearAsync(2026), p => Assert.False(p.IsLocked));
        Assert.Equal(0m, await BalanceAsync(e, "34"));
        Assert.Equal(-4_000m, await Moved2026Async(e, "511") + await Moved2026Async(e, "512")); // the revenue is back, un-closed
        await e.Vouchers.SaveAndPostAsync(null, Receipt(e, new DateOnly(2026, 11, 3), ("511", 50m))); // the year takes entries again
    }

    [Fact]
    public async Task Only_the_latest_closed_year_can_be_reopened_and_an_open_year_cannot_be_reopened()
    {
        var e = await SeedMonthAsync();
        Assert.Contains("year.not-closed", Codes(await RefusedAsync(() => e.Years.ReopenAsync(2026))));

        await e.Vouchers.SaveAndPostAsync(null, Receipt(e, new DateOnly(2027, 5, 5), ("511", 700m)));
        Clock.Now = new DateTimeOffset(2028, 1, 10, 9, 0, 0, TimeSpan.Zero);
        await e.Years.CloseAsync(2026);
        await e.Years.CloseAsync(2027);

        Assert.Contains("year.later-year-closed", Codes(await RefusedAsync(() => e.Years.ReopenAsync(2026))));
        await e.Years.ReopenAsync(2027);
        await e.Years.ReopenAsync(2026);
        Assert.All(await e.Years.ListAsync(), y => Assert.False(y.IsClosed));
    }

    [Fact]
    public async Task The_closing_entry_is_a_posted_voucher_in_the_list_but_cannot_be_deleted_by_hand()
    {
        var e = await SeedEndedYearAsync();
        var result = await e.Years.CloseAsync(2026);

        var listed = await e.Vouchers.ListAsync(new VoucherSearch(VoucherKind.Closing));
        Assert.Equal(result.Year.ClosingVoucherId, listed.Single().Id);
        Assert.Contains("voucher.system-generated", Codes(await RefusedAsync(() => e.Vouchers.DeleteAsync(listed.Single().Id))));
    }
}
