using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Payroll;
using Baba.Localization;

namespace Baba.Application.Payroll;

public sealed record PayrollSettingsInput(
    bool AutoPost,
    decimal NationalEmployeePercent,
    decimal NationalEmployerPercent,
    decimal ForeignEmployeePercent,
    decimal ForeignEmployerPercent,
    decimal? InsuranceFloor,
    decimal? InsuranceCeiling,
    Guid? SalaryExpenseAccountId,
    Guid? SalariesPayableAccountId,
    Guid? InsuranceExpenseAccountId,
    Guid? InsurancePayableAccountId,
    Guid? EndOfServiceExpenseAccountId,
    Guid? EndOfServiceProvisionAccountId);

public sealed record PayrollSettingsDto(
    DateOnly? StartMonth,
    bool AutoPost,
    decimal NationalEmployeePercent,
    decimal NationalEmployerPercent,
    decimal ForeignEmployeePercent,
    decimal ForeignEmployerPercent,
    decimal? InsuranceFloor,
    decimal? InsuranceCeiling,
    Guid? SalaryExpenseAccountId,
    Guid? SalariesPayableAccountId,
    Guid? InsuranceExpenseAccountId,
    Guid? InsurancePayableAccountId,
    Guid? EndOfServiceExpenseAccountId,
    Guid? EndOfServiceProvisionAccountId,
    /// <summary>The country has a social insurance scheme (its name, for the screen).</summary>
    string? InsuranceNameEn,
    string? InsuranceNameAr,
    /// <summary>The country pays an end-of-service gratuity.</summary>
    bool HasEndOfService,
    string? EndOfServiceNameEn,
    string? EndOfServiceNameAr);

public sealed record PayslipItemInput(
    SalaryComponentKind Kind,
    string Label,
    string? LabelAr,
    decimal Amount,
    Guid? AccountId,
    bool IsInsurable,
    bool InEndOfService,
    Guid? ComponentId = null,
    bool IsBasic = false);

public sealed record PayslipItemDto(
    SalaryComponentKind Kind, string Label, string LabelAr, decimal Amount, Guid? AccountId, bool IsInsurable, bool InEndOfService, Guid? ComponentId, bool IsBasic);

public sealed record PayslipDto(
    Guid Id,
    Guid EmployeeId,
    IReadOnlyList<PayslipItemDto> Items,
    decimal Earnings,
    decimal Deductions,
    decimal EmployeeInsurance,
    decimal EmployerInsurance,
    decimal Net);

public sealed record PayrollRunSummary(
    Guid Id,
    DateOnly Month,
    PayrollStatus Status,
    int Employees,
    decimal Earnings,
    decimal Deductions,
    decimal EmployeeInsurance,
    decimal EmployerInsurance,
    decimal Net,
    Guid? VoucherId,
    DateOnly? PaidDate,
    string? Memo);

public sealed record PayrollRunDto(PayrollRunSummary Summary, IReadOnlyList<PayslipDto> Payslips);

public sealed record PayInput(DateOnly Date, Guid CashAccountId);

/// <summary>A month the automatic run made (and whether it posted it), or why it stopped.</summary>
public sealed record PayrollRunItem(DateOnly Month, Guid? RunId, bool Posted, string? ProblemCode);

public sealed record PayrollAutoResult(IReadOnlyList<PayrollRunItem> Items);

public sealed record EndOfServiceLine(Guid EmployeeId, decimal Wage, decimal YearsOfService, decimal Gratuity);

/// <summary>What the company would owe its employees as end-of-service on a date, against what the ledger has set aside.</summary>
public sealed record EndOfServicePosition(DateOnly AsOf, bool Applicable, IReadOnlyList<EndOfServiceLine> Lines, decimal Required, decimal Provision, decimal Difference);

public sealed record EndOfServiceResult(Guid? VoucherId, decimal Amount);

