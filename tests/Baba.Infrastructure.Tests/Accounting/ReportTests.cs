using Baba.Application;
using Baba.Application.Accounting;
using Baba.Application.Reporting;
using Baba.Domain.Accounting;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>
/// "A month of bookkeeping" (the Phase 1 goal): real vouchers posted through the real services, then every report checked
/// against figures worked out by hand.
/// </summary>
public class ReportTests : AccountingFixture
{
    private static ReportRow Row(ReportResult report, string code) =>
        report.Rows.Single(r => r.Cells[0].Text == code && r.Style != RowStyle.Heading);

    private static ReportRow Row(ReportResult report, string columnKey, string text) =>
        report.Rows.Single(r => r.Cells[report.Columns.ToList().FindIndex(c => c.Key == columnKey)].Text == text);

    private static decimal? Amount(ReportResult report, ReportRow row, string column) =>
        row.Cells[report.Columns.ToList().FindIndex(c => c.Key == column)].Amount;

    // ------------------------------------------------------------------ Trial balance

    [Fact]
    public async Task The_trial_balance_shows_opening_movement_and_closing_for_the_month_and_balances()
    {
        var e = await SeedMonthAsync();

        var tb = await e.Reports.TrialBalanceAsync(Oct1, Oct31);

        Assert.Equal("Trial balance", tb.TitleEn);
        Assert.Equal("ميزان المراجعة", tb.TitleAr);
        Assert.Equal("From 01/10/2026 to 31/10/2026", tb.SubtitleEn);
        Assert.Equal("KWD", tb.CurrencyCode);
        Assert.Equal(3, tb.MinorUnits);
        Assert.All(tb.Checks, c => Assert.True(c.Passed));

        // The bank: 400 brought forward from last year, then in October +10,000 -2,000 +1,200 -1,000.
        var bank = Row(tb, "112");
        Assert.Equal(400m, Amount(tb, bank, "openingDebit"));
        Assert.Null(Amount(tb, bank, "openingCredit"));
        Assert.Equal(11_200m, Amount(tb, bank, "movementDebit"));
        Assert.Equal(3_000m, Amount(tb, bank, "movementCredit"));
        Assert.Equal(8_600m, Amount(tb, bank, "closingDebit"));
        Assert.Null(Amount(tb, bank, "closingCredit"));

        // Capital is a credit balance, so it sits in the credit columns.
        var capital = Row(tb, "31");
        Assert.Equal(10_000m, Amount(tb, capital, "movementCredit"));
        Assert.Equal(10_000m, Amount(tb, capital, "closingCredit"));
        Assert.Null(Amount(tb, capital, "closingDebit"));

        // Sales (511) had a 400 credit last year and 3,500 this month.
        var sales = Row(tb, "511");
        Assert.Equal(400m, Amount(tb, sales, "openingCredit"));
        Assert.Equal(3_900m, Amount(tb, sales, "closingCredit"));

        // The totals row: every column adds up, and debits equal credits in each.
        var totals = tb.Rows[^1];
        Assert.Equal(RowStyle.Total, totals.Style);
        Assert.Equal(Amount(tb, totals, "openingDebit"), Amount(tb, totals, "openingCredit"));
        Assert.Equal(Amount(tb, totals, "movementDebit"), Amount(tb, totals, "movementCredit"));
        Assert.Equal(Amount(tb, totals, "closingDebit"), Amount(tb, totals, "closingCredit"));
        Assert.Equal(400m, Amount(tb, totals, "openingDebit"));
    }

