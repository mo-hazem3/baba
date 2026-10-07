using System.Text;
using Baba.Application.Accounting;
using Baba.Application.Budgets;
using Baba.Application.Claims;
using Baba.Application.Importing;
using Baba.Application.Payroll;
using Baba.Domain.Accounting;
using Baba.Domain.Claims;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Expense claims (submit, approve, pay) and budgets (plan, copy, import, and read against what happened).</summary>
public class ClaimAndBudgetTests : AccountingFixture
{
    private static async Task<(Env Env, EmployeeDto Employee)> EnvAsync()
    {
        var e = await new ClaimAndBudgetTests().NewEnvAsync(countryCode: "KW");
        var employee = await e.Employees.CreateAsync(new EmployeeInput("E1", "", "Sara", null, null, true, new DateOnly(2025, 1, 1), null, 1000m, null, null, null, 30, 0, null, null, []));
        return (e, employee);
    }

    private static ClaimInput Taxi(Env e, EmployeeDto employee, decimal amount = 45m) => new(
        employee.Id, Oct6, "Client visit", [new ClaimLineInput(Oct6, "Taxi", e.Id("423"), amount)]);

    // ------------------------------------------------------------------ Expense claims

    [Fact]
    public async Task A_claim_is_submitted_approved_into_the_books_and_paid_back()
    {
        var (e, sara) = await EnvAsync();
        var claim = await e.Claims.SaveAsync(null, Taxi(e, sara));
        Assert.Equal("EC-2026-0001", claim.Number);
        Assert.Equal(0m, await BalanceAsync(e, "423")); // a draft changes nothing

        await e.Claims.SubmitAsync(claim.Id);
        var approved = await e.Claims.ApproveAsync(claim.Id);

        Assert.Equal(ClaimStatus.Approved, approved.Status);
        Assert.Equal(45m, await BalanceAsync(e, "423"));
        Assert.Equal(-45m, await BalanceAsync(e, "216")); // owed to the employee
        var voucher = await e.Vouchers.GetAsync(approved.VoucherId!.Value);
        Assert.StartsWith("XC-2026-", voucher!.Number);

        var paid = await e.Claims.PayAsync(claim.Id, new PayClaimInput(Oct6, e.Id("111")));

        Assert.Equal(ClaimStatus.Paid, paid.Status);
        Assert.Equal(0m, await BalanceAsync(e, "216"));
        Assert.Equal(-45m, await BalanceAsync(e, "111"));
        var unpaid = await e.Claims.UnpayAsync(claim.Id);
        Assert.Equal(ClaimStatus.Approved, unpaid.Status);
        Assert.Equal(-45m, await BalanceAsync(e, "216"));

        var back = await e.Claims.UnapproveAsync(claim.Id);
        Assert.Equal(ClaimStatus.Submitted, back.Status);
        Assert.Equal(0m, await BalanceAsync(e, "423"));
    }

    [Fact]
    public async Task A_rejected_claim_can_be_changed_and_sent_again_and_numbers_run_on()
    {
        var (e, sara) = await EnvAsync();
        var first = await e.Claims.SaveAsync(null, Taxi(e, sara));
        await e.Claims.SubmitAsync(first.Id);

        var rejected = await e.Claims.RejectAsync(first.Id, "No receipt");
        Assert.Equal(ClaimStatus.Rejected, rejected.Status);
        Assert.Equal("No receipt", rejected.RejectionReason);

        var changed = await e.Claims.SaveAsync(first.Id, Taxi(e, sara, 50m));
        Assert.Equal(ClaimStatus.Draft, changed.Status);
        Assert.Null(changed.RejectionReason);
        Assert.Equal(50m, changed.Total);

        var second = await e.Claims.SaveAsync(null, Taxi(e, sara));
        Assert.Equal("EC-2026-0002", second.Number);
    }

    [Fact]
    public async Task A_claim_in_the_books_cannot_be_edited_or_deleted_and_the_steps_must_come_in_order()
    {
        var (e, sara) = await EnvAsync();
        var claim = await e.Claims.SaveAsync(null, Taxi(e, sara));

        Assert.Contains("claim.not-submitted", Codes(await RefusedAsync(() => e.Claims.ApproveAsync(claim.Id))));
        Assert.Contains("claim.not-approved", Codes(await RefusedAsync(() => e.Claims.PayAsync(claim.Id, new PayClaimInput(Oct6, e.Id("111"))))));

        await e.Claims.SubmitAsync(claim.Id);
        await e.Claims.ApproveAsync(claim.Id);
        Assert.Contains("claim.not-draft", Codes(await RefusedAsync(() => e.Claims.SaveAsync(claim.Id, Taxi(e, sara)))));
        Assert.Contains("claim.not-draft", Codes(await RefusedAsync(() => e.Claims.DeleteAsync(claim.Id))));
        Assert.Contains("claim.cash-account-invalid", Codes(await RefusedAsync(() => e.Claims.PayAsync(claim.Id, new PayClaimInput(Oct6, e.Id("423"))))));
    }