/// <summary>
/// Payroll (brief section 10.4): a payslip for each employee each month from their salary and components, the country's social
/// insurance, one voucher for the month, payment of the net salaries, and the end-of-service provision. Months that have ended are made
/// (and, if the company chose, posted) by themselves once payroll has been started, so the books stay current without anyone remembering.
/// </summary>
public sealed class PayrollService(
    IPayrollStore store,
    IAccountStore accounts,
    ILedgerQuery ledger,
    VoucherService vouchers,
    CountryPackRegistry countryPacks,
    ICompanyFiles files,
    TimeProvider clock)
{
    // The runs when a company opens and the buttons on the screen can happen together; one at a time, so a month is never made twice.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private async Task<T> InGateAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await work();
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------------------------------------------------------------- Settings

    private CompanyInfo CompanyOrThrow() => files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");

    private IPayrollRules? Rules() => countryPacks.Find(CompanyOrThrow().CountryCode)?.Payroll;

    private Currency BaseCurrency()
    {
        var company = CompanyOrThrow();
        return CurrencyCatalog.Find(company.BaseCurrencyCode)?.Currency ?? new Currency(company.BaseCurrencyCode, 2);
    }

    /// <summary>The company's settings; the first time, they are made from the country's insurance scheme and the accounts with the matching special use.</summary>
    private async Task<PayrollSettings> SettingsAsync(CancellationToken cancellationToken)
    {
        if (await store.GetSettingsAsync(cancellationToken) is { } existing)
            return existing;

        var rules = Rules();
        var chart = (await accounts.ListAsync(cancellationToken)).Where(a => a.IsPosting && a.IsActive).OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).ToList();
        Guid? Role(AccountRole role) => chart.FirstOrDefault(a => a.Role == role)?.Id;
        var nationals = rules?.InsuranceForNationals;
        var foreigners = rules?.InsuranceForForeigners;
        var settings = new PayrollSettings
        {
            CompanyId = CompanyOrThrow().Id,
            AutoPost = true,
            NationalEmployeePercentScaled = Scaled.ToScaled(nationals?.EmployeePercent ?? 0),
            NationalEmployerPercentScaled = Scaled.ToScaled(nationals?.EmployerPercent ?? 0),
            ForeignEmployeePercentScaled = Scaled.ToScaled(foreigners?.EmployeePercent ?? 0),
            ForeignEmployerPercentScaled = Scaled.ToScaled(foreigners?.EmployerPercent ?? 0),
            InsuranceFloorScaled = (nationals ?? foreigners)?.MonthlyFloor is { } floor ? Scaled.ToScaled(floor) : null,
            InsuranceCeilingScaled = (nationals ?? foreigners)?.MonthlyCeiling is { } ceiling ? Scaled.ToScaled(ceiling) : null,
            SalaryExpenseAccountId = Role(AccountRole.SalaryExpense),
            SalariesPayableAccountId = Role(AccountRole.SalariesPayable),
            InsuranceExpenseAccountId = Role(AccountRole.SocialInsuranceExpense),
            InsurancePayableAccountId = Role(AccountRole.SocialInsurancePayable),
            EndOfServiceExpenseAccountId = Role(AccountRole.EndOfServiceExpense),
            EndOfServiceProvisionAccountId = Role(AccountRole.EndOfServiceProvision),
        };
        await store.SaveSettingsAsync(settings, cancellationToken);
        return settings;
    }

    public async Task<PayrollSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) => ToDto(await SettingsAsync(cancellationToken));

    public async Task<PayrollSettingsDto> SaveSettingsAsync(PayrollSettingsInput input, CancellationToken cancellationToken = default)
    {
        var settings = await SettingsAsync(cancellationToken);
        var byId = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        var issues = new List<ValidationIssue>();

        void Percent(string field, decimal value)
        {
            if (value is < 0 or > 100)
                issues.Add(new(field, "payroll.percent-invalid"));
        }

        Percent("nationalEmployeePercent", input.NationalEmployeePercent);
        Percent("nationalEmployerPercent", input.NationalEmployerPercent);
        Percent("foreignEmployeePercent", input.ForeignEmployeePercent);
        Percent("foreignEmployerPercent", input.ForeignEmployerPercent);
        if (input.InsuranceFloor is < 0 || input.InsuranceCeiling is < 0 || input.InsuranceFloor > input.InsuranceCeiling)
            issues.Add(new("insuranceCeiling", "payroll.limits-invalid"));

        void Account(string field, Guid? id, AccountType type)
        {
            if (id is { } value && value != Guid.Empty && (!byId.TryGetValue(value, out var account) || !account.IsPosting || !account.IsActive || account.Type != type))
                issues.Add(new(field, "payroll.account-invalid"));
        }

        Account("salaryExpenseAccount", input.SalaryExpenseAccountId, AccountType.Expense);
        Account("salariesPayableAccount", input.SalariesPayableAccountId, AccountType.Liability);
        Account("insuranceExpenseAccount", input.InsuranceExpenseAccountId, AccountType.Expense);
        Account("insurancePayableAccount", input.InsurancePayableAccountId, AccountType.Liability);
        Account("endOfServiceExpenseAccount", input.EndOfServiceExpenseAccountId, AccountType.Expense);
        Account("endOfServiceProvisionAccount", input.EndOfServiceProvisionAccountId, AccountType.Liability);
        if (issues.Count > 0)
            throw new ValidationException(issues);

        Guid? Id(Guid? id) => id == Guid.Empty ? null : id;
        settings.AutoPost = input.AutoPost;
        settings.NationalEmployeePercentScaled = Scaled.ToScaled(input.NationalEmployeePercent);
        settings.NationalEmployerPercentScaled = Scaled.ToScaled(input.NationalEmployerPercent);
        settings.ForeignEmployeePercentScaled = Scaled.ToScaled(input.ForeignEmployeePercent);
        settings.ForeignEmployerPercentScaled = Scaled.ToScaled(input.ForeignEmployerPercent);
        settings.InsuranceFloorScaled = input.InsuranceFloor is { } f ? Scaled.ToScaled(f) : null;
        settings.InsuranceCeilingScaled = input.InsuranceCeiling is { } c ? Scaled.ToScaled(c) : null;
        settings.SalaryExpenseAccountId = Id(input.SalaryExpenseAccountId);
        settings.SalariesPayableAccountId = Id(input.SalariesPayableAccountId);
        settings.InsuranceExpenseAccountId = Id(input.InsuranceExpenseAccountId);
        settings.InsurancePayableAccountId = Id(input.InsurancePayableAccountId);
        settings.EndOfServiceExpenseAccountId = Id(input.EndOfServiceExpenseAccountId);
        settings.EndOfServiceProvisionAccountId = Id(input.EndOfServiceProvisionAccountId);
        await store.SaveSettingsAsync(settings, cancellationToken);
        return ToDto(settings);
    }

    private PayrollSettingsDto ToDto(PayrollSettings s)
    {
        var rules = Rules();
        var scheme = rules?.InsuranceForNationals ?? rules?.InsuranceForForeigners;
        return new PayrollSettingsDto(
            s.StartMonth, s.AutoPost, Scaled.ToDecimal(s.NationalEmployeePercentScaled), Scaled.ToDecimal(s.NationalEmployerPercentScaled),
            Scaled.ToDecimal(s.ForeignEmployeePercentScaled), Scaled.ToDecimal(s.ForeignEmployerPercentScaled),
            s.InsuranceFloorScaled is { } f ? Scaled.ToDecimal(f) : null, s.InsuranceCeilingScaled is { } c ? Scaled.ToDecimal(c) : null,
            s.SalaryExpenseAccountId, s.SalariesPayableAccountId, s.InsuranceExpenseAccountId, s.InsurancePayableAccountId,
            s.EndOfServiceExpenseAccountId, s.EndOfServiceProvisionAccountId,
            scheme?.NameEn, scheme?.NameAr, rules?.EndOfService is not null, rules?.EndOfService?.NameEn, rules?.EndOfService?.NameAr);
    }

    // ---------------------------------------------------------------- Runs

    public async Task<IReadOnlyList<PayrollRunSummary>> ListRunsAsync(CancellationToken cancellationToken = default) =>
        (await store.ListRunsAsync(cancellationToken)).OrderByDescending(r => r.Month).Select(Summarize).ToList();

    public async Task<PayrollRunDto?> GetRunAsync(Guid id, CancellationToken cancellationToken = default) =>
        await store.FindRunAsync(id, cancellationToken) is { } run ? ToDto(run) : null;

    /// <summary>Starts a month: a payslip for every employee who worked in it, from what the employees say today. A draft until posted.</summary>
    public Task<PayrollRunDto> CreateRunAsync(DateOnly month, CancellationToken cancellationToken = default) =>
        InGateAsync(() => CreateRunCoreAsync(month, cancellationToken), cancellationToken);

    private async Task<PayrollRunDto> CreateRunCoreAsync(DateOnly month, CancellationToken cancellationToken)
    {
        var company = CompanyOrThrow();
        month = new DateOnly(month.Year, month.Month, 1);
        if ((await store.ListRunsAsync(cancellationToken)).Any(r => r.Month == month))
            throw Refused("month", "payroll.run-exists");

        var settings = await SettingsAsync(cancellationToken);
        var run = new PayrollRun { CompanyId = company.Id, Month = month, Status = PayrollStatus.Draft };
        run.Payslips = await BuildPayslipsAsync(run, month, settings, cancellationToken);
        if (run.Payslips.Count == 0)
            throw Refused("month", "payroll.no-employees");

        await store.AddRunAsync(run, cancellationToken);
        if (settings.StartMonth is null || settings.StartMonth > month)
        {
            settings.StartMonth = month;
            await store.SaveSettingsAsync(settings, cancellationToken);
        }

        return ToDto(run);
    }

    /// <summary>Makes the payslips again from the employees' current pay, throwing away changes made to a draft's payslips.</summary>
    public async Task<PayrollRunDto> RefreshRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await store.FindRunAsync(id, cancellationToken) ?? throw new NotFoundException("payroll-run");
        if (run.Status != PayrollStatus.Draft)
            throw Refused("run", "payroll.not-draft");

        run.Payslips = await BuildPayslipsAsync(run, run.Month, await SettingsAsync(cancellationToken), cancellationToken);
        await store.UpdateRunAsync(run, cancellationToken);
        return ToDto(run);
    }

    /// <summary>Changes the lines of one payslip (an overtime allowance, a deduction). The social insurance follows. A posted run is posted again.</summary>
    public async Task<PayrollRunDto> SavePayslipAsync(Guid runId, Guid payslipId, IReadOnlyList<PayslipItemInput> items, CancellationToken cancellationToken = default)
    {
        var run = await store.FindRunAsync(runId, cancellationToken) ?? throw new NotFoundException("payroll-run");
        var payslip = run.Payslips.FirstOrDefault(p => p.Id == payslipId) ?? throw new NotFoundException("payslip");
        var employee = await store.FindEmployeeAsync(payslip.EmployeeId, cancellationToken);
        var byId = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        var issues = new List<ValidationIssue>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (string.IsNullOrWhiteSpace(item.Label) && string.IsNullOrWhiteSpace(item.LabelAr))
                issues.Add(new($"items[{i}].label", "payslip.label-required"));
            if (item.Amount < 0)
                issues.Add(new($"items[{i}].amount", "payslip.amount-invalid"));
            if (item.AccountId is { } account && account != Guid.Empty && (!byId.TryGetValue(account, out var a) || !a.IsPosting || !a.IsActive))
                issues.Add(new($"items[{i}].account", "payslip.account-invalid"));
            else if (item.Kind == SalaryComponentKind.Deduction && (item.AccountId is null || item.AccountId == Guid.Empty))
                issues.Add(new($"items[{i}].account", "payslip.account-required"));
        }

        if (issues.Count > 0)
            throw new ValidationException(issues);

        var company = CompanyOrThrow();
        payslip.Items = [.. items.Select((item, index) => new PayslipItem
        {
            CompanyId = company.Id,
            PayslipId = payslip.Id,
            LineNumber = index + 1,
            Kind = item.Kind,
            Label = item.Label?.Trim() ?? "",
            LabelAr = string.IsNullOrWhiteSpace(item.LabelAr) ? item.Label?.Trim() ?? "" : item.LabelAr.Trim(),
            ComponentId = item.ComponentId,
            Amount = item.Amount,
            AccountId = item.AccountId == Guid.Empty ? null : item.AccountId,
            IsInsurable = item.IsInsurable,
            InEndOfService = item.InEndOfService,
            IsBasic = item.IsBasic,
        })];
        ApplyInsurance(payslip, employee?.IsNational ?? false, await SettingsAsync(cancellationToken));

        await SaveAndRepostAsync(run, cancellationToken);
        return ToDto(run);
    }

    /// <summary>Leaves an employee out of a month (not paid this month). A posted run is posted again.</summary>
    public async Task<PayrollRunDto> RemovePayslipAsync(Guid runId, Guid payslipId, CancellationToken cancellationToken = default)
    {
        var run = await store.FindRunAsync(runId, cancellationToken) ?? throw new NotFoundException("payroll-run");
        if (run.Payslips.RemoveAll(p => p.Id == payslipId) == 0)
            throw new NotFoundException("payslip");
        if (run.Payslips.Count == 0)
            throw Refused("run", "payroll.no-employees");

        await SaveAndRepostAsync(run, cancellationToken);
        return ToDto(run);
    }

    private async Task SaveAndRepostAsync(PayrollRun run, CancellationToken cancellationToken)
    {
        if (run.Status == PayrollStatus.Posted)
        {
            if (run.PaymentVoucherId is not null)
                throw Refused("run", "payroll.paid"); // a paid month is changed by taking the payment back first
            run.VoucherId = await PostVoucherAsync(run, cancellationToken);
        }

        await store.UpdateRunAsync(run, cancellationToken);
    }

    public Task<PayrollRunDto> PostRunAsync(Guid id, CancellationToken cancellationToken = default) =>
        InGateAsync(() => PostRunCoreAsync(id, cancellationToken), cancellationToken);

    private async Task<PayrollRunDto> PostRunCoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await store.FindRunAsync(id, cancellationToken) ?? throw new NotFoundException("payroll-run");
        if (run.PaymentVoucherId is not null)
            throw Refused("run", "payroll.paid");

        run.VoucherId = await PostVoucherAsync(run, cancellationToken);
        run.Status = PayrollStatus.Posted;
        await store.UpdateRunAsync(run, cancellationToken);
        return ToDto(run);
    }

    public async Task<PayrollRunDto> UnpostRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await store.FindRunAsync(id, cancellationToken) ?? throw new NotFoundException("payroll-run");
        if (run.PaymentVoucherId is not null)
            throw Refused("run", "payroll.paid");

        if (run.VoucherId is { } voucherId)
            await vouchers.DeleteSystemAsync(voucherId, cancellationToken);
        run.VoucherId = null;
        run.Status = PayrollStatus.Draft;
        await store.UpdateRunAsync(run, cancellationToken);
        return ToDto(run);
    }

    public async Task DeleteRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await store.FindRunAsync(id, cancellationToken) ?? throw new NotFoundException("payroll-run");
        if (run.PaymentVoucherId is not null)
            throw Refused("run", "payroll.paid");

        if (run.VoucherId is { } voucherId)
            await vouchers.DeleteSystemAsync(voucherId, cancellationToken);
        await store.DeleteRunAsync(id, cancellationToken);
    }

    /// <summary>Pays the net salaries of a posted month out of a bank or cash account: what was owed to the employees is cleared.</summary>
    public async Task<PayrollRunDto> PayRunAsync(Guid id, PayInput input, CancellationToken cancellationToken = default)
    {
        var run = await store.FindRunAsync(id, cancellationToken) ?? throw new NotFoundException("payroll-run");
        if (run.Status != PayrollStatus.Posted)
            throw Refused("run", "payroll.not-posted");
        if (run.PaymentVoucherId is not null)
            throw Refused("run", "payroll.paid");

        var settings = await SettingsAsync(cancellationToken);
        var chart = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        if (!chart.TryGetValue(input.CashAccountId, out var cash) || cash.Role != AccountRole.CashOrBank || !cash.IsPosting || !cash.IsActive)
            throw Refused("cashAccount", "payroll.cash-account-invalid");
        if (settings.SalariesPayableAccountId is not { } payable)
            throw Refused("settings", "payroll.accounts-missing");

        var net = run.Payslips.Sum(p => p.Net);
        if (net <= 0)
            throw Refused("run", "payroll.nothing-to-pay");

        var memo = $"Salaries {run.Month:yyyy-MM}";
        var voucher = await vouchers.SaveAndPostSystemAsync(
            new VoucherInput(VoucherKind.SalaryPayment, input.Date, null, $"{run.Month:yyyy-MM}", memo,
                [new VoucherLineInput(null, payable, memo, net, 0), new VoucherLineInput(null, input.CashAccountId, memo, 0, net)]),
            null, null, cancellationToken);
        run.PaymentVoucherId = voucher.Id;
        run.PaidDate = input.Date;
        await store.UpdateRunAsync(run, cancellationToken);
        return ToDto(run);
    }

    public async Task<PayrollRunDto> UnpayRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await store.FindRunAsync(id, cancellationToken) ?? throw new NotFoundException("payroll-run");
        if (run.PaymentVoucherId is not { } voucherId)
            throw Refused("run", "payroll.not-paid");

        await vouchers.DeleteSystemAsync(voucherId, cancellationToken);
        run.PaymentVoucherId = null;
        run.PaidDate = null;
        await store.UpdateRunAsync(run, cancellationToken);
        return ToDto(run);
    }

    /// <summary>The last day of the month before the current one.</summary>
    public DateOnly LastCompletedMonthStart()
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        return new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
    }

    /// <summary>
    /// The run that happens when a company is opened: once payroll has been started, every later month that has ended is made, and posted
    /// when the company chose that. It stops at the first month that cannot be made (a locked month, a missing account).
    /// </summary>
    public Task<PayrollAutoResult> RunDueAsync(CancellationToken cancellationToken = default) =>
        InGateAsync(() => RunDueCoreAsync(cancellationToken), cancellationToken);

    private async Task<PayrollAutoResult> RunDueCoreAsync(CancellationToken cancellationToken)
    {
        var items = new List<PayrollRunItem>();
        var settings = await SettingsAsync(cancellationToken);
        if (settings.StartMonth is not { } start)
            return new PayrollAutoResult(items);

        var runs = await store.ListRunsAsync(cancellationToken);
        var first = runs.Count == 0 ? start : runs.Max(r => r.Month).AddMonths(1);
        for (var month = first; month <= LastCompletedMonthStart(); month = month.AddMonths(1))
        {
            if (runs.Any(r => r.Month == month))
                continue;

            try
            {
                var run = await CreateRunCoreAsync(month, cancellationToken);
                var posted = false;
                if (settings.AutoPost)
                {
                    await PostRunCoreAsync(run.Summary.Id, cancellationToken);
                    posted = true;
                }

                items.Add(new PayrollRunItem(month, run.Summary.Id, posted, null));
            }
            catch (ValidationException e) when (e.Issues.Any(i => i.Code == "payroll.no-employees"))
            {
                continue; // nobody worked that month
            }
            catch (ValidationException e)
            {
                items.Add(new PayrollRunItem(month, null, false, e.Issues.FirstOrDefault()?.Code ?? "payroll.run-failed"));
                break;
            }
        }

        return new PayrollAutoResult(items);
    }

    // ---------------------------------------------------------------- Making the payslips

    private async Task<List<Payslip>> BuildPayslipsAsync(PayrollRun run, DateOnly month, PayrollSettings settings, CancellationToken cancellationToken)
    {
        var company = CompanyOrThrow();
        var currency = BaseCurrency();
        var components = (await store.ListComponentsAsync(cancellationToken)).ToDictionary(c => c.Id);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        var payslips = new List<Payslip>();

        foreach (var employee in (await store.ListEmployeesAsync(cancellationToken)).Where(e => e.IsActive).OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase))
        {
            if (employee.JoinDate > monthEnd || employee.LeaveDate is { } left && left < month)
                continue;

            var from = employee.JoinDate > month ? employee.JoinDate : month;
            var to = employee.LeaveDate is { } end && end < monthEnd ? end : monthEnd;
            var factor = (to.DayNumber - from.DayNumber + 1) / (decimal)days; // part of the month worked

            var payslip = new Payslip { CompanyId = company.Id, RunId = run.Id, EmployeeId = employee.Id };
            var number = 1;
            var basic = Money.Round(employee.BasicSalary * factor, currency);
            payslip.Items.Add(new PayslipItem
            {
                CompanyId = company.Id, PayslipId = payslip.Id, LineNumber = number++, Kind = SalaryComponentKind.Earning, Label = "Basic salary", LabelAr = "الراتب الأساسي",
                Amount = basic, AccountId = settings.SalaryExpenseAccountId, IsInsurable = true, InEndOfService = true, IsBasic = true,
            });

            foreach (var assigned in employee.Components)
            {
                if (!components.TryGetValue(assigned.ComponentId, out var component))
                    continue;

                var amount = component.Calculation == ComponentCalculation.PercentOfBasic
                    ? Money.Round(basic * assigned.Value / 100m, currency)
                    : Money.Round(assigned.Value * factor, currency);
                if (amount == 0)
                    continue;

                payslip.Items.Add(new PayslipItem
                {
                    CompanyId = company.Id, PayslipId = payslip.Id, LineNumber = number++, Kind = component.Kind, Label = component.NameEn, LabelAr = component.NameAr,
                    ComponentId = component.Id, Amount = amount, AccountId = component.AccountId, IsInsurable = component.IsInsurable, InEndOfService = component.InEndOfService,
                });
            }

            ApplyInsurance(payslip, employee.IsNational, settings);
            payslips.Add(payslip);
        }

        return payslips;
    }

    /// <summary>Works out the insurance both sides pay on the insurable part of the pay, within the scheme's limits.</summary>
    private void ApplyInsurance(Payslip payslip, bool national, PayrollSettings settings)
    {
        var currency = BaseCurrency();
        var employeePercent = Scaled.ToDecimal(national ? settings.NationalEmployeePercentScaled : settings.ForeignEmployeePercentScaled);
        var employerPercent = Scaled.ToDecimal(national ? settings.NationalEmployerPercentScaled : settings.ForeignEmployerPercentScaled);
        var wage = payslip.Items.Where(i => i is { Kind: SalaryComponentKind.Earning, IsInsurable: true }).Sum(i => i.Amount);

        if (wage <= 0 || employeePercent == 0 && employerPercent == 0)
        {
            payslip.EmployeeInsurance = 0;
            payslip.EmployerInsurance = 0;
            return;
        }

        var basis = wage;
        if (settings.InsuranceFloorScaled is { } floor && basis < Scaled.ToDecimal(floor))
            basis = Scaled.ToDecimal(floor);
        if (settings.InsuranceCeilingScaled is { } ceiling && basis > Scaled.ToDecimal(ceiling))
            basis = Scaled.ToDecimal(ceiling);

        payslip.EmployeeInsurance = Money.Round(basis * employeePercent / 100m, currency);
        payslip.EmployerInsurance = Money.Round(basis * employerPercent / 100m, currency);
    }

    // ---------------------------------------------------------------- Posting

    private async Task<Guid> PostVoucherAsync(PayrollRun run, CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        var employees = (await store.ListEmployeesAsync(cancellationToken)).ToDictionary(e => e.Id);
        var issues = new List<ValidationIssue>();

        if (settings.SalariesPayableAccountId is null || settings.SalaryExpenseAccountId is null)
            issues.Add(new("settings", "payroll.accounts-missing"));
        var anyInsurance = run.Payslips.Any(p => p.EmployeeInsuranceScaled != 0 || p.EmployerInsuranceScaled != 0);
        if (anyInsurance && (settings.InsurancePayableAccountId is null || run.Payslips.Any(p => p.EmployerInsuranceScaled != 0) && settings.InsuranceExpenseAccountId is null))
            issues.Add(new("settings", "payroll.accounts-missing"));
        if (issues.Count > 0)
            throw new ValidationException(issues);

        var lines = new List<VoucherLineInput>();
        for (var index = 0; index < run.Payslips.Count; index++)
        {
            var payslip = run.Payslips[index];
            employees.TryGetValue(payslip.EmployeeId, out var employee);
            var who = employee is null ? "" : $"{employee.Code} {employee.NameEn}".Trim();
            Guid? center = employee?.CostCenterId;

            foreach (var item in payslip.Items.Where(i => i is { Kind: SalaryComponentKind.Earning } && i.Amount > 0))
                lines.Add(new VoucherLineInput(null, item.AccountId ?? settings.SalaryExpenseAccountId!.Value, $"{item.Label} — {who}", item.Amount, 0, null, center));
            if (payslip.EmployerInsurance > 0)
                lines.Add(new VoucherLineInput(null, settings.InsuranceExpenseAccountId!.Value, $"Employer insurance — {who}", payslip.EmployerInsurance, 0, null, center));

            if (payslip.Net < 0)
                throw new ValidationException([new ValidationIssue($"payslips[{index}]", "payroll.net-negative")]);
            if (payslip.Net > 0)
                lines.Add(new VoucherLineInput(null, settings.SalariesPayableAccountId!.Value, $"Net salary — {who}", 0, payslip.Net));

            var insurance = payslip.EmployeeInsurance + payslip.EmployerInsurance;
            if (insurance > 0)
                lines.Add(new VoucherLineInput(null, settings.InsurancePayableAccountId!.Value, $"Social insurance — {who}", 0, insurance));
            foreach (var item in payslip.Items.Where(i => i is { Kind: SalaryComponentKind.Deduction } && i.Amount > 0))
                lines.Add(new VoucherLineInput(null, item.AccountId ?? throw new ValidationException([new ValidationIssue($"payslips[{index}]", "payslip.account-required")]), $"{item.Label} — {who}", 0, item.Amount));
        }

        var monthEnd = run.Month.AddMonths(1).AddDays(-1);
        var voucher = await vouchers.SaveAndPostSystemAsync(
            new VoucherInput(VoucherKind.Payroll, monthEnd, null, $"{run.Month:yyyy-MM}", $"Payroll {run.Month:yyyy-MM}", lines), run.VoucherId, null, cancellationToken);
        return voucher.Id;
    }

    // ---------------------------------------------------------------- End-of-service provision

    /// <summary>What the employees' end-of-service gratuity comes to on a date, and what the ledger holds against it.</summary>
    public async Task<EndOfServicePosition> EndOfServicePositionAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var rules = Rules()?.EndOfService;
        if (rules is null)
            return new EndOfServicePosition(asOf, false, [], 0, 0, 0);

        var settings = await SettingsAsync(cancellationToken);
        var components = (await store.ListComponentsAsync(cancellationToken)).ToDictionary(c => c.Id);
        var currency = BaseCurrency();
        var lines = new List<EndOfServiceLine>();
        foreach (var employee in (await store.ListEmployeesAsync(cancellationToken)).Where(e => e.IsActive && e.JoinDate <= asOf && (e.LeaveDate is null || e.LeaveDate > asOf)).OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase))
        {
            var wage = employee.BasicSalary;
            foreach (var assigned in employee.Components)
            {
                if (!components.TryGetValue(assigned.ComponentId, out var component) || !component.InEndOfService || component.Kind != SalaryComponentKind.Earning)
                    continue;
                wage += component.Calculation == ComponentCalculation.PercentOfBasic ? employee.BasicSalary * assigned.Value / 100m : assigned.Value;
            }

            var years = Math.Round((asOf.DayNumber - employee.JoinDate.DayNumber) / 365.25m, 4);
            lines.Add(new EndOfServiceLine(employee.Id, wage, years, Money.Round(rules.Gratuity(wage, years), currency)));
        }

        var provision = 0m;
        if (settings.EndOfServiceProvisionAccountId is { } account && (await ledger.TotalsAsync(null, asOf, cancellationToken)).FirstOrDefault(t => t.AccountId == account) is { } total)
            provision = total.Credit - total.Debit;

        var required = lines.Sum(l => l.Gratuity);
        return new EndOfServicePosition(asOf, true, lines, required, provision, required - provision);
    }

    /// <summary>Posts what is needed to bring the provision to what is owed on the last day of a month (a release when it is too high).</summary>
    public Task<EndOfServiceResult> AccrueEndOfServiceAsync(DateOnly month, CancellationToken cancellationToken = default) =>
        InGateAsync(() => AccrueCoreAsync(month, cancellationToken), cancellationToken);

    private async Task<EndOfServiceResult> AccrueCoreAsync(DateOnly month, CancellationToken cancellationToken)
    {
        var company = CompanyOrThrow();
        month = new DateOnly(month.Year, month.Month, 1);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        if ((await store.ListAccrualsAsync(cancellationToken)).Any(a => a.Month == month))
            throw Refused("month", "payroll.eos-done");

        var position = await EndOfServicePositionAsync(monthEnd, cancellationToken);
        if (!position.Applicable)
            throw Refused("settings", "payroll.eos-not-applicable");

        var settings = await SettingsAsync(cancellationToken);
        if (settings.EndOfServiceExpenseAccountId is not { } expense || settings.EndOfServiceProvisionAccountId is not { } provision)
            throw Refused("settings", "payroll.accounts-missing");

        var amount = position.Difference;
        if (Math.Abs(amount) < Scaled.ToDecimal(1))
            return new EndOfServiceResult(null, 0);

        var memo = $"End-of-service provision {month:yyyy-MM}";
        var lines = amount > 0
            ? new[] { new VoucherLineInput(null, expense, memo, amount, 0), new VoucherLineInput(null, provision, memo, 0, amount) }
            : new[] { new VoucherLineInput(null, provision, memo, -amount, 0), new VoucherLineInput(null, expense, memo, 0, -amount) };
        var voucher = await vouchers.SaveAndPostSystemAsync(new VoucherInput(VoucherKind.EndOfServiceAccrual, monthEnd, null, $"{month:yyyy-MM}", memo, lines), null, null, cancellationToken);
        await store.AddAccrualAsync(new EndOfServiceAccrual { CompanyId = company.Id, Month = month, Amount = amount, VoucherId = voucher.Id }, cancellationToken);
        return new EndOfServiceResult(voucher.Id, amount);
    }

    /// <summary>Takes back the latest month's provision entry.</summary>
    public async Task UndoEndOfServiceAsync(CancellationToken cancellationToken = default)
    {
        var accruals = await store.ListAccrualsAsync(cancellationToken);
        if (accruals.Count == 0)
            throw Refused("month", "payroll.eos-nothing-to-undo");

        var latest = accruals.OrderByDescending(a => a.Month).First();
        await vouchers.DeleteSystemAsync(latest.VoucherId, cancellationToken);
        await store.DeleteAccrualAsync(latest.Id, cancellationToken);
    }

    /// <summary>The provision entry of the last month that ended, made when a company is opened (once payroll has been started).</summary>
    public Task<EndOfServiceResult?> RunDueEndOfServiceAsync(CancellationToken cancellationToken = default) =>
        InGateAsync(() => RunDueEndOfServiceCoreAsync(cancellationToken), cancellationToken);

    private async Task<EndOfServiceResult?> RunDueEndOfServiceCoreAsync(CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        var month = LastCompletedMonthStart();
        if (Rules()?.EndOfService is null || settings.StartMonth is not { } start || start > month)
            return null;
        if ((await store.ListAccrualsAsync(cancellationToken)).Any(a => a.Month == month))
            return null;

        try
        {
            return await AccrueCoreAsync(month, cancellationToken);
        }
        catch (ValidationException)
        {
            return null; // accounts or a locked month: the screen shows what is missing
        }
    }

    // ---------------------------------------------------------------- Helpers

    private static PayrollRunSummary Summarize(PayrollRun r) => new(
        r.Id, r.Month, r.Status, r.Payslips.Count, r.Payslips.Sum(p => p.Earnings), r.Payslips.Sum(p => p.Deductions), r.Payslips.Sum(p => p.EmployeeInsurance),
        r.Payslips.Sum(p => p.EmployerInsurance), r.Payslips.Sum(p => p.Net), r.VoucherId, r.PaidDate, r.Memo);

    private static PayrollRunDto ToDto(PayrollRun run) => new(
        Summarize(run),
        [.. run.Payslips.Select(p => new PayslipDto(
            p.Id, p.EmployeeId,
            [.. p.Items.OrderBy(i => i.LineNumber).Select(i => new PayslipItemDto(i.Kind, i.Label, i.LabelAr, i.Amount, i.AccountId, i.IsInsurable, i.InEndOfService, i.ComponentId, i.IsBasic))],
            p.Earnings, p.Deductions, p.EmployeeInsurance, p.EmployerInsurance, p.Net))]);

    private static ValidationException Refused(string field, string code) => new([new ValidationIssue(field, code)]);
}