    [Fact]
    public async Task Groups_show_the_rolled_up_total_of_their_accounts_and_unused_accounts_are_left_out()
    {
        var e = await SeedMonthAsync();

        var tb = await e.Reports.TrialBalanceAsync(Oct1, Oct31);

        var currentAssets = Row(tb, "11");
        Assert.Equal(RowStyle.Group, currentAssets.Style);
        Assert.Equal(1_629.5m + 8_600m + 2_300m, Amount(tb, currentAssets, "closingDebit")); // cash + bank + receivables
        var generalExpenses = Row(tb, "42");
        Assert.Equal(1_870.5m, Amount(tb, generalExpenses, "closingDebit"));

        var codes = tb.Rows.Select(r => r.Cells[0].Text).ToList();
        Assert.DoesNotContain("424", codes);   // office supplies: never used
        Assert.DoesNotContain("12", codes);    // fixed assets: nothing below it was used
        Assert.Equal(codes.Where(c => c is not null).OrderBy(c => c, StringComparer.OrdinalIgnoreCase), codes.Where(c => c is not null)); // code order

        // Children sit one level below their parent, and every row can be drilled into.
        Assert.Equal(Row(tb, "11").Level + 1, Row(tb, "111").Level);
        Assert.Equal(ReportLink.Account, Row(tb, "111").Link!.Kind);
        Assert.Equal((Oct1, Oct31), (Row(tb, "111").Link!.From, Row(tb, "111").Link!.To));
    }

    [Fact]
    public async Task Without_dates_the_trial_balance_covers_everything_ever_posted()
    {
        var e = await SeedMonthAsync();

        var tb = await e.Reports.TrialBalanceAsync(null, null);

        Assert.Equal("All dates", tb.SubtitleEn);
        Assert.Null(Amount(tb, Row(tb, "112"), "openingDebit")); // nothing brought forward
        Assert.Equal(8_600m, Amount(tb, Row(tb, "112"), "closingDebit"));
        Assert.All(tb.Checks, c => Assert.True(c.Passed));
    }

    [Fact]
    public async Task A_company_with_no_entries_has_an_empty_balanced_trial_balance()
    {
        var e = await NewEnvAsync();

        var tb = await e.Reports.TrialBalanceAsync(Oct1, Oct31);

        var only = Assert.Single(tb.Rows);
        Assert.Equal(RowStyle.Total, only.Style);
        Assert.All(tb.Checks, c => Assert.True(c.Passed));
    }

    // ------------------------------------------------------------------ Profit and loss

    [Fact]
    public async Task Profit_and_loss_adds_revenues_subtracts_expenses_and_compares_with_last_year()
    {
        var e = await SeedMonthAsync();

        var pl = await e.Reports.ProfitAndLossAsync(Oct1, Oct31, Comparison.PreviousYear);

        Assert.Equal("Profit and loss", pl.TitleEn);
        Assert.Equal(["code", "name", "amount", "previous"], pl.Columns.Select(c => c.Key));

        Assert.Equal(3_500m, Amount(pl, Row(pl, "511"), "amount"));
        Assert.Equal(400m, Amount(pl, Row(pl, "511"), "previous"));
        Assert.Equal(500m, Amount(pl, Row(pl, "512"), "amount"));
        Assert.Equal(750m, Amount(pl, Row(pl, "422"), "amount"));
        Assert.Equal(0m, Amount(pl, Row(pl, "422"), "previous")); // no rent last year: zero, never invented
        Assert.Equal(4_000m, Amount(pl, Row(pl, "name", "Total revenues"), "amount"));
        Assert.Equal(1_870.5m, Amount(pl, Row(pl, "name", "Total expenses"), "amount"));

        var net = Row(pl, "name", "Net profit (loss)");
        Assert.Equal(2_129.5m, Amount(pl, net, "amount"));
        Assert.Equal(400m, Amount(pl, net, "previous"));
        Assert.Equal("صافي الربح (الخسارة)", net.Cells[1].TextAr);
    }