    [Fact]
    public async Task Wrong_claims_are_refused_with_the_line_that_is_wrong()
    {
        var (e, sara) = await EnvAsync();

        var refused = await RefusedAsync(() => e.Claims.SaveAsync(null, new ClaimInput(
            Guid.NewGuid(), Oct6, null, [new ClaimLineInput(Oct6, "Sales?", e.Id("511"), 10m), new ClaimLineInput(Oct6, "Free", e.Id("423"), 0m)])));
        var empty = await RefusedAsync(() => e.Claims.SaveAsync(null, new ClaimInput(sara.Id, Oct6, null, [])));

        Assert.Contains(refused.Issues, i => i is { Field: "employee", Code: "claim.employee-unknown" });
        Assert.Contains(refused.Issues, i => i is { Field: "lines[0].account", Code: "claim.account-invalid" });
        Assert.Contains(refused.Issues, i => i is { Field: "lines[1].amount", Code: "claim.amount-invalid" });
        Assert.Contains("claim.lines-required", Codes(empty));
    }

    [Fact]
    public async Task A_claim_can_be_exported_in_a_list()
    {
        var (e, sara) = await EnvAsync();
        await e.Claims.SaveAsync(null, Taxi(e, sara));

        var report = await e.Listings.ClaimsAsync();

        var row = Assert.Single(report.Rows);
        Assert.Equal("EC-2026-0001", row.Cells[0].Text);
        Assert.Equal("Sara", row.Cells[2].Text);
        Assert.Equal(45m, row.Cells[5].Amount);
    }

    // ------------------------------------------------------------------ Budgets

    private static BudgetLineInput Plan(Env e, string code, decimal each) => new(e.Id(code), Enumerable.Repeat(each, 12).ToArray());

    [Fact]
    public async Task A_budget_is_saved_replaced_and_kept_apart_for_each_cost_center()
    {
        var e = await NewEnvAsync();
        var center = await e.CostCenters.CreateAsync(new CostCenterInput("BR1", "", "Branch"));

        await e.Budgets.SaveAsync(new BudgetInput(2026, null, [Plan(e, "511", 1000m), Plan(e, "422", 400m)]));
        await e.Budgets.SaveAsync(new BudgetInput(2026, center.Id, [Plan(e, "511", 300m)]));
        var saved = await e.Budgets.GetAsync(2026, null);
        await e.Budgets.SaveAsync(new BudgetInput(2026, null, [Plan(e, "511", 1200m), new BudgetLineInput(e.Id("423"), new decimal[12])]));
        var replaced = await e.Budgets.GetAsync(2026, null);

        Assert.Equal(2, saved.Lines.Count);
        Assert.Equal(12_000m, saved.Lines.Single(l => l.AccountId == e.Id("511")).Amounts.Sum());
        Assert.Single(replaced.Lines); // the rent is gone, and an account of zeros is left out
        Assert.Equal(1200m, replaced.Lines.Single().Amounts[0]);
        Assert.Equal(300m, (await e.Budgets.GetAsync(2026, center.Id)).Lines.Single().Amounts[5]); // the cost center's own
        Assert.Equal([2026], await e.Budgets.YearsAsync());
    }

    [Fact]
    public async Task Wrong_budgets_are_refused()
    {
        var e = await NewEnvAsync();

        var refused = await RefusedAsync(() => e.Budgets.SaveAsync(new BudgetInput(2026, null,
            [new BudgetLineInput(e.Id("111"), new decimal[12]), new BudgetLineInput(e.Id("511"), [1m, 2m]), new BudgetLineInput(e.Id("422"), [.. Enumerable.Repeat(-5m, 12)])])));

        Assert.Contains(refused.Issues, i => i is { Field: "lines[0].account", Code: "budget.account-invalid" });
        Assert.Contains(refused.Issues, i => i is { Field: "lines[1].amounts", Code: "budget.months-invalid" });
        Assert.Contains(refused.Issues, i => i is { Field: "lines[2].amounts", Code: "budget.amount-negative" });
    }

    [Fact]
    public async Task A_budget_is_copied_to_the_next_year_with_a_percentage_added()
    {
        var e = await NewEnvAsync();
        await e.Budgets.SaveAsync(new BudgetInput(2026, null, [Plan(e, "511", 1000m)]));

        var copy = await e.Budgets.CopyAsync(new CopyBudgetInput(2026, 2027, null, 10m));

        Assert.Equal(1100m, copy.Lines.Single().Amounts[0]);
        Assert.Equal(new DateOnly(2027, 1, 1), copy.Start);
        var none = await RefusedAsync(() => e.Budgets.CopyAsync(new CopyBudgetInput(2030, 2031, null, 0)));
        Assert.Contains("budget.nothing-to-copy", Codes(none));
    }

