using Baba.Application;
using Baba.Application.Accounting;
using Baba.Application.Reporting;
using Baba.Domain.Accounting;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Customers, suppliers, cost centers: the sub-ledger rules and the reports built on them (brief section 10.2).</summary>
public class SubLedgerTests : AccountingFixture
{
    private static PartyInput Customer(string code, decimal creditLimit = 0, int terms = 30) =>
        new(PartyKind.Customer, code, "", "Customer " + code, null, null, null, null, creditLimit, terms, null);

    private static decimal Cell(ReportResult report, string rowName, string column) =>
        report.Rows.First(r => r.Cells.Any(c => c.Text == rowName)).Cells[report.Columns.ToList().FindIndex(c => c.Key == column)].Amount ?? 0m;

    // ------------------------------------------------------------------ Parties

    [Fact]
    public async Task Parties_need_a_code_and_a_name_and_the_code_is_unique()
    {
        var e = await NewEnvAsync();

        Assert.Equal(["party.code-required", "party.name-required"],
            Codes(await RefusedAsync(() => e.Parties.CreateAsync(Customer("") with { NameEn = "" }))).Order());
        Assert.Contains("party.code-duplicate", Codes(await RefusedAsync(() => e.Parties.CreateAsync(Customer("c001")))));
        Assert.Contains("party.credit-limit-negative", Codes(await RefusedAsync(() => e.Parties.CreateAsync(Customer("C9", creditLimit: -1)))));
        Assert.Contains("party.terms-invalid", Codes(await RefusedAsync(() => e.Parties.CreateAsync(Customer("C9", terms: 400)))));
    }

    [Fact]
    public async Task A_name_in_one_language_is_used_for_both()
    {
        var e = await NewEnvAsync();

        var party = await e.Parties.CreateAsync(Customer("C5") with { NameAr = "شركة النور", NameEn = "" });

        Assert.Equal("شركة النور", party.NameEn);
    }