    [Fact]
    public async Task Profit_and_loss_without_a_comparison_has_one_amount_column_and_a_loss_is_negative()
    {
        var e = await SeedMonthAsync();

        var october = await e.Reports.ProfitAndLossAsync(Oct1, Oct31);
        Assert.Equal(["code", "name", "amount"], october.Columns.Select(c => c.Key));

        // A month with only expenses is a loss.
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, new DateOnly(2026, 11, 3), ("422", 300m)));
        var november = await e.Reports.ProfitAndLossAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        Assert.Equal(-300m, Amount(november, Row(november, "name", "Net profit (loss)"), "amount"));
    }

    // ------------------------------------------------------------------ Balance sheet

    [Fact]
    public async Task The_balance_sheet_balances_with_the_unclosed_profit_inside_equity()
    {
        var e = await SeedMonthAsync();

        var bs = await e.Reports.BalanceSheetAsync(Oct31);

        Assert.Equal("As of 31/10/2026", bs.SubtitleEn);
        Assert.Equal("حتى تاريخ 31/10/2026", bs.SubtitleAr);
        Assert.All(bs.Checks, c => Assert.True(c.Passed));

        Assert.Equal(12_529.5m, Amount(bs, Row(bs, "name", "Total assets"), "amount"));
        Assert.Equal(0m, Amount(bs, Row(bs, "name", "Total liabilities"), "amount"));
        Assert.Equal(10_000m, Amount(bs, Row(bs, "31"), "amount"));
        Assert.Equal(2_529.5m, Amount(bs, Row(bs, "name", "Net profit (loss) not yet closed to retained earnings"), "amount")); // 4,400 revenue - 1,870.5 expenses
        Assert.Equal(12_529.5m, Amount(bs, Row(bs, "name", "Total owners' equity"), "amount"));
        Assert.Equal(12_529.5m, Amount(bs, Row(bs, "name", "Total liabilities and equity"), "amount"));

        Assert.Equal(1_629.5m, Amount(bs, Row(bs, "111"), "amount"));
        Assert.Equal(2_300m, Amount(bs, Row(bs, "113"), "amount"));
    }

    [Fact]
    public async Task The_balance_sheet_as_of_an_earlier_date_only_counts_what_was_posted_by_then()
    {
        var e = await SeedMonthAsync();

        var bs = await e.Reports.BalanceSheetAsync(new DateOnly(2026, 10, 3), Comparison.PreviousYear);

        // By 3 October: bank 400 + 10,000 - 2,000 = 8,400; cash 2,000 - 870.5 = 1,129.5.
        Assert.Equal(8_400m, Amount(bs, Row(bs, "112"), "amount"));
        Assert.Equal(1_129.5m, Amount(bs, Row(bs, "111"), "amount"));
        Assert.Equal(0m, Amount(bs, Row(bs, "112"), "previous")); // a year earlier (3 Oct 2025) the 5 Oct 2025 receipt had not happened yet
        Assert.All(bs.Checks, c => Assert.True(c.Passed));

        // By 10 October the comparison year has its 400 (posted on 5 October 2025).
        var tenth = await e.Reports.BalanceSheetAsync(new DateOnly(2026, 10, 10), Comparison.PreviousYear);
        Assert.Equal(9_600m, Amount(tenth, Row(tenth, "112"), "amount")); // 400 + 10,000 - 2,000 + 1,200
        Assert.Equal(400m, Amount(tenth, Row(tenth, "112"), "previous"));
        Assert.All(tenth.Checks, c => Assert.True(c.Passed));
    }

    // ------------------------------------------------------------------ Statements

    [Fact]
    public async Task A_statement_of_account_has_an_opening_balance_a_running_balance_and_a_closing_balance()
    {
        var e = await SeedMonthAsync();

        var statement = await e.Reports.StatementOfAccountAsync(e.Id("112"), Oct1, Oct31);

        Assert.Equal("Statement of account: 112 Bank account", statement.TitleEn);
        Assert.Equal("كشف حساب: 112 الحساب البنكي", statement.TitleAr);
        var rows = statement.Rows;
        Assert.Equal(400m, rows[0].Cells[5].Amount);              // opening: last year's receipt
        var lines = rows.Skip(1).Take(rows.Count - 2).ToList();
        Assert.Equal([10_400m, 8_400m, 9_600m, 8_600m], lines.Select(r => r.Cells[5].Amount)); // the running balance
        Assert.Equal([10_000m, null, 1_200m, null], lines.Select(r => r.Cells[3].Amount));      // debits
        Assert.Equal([null, 2_000m, null, 1_000m], lines.Select(r => r.Cells[4].Amount));       // credits
        Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 15)], lines.Select(r => r.Cells[0].Date));
        Assert.All(lines, r => Assert.Equal(ReportLink.Voucher, r.Link!.Kind));

        var closing = rows[^1];
        Assert.Equal((11_200m, 3_000m, 8_600m), (closing.Cells[3].Amount, closing.Cells[4].Amount, closing.Cells[5].Amount));
    }

    [Fact]
    public async Task A_credit_side_account_reads_positive_on_its_own_side_and_a_group_covers_everything_below_it()
    {
        var e = await SeedMonthAsync();

        var capital = await e.Reports.StatementOfAccountAsync(e.Id("31"), null, null);
        Assert.Equal(10_000m, capital.Rows[^1].Cells[5].Amount); // capital: a credit balance shown as +10,000

        var expenses = await e.Reports.StatementOfAccountAsync(e.Id("42"), Oct1, Oct31);
        var lines = expenses.Rows.Skip(1).Take(expenses.Rows.Count - 2).ToList();
        Assert.Equal(3, lines.Count);                                           // salaries, rent, utilities
        Assert.Equal(1_870.5m, expenses.Rows[^1].Cells[5].Amount);
        Assert.Contains("422 Rent", lines.Select(r => r.Cells[2].Text).First(t => t!.StartsWith("422")));   // a group's lines name their account

        await Assert.ThrowsAsync<NotFoundException>(() => e.Reports.StatementOfAccountAsync(Guid.NewGuid(), null, null));
    }

    [Fact]
    public async Task The_general_ledger_has_one_statement_per_active_account()
    {
        var e = await SeedMonthAsync();

        var gl = await e.Reports.GeneralLedgerAsync(Oct1, Oct31);

        var headings = gl.Rows.Where(r => r.Style == RowStyle.Heading).Select(r => r.Cells[0].Text).ToList();
        Assert.Equal(["111", "112", "113", "31", "421", "422", "423", "511", "512"], headings);
        Assert.All(gl.Rows.Where(r => r.Style == RowStyle.Heading), r => Assert.Equal(ReportLink.Account, r.Link!.Kind));
        Assert.Equal(headings.Count, gl.Rows.Count(r => r.Style == RowStyle.Total)); // each statement ends with its closing row
    }

    [Fact]
    public async Task The_journal_lists_every_entry_in_date_order_and_balances()
    {
        var e = await SeedMonthAsync();

        var journal = await e.Reports.JournalAsync(Oct1, Oct31);

        var entries = journal.Rows.Where(r => r.Style == RowStyle.Normal).ToList();
        Assert.Equal(15, entries.Count); // 2 + 2 + 3 + 2 + 2 + 2 + 2 entries: last year's is outside the range
        Assert.Equal(entries.Select(r => r.Cells[0].Date).OrderBy(d => d), entries.Select(r => r.Cells[0].Date));
        Assert.All(entries, r => Assert.Equal(ReportLink.Voucher, r.Link!.Kind));
        Assert.Contains("111 Cash on hand", entries.Select(r => r.Cells[2].Text));
        Assert.Contains("111 النقدية بالصندوق", entries.Select(r => r.Cells[2].TextAr));
        Assert.All(journal.Checks, c => Assert.True(c.Passed));

        var totals = journal.Rows[^1];
        Assert.Equal(Amount(journal, totals, "debit"), Amount(journal, totals, "credit"));
        Assert.Equal(19_070.5m, Amount(journal, totals, "debit")); // 10,000 + 2,000 + 870.5 + 3,500 + 1,200 + 500 + 1,000
    }

    [Fact]
    public async Task Reports_need_an_open_company_and_amounts_are_exact_to_the_last_fils()
    {
        var e = await SeedMonthAsync();
        for (var i = 0; i < 30; i++)
            await e.Vouchers.SaveAndPostAsync(null, Journal(e, new DateOnly(2026, 10, 20), ("424", 0.1m, 0), ("512", 0, 0.1m)));

        var pl = await e.Reports.ProfitAndLossAsync(Oct1, Oct31);

        Assert.Equal(3m, Amount(pl, Row(pl, "424"), "amount")); // thirty tenths are exactly three
        Assert.Equal(503m, Amount(pl, Row(pl, "512"), "amount"));

        e.Files.Close();
        await Assert.ThrowsAsync<Application.Companies.CompanyFileException>(() => e.Reports.TrialBalanceAsync(null, null));
    }
}
