using Baba.Application.Payroll;
using Baba.Domain.Payroll;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Payroll: payslips, the country's social insurance, one voucher a month, payment, automatic months, end-of-service and leave (brief section 10.4).</summary>
public class PayrollTests : AccountingFixture
{
    private static readonly DateOnly Sep = new(2026, 9, 1);
    private static readonly DateOnly Oct = new(2026, 10, 1);

    private static EmployeeInput Staff(
        string code, decimal basic, bool national = true, DateOnly? joined = null, IReadOnlyList<EmployeeComponentInput>? components = null, DateOnly? left = null) => new(
        code, "", $"Employee {code}", null, null, national, joined ?? new DateOnly(2025, 1, 1), left, basic, null, null, null, 30, 0, null, null, components ?? []);

    private static async Task<(Env Env, SalaryComponentDto Housing, SalaryComponentDto Loan)> EnvAsync(string country = "KW")
    {
        var e = await new PayrollTests().NewEnvAsync(countryCode: country);
        var housing = await e.Employees.CreateComponentAsync(new SalaryComponentInput("HOUSING", "", "Housing allowance", SalaryComponentKind.Earning, ComponentCalculation.Fixed, 200m, null, IsInsurable: true, InEndOfService: true));
        var loan = await e.Employees.CreateComponentAsync(new SalaryComponentInput("LOAN", "", "Loan repayment", SalaryComponentKind.Deduction, ComponentCalculation.Fixed, 40m, e.Id("115"), false, false));
        return (e, housing, loan);
    }

    [Fact]
    public async Task The_settings_start_from_the_countrys_insurance_scheme_and_the_accounts_made_for_payroll()
    {
        var (e, _, _) = await EnvAsync();

        var settings = await e.Payroll.GetSettingsAsync();

        Assert.Equal(10.5m, settings.NationalEmployeePercent);
        Assert.Equal(11.5m, settings.NationalEmployerPercent);
        Assert.Equal(2750m, settings.InsuranceCeiling);
        Assert.Equal(0m, settings.ForeignEmployeePercent);
        Assert.Equal(e.Id("421"), settings.SalaryExpenseAccountId);
        Assert.Equal(e.Id("213"), settings.SalariesPayableAccountId);
        Assert.Equal(e.Id("431"), settings.InsuranceExpenseAccountId);
        Assert.Equal(e.Id("215"), settings.InsurancePayableAccountId);
        Assert.True(settings.HasEndOfService);
        Assert.NotNull(settings.InsuranceNameEn);
    }