    [Fact]
    public async Task A_party_that_has_been_used_is_switched_off_not_deleted_and_keeps_its_kind()
    {
        var e = await NewEnvAsync();
        var unused = await e.Parties.CreateAsync(Customer("C7"));
        await e.Parties.DeleteAsync(unused.Id); // nothing refers to it: gone
        Assert.DoesNotContain(await e.Parties.ListAsync(), p => p.Id == unused.Id);

        await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("113", 100m)));

        Assert.Contains("party.in-use", Codes(await RefusedAsync(() => e.Parties.DeleteAsync(e.Customer))));
        var current = (await e.Parties.ListAsync()).Single(p => p.Id == e.Customer);
        Assert.True(current.InUse);
        Assert.Equal(-100m, current.Balance); // they paid 100 and owed nothing: an advance

        var asSupplier = Customer("C001") with { Kind = PartyKind.Supplier };
        Assert.Contains("party.kind-in-use", Codes(await RefusedAsync(() => e.Parties.UpdateAsync(e.Customer, asSupplier))));

        var off = await e.Parties.SetActiveAsync(e.Customer, false);
        Assert.False(off.IsActive);
    }

    [Fact]
    public async Task A_voucher_line_on_a_receivable_account_without_a_customer_is_a_draft_at_most()
    {
        var e = await NewEnvAsync();
        var input = Receipt(e, Oct6, ("113", 100m));
        var without = input with { Lines = [input.Lines[0] with { PartyId = null }] };

        var draft = await e.Vouchers.SaveDraftAsync(null, without);
        var refused = await RefusedAsync(() => e.Vouchers.PostAsync(draft.Id));

        Assert.Contains("line.party-required", Codes(refused));
        Assert.Contains(refused.Issues, i => i.Field == "lines[0].party");
    }

    // ------------------------------------------------------------------ Statement and aging

    /// <summary>
    /// Customer C001 (30 days to pay): debits of 1,000 on 1 Aug, 600 on 15 Sep and 400 on 1 Oct, then a payment of 700 on 5 Oct.
    /// The payment clears the oldest first, so 300 of August, 600 of September and 400 of October are still open.
    /// </summary>
    private async Task<Env> SeedCustomerAsync(decimal creditLimit = 0)
    {
        var e = await NewEnvAsync();
        await e.Parties.UpdateAsync(e.Customer, Customer("C001", creditLimit));
        async Task Post(VoucherInput input) => await e.Vouchers.SaveAndPostAsync(null, input);

        await Post(Journal(e, new DateOnly(2026, 8, 1), ("113", 1_000m, 0), ("511", 0, 1_000m)));
        await Post(Journal(e, new DateOnly(2026, 9, 15), ("113", 600m, 0), ("511", 0, 600m)));
        await Post(Journal(e, new DateOnly(2026, 10, 1), ("113", 400m, 0), ("511", 0, 400m)));
        await Post(Receipt(e, new DateOnly(2026, 10, 5), ("113", 700m)));
        return e;
    }

    [Fact]
    public async Task A_customer_statement_has_an_opening_balance_entries_and_a_running_balance()
    {
        var e = await SeedCustomerAsync();

        var report = await e.Reports.PartyStatementAsync(e.Customer, Oct1, Oct31);

        Assert.Equal("party-statement", report.Key);
        var balance = report.Columns.ToList().FindIndex(c => c.Key == "balance");
        Assert.Equal([1_600m, 2_000m, 1_300m, 1_300m], report.Rows.Select(r => r.Cells[balance].Amount ?? 0m)); // opening, +400, -700, closing
        Assert.All(report.Rows.Where(r => r.Style == RowStyle.Normal), r => Assert.Equal(ReportLink.Voucher, r.Link!.Kind));
        Assert.Contains("C001", report.TitleEn);
    }

    [Fact]
    public async Task Aging_clears_the_oldest_debits_first_and_ages_the_rest_from_the_due_date()
    {
        var e = await SeedCustomerAsync();

        var report = await e.Reports.AgingAsync(PartyKind.Customer, new DateOnly(2026, 10, 20));

        // August 300 is due 31 Aug (50 days late), September 600 is due 15 Oct (5 days late), October 400 is due 31 Oct (not yet).
        Assert.Equal((400m, 600m, 300m, 0m, 0m, 1_300m),
            (Cell(report, "Customer C001", "notDue"), Cell(report, "Customer C001", "d30"), Cell(report, "Customer C001", "d60"),
             Cell(report, "Customer C001", "d90"), Cell(report, "Customer C001", "over90"), Cell(report, "Customer C001", "total")));
        Assert.Equal(1_300m, report.Rows.Last().Cells[report.Columns.ToList().FindIndex(c => c.Key == "total")].Amount);
        Assert.Equal(ReportLink.Party, report.Rows[0].Link!.Kind);
    }

    [Fact]
    public async Task The_same_debts_age_into_older_buckets_later()
    {
        var e = await SeedCustomerAsync();

        var report = await e.Reports.AgingAsync(PartyKind.Customer, new DateOnly(2026, 12, 31));

        // 31 Aug is 122 days back, 15 Oct is 77 days back, 31 Oct is 61 days back.
        Assert.Equal((0m, 0m, 1_000m, 300m, 1_300m),
            (Cell(report, "Customer C001", "notDue"), Cell(report, "Customer C001", "d60"), Cell(report, "Customer C001", "d90"),
             Cell(report, "Customer C001", "over90"), Cell(report, "Customer C001", "total")));
    }

    [Fact]
    public async Task Aging_ignores_entries_after_the_date()
    {
        var e = await SeedCustomerAsync();

        var report = await e.Reports.AgingAsync(PartyKind.Customer, new DateOnly(2026, 8, 15));

        Assert.Equal((1_000m, 1_000m), (Cell(report, "Customer C001", "notDue"), Cell(report, "Customer C001", "total")));
    }

    [Fact]
    public async Task A_payment_with_nothing_to_pay_off_is_an_advance_shown_as_a_negative_amount()
    {
        var e = await NewEnvAsync();
        var second = await e.Parties.CreateAsync(Customer("C002"));
        var input = Receipt(e, Oct6, ("113", 50m));
        await e.Vouchers.SaveAndPostAsync(null, input with { Lines = [input.Lines[0] with { PartyId = second.Id }] });

        var report = await e.Reports.AgingAsync(PartyKind.Customer, Oct31);

        Assert.Equal((-50m, -50m), (Cell(report, "Customer C002", "notDue"), Cell(report, "Customer C002", "total")));
    }

    [Fact]
    public async Task The_customer_aging_passes_its_credit_limit_check_when_nobody_is_over()
    {
        var e = await SeedCustomerAsync(creditLimit: 5_000m);

        Assert.True((await e.Reports.AgingAsync(PartyKind.Customer, Oct31)).Checks.Single().Passed);
    }

    [Fact]
    public async Task The_customer_aging_fails_its_credit_limit_check_for_a_customer_over_the_limit()
    {
        var e = await SeedCustomerAsync(creditLimit: 1_000m); // owes 1,300

        var report = await e.Reports.AgingAsync(PartyKind.Customer, Oct31);

        Assert.False(report.Checks.Single().Passed);
        Assert.Equal(1_000m, Cell(report, "Customer C001", "creditLimit"));
    }

    [Fact]
    public async Task Suppliers_age_what_is_owed_to_them()
    {
        var e = await NewEnvAsync();
        // Office supplies of 500 bought on credit on 1 Sep (30 days), 200 paid on 10 Oct from the cash account.
        await e.Vouchers.SaveAndPostAsync(null, Journal(e, new DateOnly(2026, 9, 1), ("424", 500m, 0), ("211", 0, 500m)));
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, new DateOnly(2026, 10, 10), ("211", 200m)));

        var report = await e.Reports.AgingAsync(PartyKind.Supplier, Oct31);

        // 300 is left, due 1 Oct: 30 days late.
        Assert.Equal((300m, 300m), (Cell(report, "First Supplier", "d30"), Cell(report, "First Supplier", "total")));
        Assert.Equal("aging-payable", report.Key);
        Assert.Empty(report.Checks); // credit limits only apply to customers

        var statement = await e.Reports.PartyStatementAsync(e.Supplier, null, null);
        Assert.Equal(300m, statement.Rows.Last().Cells[^1].Amount); // a supplier's balance is what we owe them
    }

    [Fact]
    public async Task Customer_balances_in_the_list_follow_the_ledger()
    {
        var e = await SeedCustomerAsync();

        var list = await e.Parties.ListAsync(PartyKind.Customer);

        Assert.Equal(1_300m, list.Single(p => p.Id == e.Customer).Balance);
        Assert.Empty(await e.Parties.ListAsync(PartyKind.Supplier) is var suppliers ? suppliers.Where(p => p.Balance != 0) : []);
    }

    [Fact]
    public async Task Moving_an_accounts_entries_keeps_the_customer_tags()
    {
        var e = await SeedCustomerAsync();
        var other = await e.Chart.CreateAsync(new AccountInput("1131", "عملاء آخرون", "Other receivables", e.Id("11"), Baba.Domain.AccountType.Asset, true, Baba.Domain.AccountRole.Receivable));

        await e.Chart.MoveEntriesAsync(e.Id("113"), other.Id);

        var report = await e.Reports.AgingAsync(PartyKind.Customer, new DateOnly(2026, 10, 20));
        Assert.Equal(1_300m, Cell(report, "Customer C001", "total"));
    }

    // ------------------------------------------------------------------ Cost centers

    [Fact]
    public async Task Cost_centers_need_a_unique_code_and_a_name_and_are_kept_once_used()
    {
        var e = await NewEnvAsync();
        var north = await e.CostCenters.CreateAsync(new CostCenterInput("N", "الفرع الشمالي", "North branch"));

        Assert.Contains("cost-center.code-duplicate", Codes(await RefusedAsync(() => e.CostCenters.CreateAsync(new CostCenterInput("n", "x", "x")))));
        Assert.Contains("cost-center.code-required", Codes(await RefusedAsync(() => e.CostCenters.CreateAsync(new CostCenterInput("", "x", "x")))));
        Assert.Contains("cost-center.name-required", Codes(await RefusedAsync(() => e.CostCenters.CreateAsync(new CostCenterInput("Z", "", "")))));

        var payment = Payment(e, Oct6, ("422", 100m));
        await e.Vouchers.SaveAndPostAsync(null, payment with { Lines = [payment.Lines[0] with { CostCenterId = north.Id }] });

        Assert.Contains("cost-center.in-use", Codes(await RefusedAsync(() => e.CostCenters.DeleteAsync(north.Id))));
        Assert.False((await e.CostCenters.SetActiveAsync(north.Id, false)).IsActive);
        var again = Payment(e, Oct6, ("422", 5m));
        var refused = await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, again with { Lines = [again.Lines[0] with { CostCenterId = north.Id }] }));
        Assert.Contains("line.cost-center-inactive", Codes(refused)); // a switched-off cost center takes no new entries
    }

    /// <summary>North branch: sales 1,000, rent 300. South branch: sales 400, salaries 500. One sale and one cost have no tag.</summary>
    private async Task<(Env Env, CostCenterDto North, CostCenterDto South)> SeedCostCentersAsync()
    {
        var e = await NewEnvAsync();
        var north = await e.CostCenters.CreateAsync(new CostCenterInput("N", "الفرع الشمالي", "North branch"));
        var south = await e.CostCenters.CreateAsync(new CostCenterInput("S", "الفرع الجنوبي", "South branch"));

        async Task PostTagged(VoucherInput input, Guid? costCenter) =>
            await e.Vouchers.SaveAndPostAsync(null, input with { Lines = input.Lines.Select(l => l with { CostCenterId = costCenter }).ToList() });

        await PostTagged(Receipt(e, Oct6, ("511", 1_000m)), north.Id);
        await PostTagged(Payment(e, Oct6, ("422", 300m)), north.Id);
        await PostTagged(Receipt(e, Oct6, ("511", 400m)), south.Id);
        await PostTagged(Payment(e, Oct6, ("421", 500m)), south.Id);
        await PostTagged(Receipt(e, Oct6, ("512", 90m)), null);
        await PostTagged(Payment(e, Oct6, ("423", 40m)), null);
        return (e, north, south);
    }

    [Fact]
    public async Task The_profit_and_loss_of_one_cost_center_counts_only_its_own_entries()
    {
        var (e, north, south) = await SeedCostCentersAsync();

        var northPl = await e.Reports.ProfitAndLossAsync(north.Id, Oct1, Oct31);
        var southPl = await e.Reports.ProfitAndLossAsync(south.Id, Oct1, Oct31);
        var company = await e.Reports.ProfitAndLossAsync(Oct1, Oct31);

        Assert.Equal(700m, northPl.Rows.Last().Cells[^1].Amount);   // 1,000 - 300
        Assert.Equal(-100m, southPl.Rows.Last().Cells[^1].Amount);  // 400 - 500
        Assert.Equal(650m, company.Rows.Last().Cells[^1].Amount);   // 700 - 100 + (90 - 40) untagged
        Assert.Contains("N North branch", northPl.TitleEn);
    }

    [Fact]
    public async Task The_cost_center_summary_lists_revenue_expenses_and_profit_for_each()
    {
        var (e, _, _) = await SeedCostCentersAsync();

        var report = await e.Reports.CostCenterSummaryAsync(Oct1, Oct31);

        var cells = report.Rows.Select(r => r.Cells.Select(c => c.Amount).ToArray()).ToList();
        Assert.Equal([1_000m, 300m, 700m], cells[0].Skip(2).Select(a => a!.Value));
        Assert.Equal([400m, 500m, -100m], cells[1].Skip(2).Select(a => a!.Value));
        Assert.Equal([1_400m, 800m, 600m], cells[2].Skip(2).Select(a => a!.Value)); // the totals only count tagged entries
        Assert.Equal(ReportLink.CostCenter, report.Rows[0].Link!.Kind);
    }

    [Fact]
    public async Task Cost_centers_survive_editing_a_posted_voucher()
    {
        var (e, north, _) = await SeedCostCentersAsync();
        var voucher = (await e.Vouchers.ListAsync(new VoucherSearch(VoucherKind.Payment))).First(v => v.Total == 300m);
        var posted = (await e.Vouchers.GetAsync(voucher.Id))!;

        // Save it again with a different amount: the tag on the line is kept and the entries are made again.
        await e.Vouchers.SaveAndPostAsync(posted.Id, new VoucherInput(
            VoucherKind.Payment, posted.Date, posted.CashAccountId, null, null,
            [new VoucherLineInput(posted.Lines[0].Id, posted.Lines[0].AccountId, null, 350m, 0, null, posted.Lines[0].CostCenterId)]));

        var northPl = await e.Reports.ProfitAndLossAsync(north.Id, Oct1, Oct31);
        Assert.Equal(650m, northPl.Rows.Last().Cells[^1].Amount); // 1,000 - 350
    }
}