    [Fact]
    public async Task Budget_versus_actual_compares_the_months_of_a_period_with_what_was_posted()
    {
        var e = await NewEnvAsync();
        await e.Budgets.SaveAsync(new BudgetInput(2026, null, [Plan(e, "511", 1000m), Plan(e, "422", 400m)]));
        await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 1500m)));
        await e.Vouchers.SaveAndPostAsync(null, new VoucherInput(VoucherKind.Payment, Oct6, e.Id("111"), null, null, [new VoucherLineInput(null, e.Id("422"), null, 250m, 0)]));

        var october = await e.Listings.BudgetVsActualAsync(Oct1, Oct31, null);
        var quarter = await e.Listings.BudgetVsActualAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31), null);

        var sales = october.Rows.Single(r => r.Cells[0].Text == "511");
        Assert.Equal(1000m, sales.Cells[2].Amount);
        Assert.Equal(1500m, sales.Cells[3].Amount);
        Assert.Equal(500m, sales.Cells[4].Amount);
        Assert.Equal(150m, sales.Cells[5].Amount); // percent of budget
        var rent = october.Rows.Single(r => r.Cells[0].Text == "422");
        Assert.Equal(400m, rent.Cells[2].Amount);
        Assert.Equal(250m, rent.Cells[3].Amount);
        var profit = october.Rows.Last();
        Assert.Equal(600m, profit.Cells[2].Amount);   // 1,000 - 400 planned
        Assert.Equal(1250m, profit.Cells[3].Amount);  // 1,500 - 250 happened
        Assert.Equal(3000m, quarter.Rows.Single(r => r.Cells[0].Text == "511").Cells[2].Amount); // three months of budget
    }

    [Fact]
    public async Task A_budget_exports_in_the_layout_the_import_reads_and_comes_back_the_same()
    {
        var e = await NewEnvAsync();
        await e.Budgets.SaveAsync(new BudgetInput(2026, null, [new BudgetLineInput(e.Id("511"), [100m, 200m, 300m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 400m])]));
        var report = await e.Listings.BudgetAsync(2026, null);
        Assert.Equal("1", report.Columns[2].TitleEn);
        Assert.Equal(1000m, report.Rows.Last().Cells[14].Amount);

        var csv = "Account code,1,2,3,4,5,6,7,8,9,10,11,12\n511,10,20,30,0,0,0,0,0,0,0,0,40\n422,5,5,5,5,5,5,5,5,5,5,5,5\n";
        var result = await Importer(e).ImportBudgetAsync(2026, null, "budget.csv", Encoding.UTF8.GetBytes(csv));

        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Imported);
        var budget = await e.Budgets.GetAsync(2026, null);
        Assert.Equal(40m, budget.Lines.Single(l => l.AccountId == e.Id("511")).Amounts[11]);
    }

    [Fact]
    public async Task A_yearly_total_is_spread_over_the_months_and_a_wrong_row_imports_nothing()
    {
        var e = await NewEnvAsync();

        var spread = await Importer(e).ImportBudgetAsync(2026, null, "b.csv", Encoding.UTF8.GetBytes("Account code,Yearly total\n511,1200\n"));
        Assert.Empty(spread.Issues);
        var months = (await e.Budgets.GetAsync(2026, null)).Lines.Single().Amounts;
        Assert.Equal(100m, months[0]);
        Assert.Equal(1200m, months.Sum());

        var bad = await Importer(e).ImportBudgetAsync(2027, null, "b.csv", Encoding.UTF8.GetBytes("Account code,Yearly total\n511,100\n111,5\n9999,5\n422,abc\n511,7\n"));
        Assert.Equal(0, bad.Imported);
        Assert.Contains(new ImportIssue(3, "budget.account-unknown"), bad.Issues);
        Assert.Contains(new ImportIssue(4, "budget.account-unknown"), bad.Issues);
        Assert.Contains(new ImportIssue(5, "budget.amount-invalid"), bad.Issues);
        Assert.Contains(new ImportIssue(6, "budget.account-twice"), bad.Issues);
        Assert.Empty((await e.Budgets.GetAsync(2027, null)).Lines);
    }

    private static ListImportService Importer(Env e) => new(
        new Baba.Infrastructure.Printing.TabularReader(), new Baba.Infrastructure.Accounting.AccountStore(e.Files), new Baba.Infrastructure.Accounting.PartyStore(e.Files),
        new Baba.Infrastructure.Accounting.CostCenterStore(e.Files), e.Tax, e.Products, e.Rates, e.Vouchers, e.StockDocs, e.Warehouses, e.FixedAssets, e.Employees, e.Budgets, e.Files);
}