    [Fact]
    public async Task A_month_is_posted_with_insurance_on_the_insurable_pay_and_the_voucher_balances()
    {
        var (e, housing, loan) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 1000m, components: [new(housing.Id, 200m), new(loan.Id, 40m)]));

        var run = await e.Payroll.CreateRunAsync(Sep);
        var posted = await e.Payroll.PostRunAsync(run.Summary.Id);

        var slip = posted.Payslips.Single();
        Assert.Equal(1200m, slip.Earnings);
        Assert.Equal(126m, slip.EmployeeInsurance);   // 10.5% of 1,200
        Assert.Equal(138m, slip.EmployerInsurance);   // 11.5% of 1,200
        Assert.Equal(1034m, slip.Net);                // 1,200 - 40 - 126
        Assert.Equal(PayrollStatus.Posted, posted.Summary.Status);
        Assert.Equal(1200m, await BalanceAsync(e, "421"));
        Assert.Equal(138m, await BalanceAsync(e, "431"));
        Assert.Equal(-1034m, await BalanceAsync(e, "213"));
        Assert.Equal(-264m, await BalanceAsync(e, "215")); // both shares owed to the authority
        Assert.Equal(-40m, await BalanceAsync(e, "115"));
        var voucher = await e.Vouchers.GetAsync(posted.Summary.VoucherId!.Value);
        Assert.StartsWith("PR-2026-", voucher!.Number);
        Assert.Equal(new DateOnly(2026, 9, 30), voucher.Date);
    }

    [Fact]
    public async Task Pay_over_the_ceiling_is_charged_insurance_on_the_ceiling_and_a_foreigner_pays_none()
    {
        var (e, _, _) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 5000m));
        await e.Employees.CreateAsync(Staff("E2", 1000m, national: false));

        var run = await e.Payroll.CreateRunAsync(Sep);

        var high = run.Payslips[0];
        Assert.Equal(288.75m, high.EmployeeInsurance); // 10.5% of 2,750
        Assert.Equal(316.25m, high.EmployerInsurance);
        Assert.Equal(4711.25m, high.Net);
        Assert.Equal(0m, run.Payslips[1].EmployeeInsurance);
        Assert.Equal(1000m, run.Payslips[1].Net);
    }

    [Fact]
    public async Task A_percentage_component_follows_the_basic_and_a_new_joiner_is_paid_for_the_days_worked()
    {
        var (e, _, _) = await EnvAsync();
        var transport = await e.Employees.CreateComponentAsync(new SalaryComponentInput("TRANSPORT", "", "Transport", SalaryComponentKind.Earning, ComponentCalculation.PercentOfBasic, 10m, null, false, false));
        await e.Employees.CreateAsync(Staff("E1", 1000m, national: false, joined: new DateOnly(2026, 9, 16), components: [new(transport.Id, 10m)])); // 15 of 30 days

        var run = await e.Payroll.CreateRunAsync(Sep);

        var slip = run.Payslips.Single();
        Assert.Equal(500m, slip.Items[0].Amount);   // half the basic
        Assert.Equal(50m, slip.Items[1].Amount);    // 10% of what the basic came to
        Assert.Equal(550m, slip.Net);
    }

    [Fact]
    public async Task Someone_who_left_before_the_month_is_not_paid_and_one_who_left_in_it_is_paid_up_to_the_day()
    {
        var (e, _, _) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 3000m, national: false, left: new DateOnly(2026, 8, 31)));
        await e.Employees.CreateAsync(Staff("E2", 3000m, national: false, left: new DateOnly(2026, 9, 10)));

        var run = await e.Payroll.CreateRunAsync(Sep);

        var slip = Assert.Single(run.Payslips);
        Assert.Equal(1000m, slip.Net); // 10 of 30 days
    }

    [Fact]
    public async Task A_month_cannot_be_made_twice_or_without_employees_and_a_deduction_needs_its_account()
    {
        var (e, _, _) = await EnvAsync();
        var none = await RefusedAsync(() => e.Payroll.CreateRunAsync(Sep));
        Assert.Contains("payroll.no-employees", Codes(none));

        await e.Employees.CreateAsync(Staff("E1", 1000m));
        await e.Payroll.CreateRunAsync(Sep);
        var twice = await RefusedAsync(() => e.Payroll.CreateRunAsync(Sep));
        Assert.Contains("payroll.run-exists", Codes(twice));

        var noAccount = await RefusedAsync(() => e.Employees.CreateComponentAsync(new SalaryComponentInput("FINE", "", "Fine", SalaryComponentKind.Deduction, ComponentCalculation.Fixed, 5m, null, false, false)));
        Assert.Contains("component.account-required", Codes(noAccount));
    }

    [Fact]
    public async Task Changing_a_payslip_of_a_posted_month_posts_it_again_and_a_paid_month_must_be_unpaid_first()
    {
        var (e, _, _) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 1000m, national: false));
        var run = await e.Payroll.CreateRunAsync(Sep);
        await e.Payroll.PostRunAsync(run.Summary.Id);
        var slip = (await e.Payroll.GetRunAsync(run.Summary.Id))!.Payslips.Single();

        var items = slip.Items.Select(i => new PayslipItemInput(i.Kind, i.Label, i.LabelAr, i.Amount, i.AccountId, i.IsInsurable, i.InEndOfService, i.ComponentId, i.IsBasic)).ToList();
        items.Add(new PayslipItemInput(SalaryComponentKind.Earning, "Overtime", "عمل إضافي", 100m, null, false, false));
        var changed = await e.Payroll.SavePayslipAsync(run.Summary.Id, slip.Id, items);

        Assert.Equal(1100m, changed.Payslips.Single().Net);
        Assert.Equal(1100m, await BalanceAsync(e, "421"));
        Assert.Equal(-1100m, await BalanceAsync(e, "213"));

        await e.Payroll.PayRunAsync(run.Summary.Id, new PayInput(Oct6, e.Id("111")));
        var refused = await RefusedAsync(() => e.Payroll.SavePayslipAsync(run.Summary.Id, slip.Id, items));
        Assert.Contains("payroll.paid", Codes(refused));
    }

    [Fact]
    public async Task Paying_a_month_clears_what_was_owed_and_taking_the_payment_back_restores_it()
    {
        var (e, _, _) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 1000m, national: false));
        var run = await e.Payroll.CreateRunAsync(Sep);
        await e.Payroll.PostRunAsync(run.Summary.Id);

        var paid = await e.Payroll.PayRunAsync(run.Summary.Id, new PayInput(Oct6, e.Id("111")));

        Assert.Equal(Oct6, paid.Summary.PaidDate);
        Assert.Equal(0m, await BalanceAsync(e, "213"));
        Assert.Equal(-1000m, await BalanceAsync(e, "111"));
        var twice = await RefusedAsync(() => e.Payroll.PayRunAsync(run.Summary.Id, new PayInput(Oct6, e.Id("111"))));
        Assert.Contains("payroll.paid", Codes(twice));
        var notBank = await RefusedAsync(async () =>
        {
            await e.Payroll.UnpayRunAsync(run.Summary.Id);
            await e.Payroll.PayRunAsync(run.Summary.Id, new PayInput(Oct6, e.Id("421")));
        });
        Assert.Contains("payroll.cash-account-invalid", Codes(notBank));
        Assert.Equal(-1000m, await BalanceAsync(e, "213")); // the payment was taken back
    }

    [Fact]
    public async Task A_month_can_be_taken_back_to_a_draft_and_deleted()
    {
        var (e, _, _) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 1000m, national: false));
        var run = await e.Payroll.CreateRunAsync(Sep);
        await e.Payroll.PostRunAsync(run.Summary.Id);

        var draft = await e.Payroll.UnpostRunAsync(run.Summary.Id);
        Assert.Equal(PayrollStatus.Draft, draft.Summary.Status);
        Assert.Equal(0m, await BalanceAsync(e, "421"));

        await e.Payroll.DeleteRunAsync(run.Summary.Id);
        Assert.Empty(await e.Payroll.ListRunsAsync());
    }

    [Fact]
    public async Task Once_started_the_months_that_have_ended_are_made_and_posted_by_themselves()
    {
        var (e, _, _) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 1000m, national: false));
        await e.Payroll.CreateRunAsync(new DateOnly(2026, 7, 1)); // payroll starts in July (the clock says 6 October)

        var auto = await e.Payroll.RunDueAsync();

        Assert.Equal([new DateOnly(2026, 8, 1), Sep], auto.Items.Select(i => i.Month));
        Assert.All(auto.Items, i => Assert.True(i.Posted));
        Assert.Equal(2000m, await BalanceAsync(e, "421")); // August and September posted; July was only made
        Assert.Empty((await e.Payroll.RunDueAsync()).Items); // nothing more to do
    }

    [Fact]
    public async Task Without_automatic_posting_the_months_are_made_as_drafts()
    {
        var (e, _, _) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 1000m, national: false));
        await e.Payroll.CreateRunAsync(new DateOnly(2026, 8, 1));
        var settings = await e.Payroll.GetSettingsAsync();
        await e.Payroll.SaveSettingsAsync(new PayrollSettingsInput(
            false, settings.NationalEmployeePercent, settings.NationalEmployerPercent, settings.ForeignEmployeePercent, settings.ForeignEmployerPercent, settings.InsuranceFloor, settings.InsuranceCeiling,
            settings.SalaryExpenseAccountId, settings.SalariesPayableAccountId, settings.InsuranceExpenseAccountId, settings.InsurancePayableAccountId, settings.EndOfServiceExpenseAccountId, settings.EndOfServiceProvisionAccountId));

        var auto = await e.Payroll.RunDueAsync();

        var item = Assert.Single(auto.Items);
        Assert.False(item.Posted);
        Assert.Equal(0m, await BalanceAsync(e, "421"));
    }

    [Fact]
    public async Task A_locked_month_stops_the_automatic_run_with_the_reason()
    {
        var (e, _, _) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 1000m, national: false));
        await e.Payroll.CreateRunAsync(new DateOnly(2026, 8, 1));
        await e.Periods.SetLockedAsync(Sep, true);

        var auto = await e.Payroll.RunDueAsync();

        var item = Assert.Single(auto.Items);
        Assert.Equal("date.locked-period", item.ProblemCode);
        Assert.False(item.Posted);
    }

    [Fact]
    public async Task The_end_of_service_provision_is_brought_to_what_is_owed_once_a_month_and_can_be_taken_back()
    {
        var (e, housing, _) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 1000m, joined: new DateOnly(2023, 10, 1), components: [new(housing.Id, 200m)]));

        var position = await e.Payroll.EndOfServicePositionAsync(new DateOnly(2026, 10, 31));
        var result = await e.Payroll.AccrueEndOfServiceAsync(Oct);

        Assert.True(position.Applicable);
        Assert.Equal(1200m, position.Lines.Single().Wage); // basic and the allowance that counts
        Assert.True(position.Required > 1800m && position.Required < 2400m); // about three years at 15 days a year of a 26-day wage
        Assert.Equal(position.Required, result.Amount);
        Assert.Equal(position.Required, await BalanceAsync(e, "432"));
        Assert.Equal(-position.Required, await BalanceAsync(e, "222"));
        var voucher = await e.Vouchers.GetAsync(result.VoucherId!.Value);
        Assert.StartsWith("EA-2026-", voucher!.Number);

        var twice = await RefusedAsync(() => e.Payroll.AccrueEndOfServiceAsync(Oct));
        Assert.Contains("payroll.eos-done", Codes(twice));

        await e.Payroll.UndoEndOfServiceAsync();
        Assert.Equal(0m, await BalanceAsync(e, "222"));
    }

    [Fact]
    public async Task A_country_without_a_gratuity_has_no_end_of_service_provision()
    {
        var (e, _, _) = await EnvAsync("EG");
        await e.Employees.CreateAsync(Staff("E1", 1000m));

        var position = await e.Payroll.EndOfServicePositionAsync(new DateOnly(2026, 10, 31));
        var refused = await RefusedAsync(() => e.Payroll.AccrueEndOfServiceAsync(Oct));

        Assert.False(position.Applicable);
        Assert.Contains("payroll.eos-not-applicable", Codes(refused));
        Assert.False((await e.Payroll.GetSettingsAsync()).HasEndOfService);
    }

    [Fact]
    public async Task Annual_leave_is_earned_a_month_at_a_time_and_only_annual_leave_uses_it()
    {
        var (e, _, _) = await EnvAsync();
        var employee = await e.Employees.CreateAsync(Staff("E1", 1000m, joined: new DateOnly(2026, 1, 1)));
        await e.Employees.AddLeaveAsync(new LeaveInput(employee.Id, LeaveKind.Annual, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5), null, null)); // 5 days
        await e.Employees.AddLeaveAsync(new LeaveInput(employee.Id, LeaveKind.Sick, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 3), null, null));   // does not count

        var balance = (await e.Employees.BalancesAsync(new DateOnly(2026, 7, 1))).Single();

        Assert.Equal(15m, balance.Earned); // six months of 2.5 days
        Assert.Equal(5m, balance.Taken);
        Assert.Equal(10m, balance.Balance);
    }

    [Fact]
    public async Task Wrong_employees_are_refused_and_one_with_a_payslip_cannot_be_deleted()
    {
        var (e, _, _) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 1000m, national: false));

        var refused = await RefusedAsync(() => e.Employees.CreateAsync(Staff("E1", -5m, joined: DateOnly.MinValue)));
        Assert.Contains("employee.code-duplicate", Codes(refused));
        Assert.Contains("employee.basic-invalid", Codes(refused));
        Assert.Contains("employee.join-required", Codes(refused));

        await e.Payroll.CreateRunAsync(Sep);
        var id = (await e.Employees.ListAsync()).Single().Id;
        var inUse = await RefusedAsync(() => e.Employees.DeleteAsync(id));
        Assert.Contains("employee.in-use", Codes(inUse));
    }

    // ------------------------------------------------------------------ Reports, payslips, import

    [Fact]
    public async Task The_payroll_summary_lists_each_payslip_with_where_the_money_goes_and_the_totals()
    {
        var (e, housing, loan) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 1000m, components: [new(housing.Id, 200m), new(loan.Id, 40m)]) with { BankName = "Gulf Bank", BankAccount = "KW81000123" });
        var run = await e.Payroll.CreateRunAsync(Sep);
        await e.Payroll.PostRunAsync(run.Summary.Id);

        var report = await e.Listings.PayrollSummaryAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var row = report.Rows.First();
        Assert.Equal("2026-09", row.Cells[0].Text);
        Assert.Equal("E1", row.Cells[1].Text);
        Assert.Equal(1200m, row.Cells[3].Amount);
        Assert.Equal(40m, row.Cells[4].Amount);
        Assert.Equal(126m, row.Cells[5].Amount);
        Assert.Equal(138m, row.Cells[6].Amount);
        Assert.Equal(1034m, row.Cells[7].Amount);
        Assert.Equal("KW81000123", row.Cells[9].Text);
        Assert.Equal(1034m, report.Rows.Last().Cells[7].Amount);
        Assert.DoesNotContain((await e.Listings.PayrollSummaryAsync(new DateOnly(2027, 1, 1), null)).Rows, r => r.Cells[1].Text == "E1");
    }

    [Fact]
    public async Task The_end_of_service_report_checks_the_books_against_what_is_owed()
    {
        var (e, _, _) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 1000m, joined: new DateOnly(2023, 10, 1)));

        var before = await e.Listings.EndOfServiceAsync(new DateOnly(2026, 10, 31));
        await e.Payroll.AccrueEndOfServiceAsync(Oct);
        var after = await e.Listings.EndOfServiceAsync(new DateOnly(2026, 10, 31));

        Assert.False(before.Checks.Single().Passed); // nothing set aside yet
        Assert.True(after.Checks.Single().Passed);
    }

    [Fact]
    public async Task A_payslip_prints_in_both_languages_with_its_lines_and_the_net_pay()
    {
        var (e, housing, loan) = await EnvAsync();
        await e.Employees.CreateAsync(Staff("E1", 1000m, components: [new(housing.Id, 200m), new(loan.Id, 40m)]));
        var run = await e.Payroll.CreateRunAsync(Sep);
        var printing = new Baba.Application.Printing.PayslipPrintService(
            e.Renderer, new Baba.Infrastructure.Printing.EmbeddedPrintFonts(), e.Files, new Baba.Infrastructure.Printing.BrandingStore(e.Files), e.Payroll, e.Employees);

        await printing.RenderAsync(run.Summary.Id, null, Baba.Domain.PrintLayout.Both);

        var html = e.Renderer.Html!;
        Assert.Contains("Payslip", html);
        Assert.Contains("قسيمة راتب", html);
        Assert.Contains("Housing allowance", html);
        Assert.Contains("Net pay", html);
        Assert.Contains("1,034.000", html);
        Assert.Contains("Employee E1", html);
    }

    [Fact]
    public async Task Employees_import_from_a_file_and_one_wrong_row_imports_nothing()
    {
        var e = await new PayrollTests().NewEnvAsync(countryCode: "KW");
        var importer = new Baba.Application.Importing.ListImportService(
            new Baba.Infrastructure.Printing.TabularReader(), new Baba.Infrastructure.Accounting.AccountStore(e.Files), new Baba.Infrastructure.Accounting.PartyStore(e.Files),
            new Baba.Infrastructure.Accounting.CostCenterStore(e.Files), e.Tax, e.Products, e.Rates, e.Vouchers, e.StockDocs, e.Warehouses, e.FixedAssets, e.Employees, e.Budgets, e.Files);

        var good = await importer.ImportEmployeesAsync("e.csv", System.Text.Encoding.UTF8.GetBytes("Code,Name,Job title,National,Join date,Basic salary,Account number\nE1,Sara,Accountant,yes,2025-03-01,750,KW81000\nE2,Omar,Driver,no,2025-04-15,400,\n"));
        Assert.Empty(good.Issues);
        Assert.Equal(2, good.Imported);
        var sara = (await e.Employees.ListAsync()).Single(x => x.Code == "E1");
        Assert.True(sara.IsNational);
        Assert.Equal(750m, sara.BasicSalary);
        Assert.Equal("KW81000", sara.BankAccount);

        var bad = await importer.ImportEmployeesAsync("e.csv", System.Text.Encoding.UTF8.GetBytes("Code,Name,Join date,Basic salary\nE3,Fine,2025-01-01,100\nE1,Twice,2025-01-01,100\nE5,NoDate,notadate,100\nE6,Negative,2025-01-01,-5\n"));
        Assert.Equal(0, bad.Imported);
        Assert.Contains(new Baba.Application.Importing.ImportIssue(3, "employee.code-duplicate"), bad.Issues);
        Assert.Contains(new Baba.Application.Importing.ImportIssue(4, "employee.join-required"), bad.Issues);
        Assert.Contains(new Baba.Application.Importing.ImportIssue(5, "employee.basic-invalid"), bad.Issues);
        Assert.Equal(2, (await e.Employees.ListAsync()).Count);
    }
}
