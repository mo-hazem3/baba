using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Domain.Payroll;

namespace Baba.Application.Payroll;

public sealed record EmployeeComponentInput(Guid ComponentId, decimal Value);

public sealed record EmployeeInput(
    string Code,
    string NameAr,
    string NameEn,
    string? JobTitle,
    string? NationalId,
    bool IsNational,
    DateOnly JoinDate,
    DateOnly? LeaveDate,
    decimal BasicSalary,
    string? BankName,
    string? BankAccount,
    Guid? CostCenterId,
    int AnnualLeaveDays,
    decimal LeaveBalanceDays,
    DateOnly? LeaveBalanceDate,
    string? Notes,
    IReadOnlyList<EmployeeComponentInput> Components);

public sealed record EmployeeDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    string? JobTitle,
    string? NationalId,
    bool IsNational,
    DateOnly JoinDate,
    DateOnly? LeaveDate,
    decimal BasicSalary,
    string? BankName,
    string? BankAccount,
    Guid? CostCenterId,
    int AnnualLeaveDays,
    decimal LeaveBalanceDays,
    DateOnly? LeaveBalanceDate,
    bool IsActive,
    string? Notes,
    IReadOnlyList<EmployeeComponentInput> Components,
    bool InUse);

public sealed record SalaryComponentInput(
    string Code,
    string NameAr,
    string NameEn,
    SalaryComponentKind Kind,
    ComponentCalculation Calculation,
    decimal DefaultValue,
    Guid? AccountId,
    bool IsInsurable,
    bool InEndOfService);

public sealed record SalaryComponentDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    SalaryComponentKind Kind,
    ComponentCalculation Calculation,
    decimal DefaultValue,
    Guid? AccountId,
    bool IsInsurable,
    bool InEndOfService,
    bool IsActive,
    bool InUse);

public sealed record LeaveInput(Guid EmployeeId, LeaveKind Kind, DateOnly From, DateOnly To, decimal? Days, string? Notes);

public sealed record LeaveDto(Guid Id, Guid EmployeeId, LeaveKind Kind, DateOnly From, DateOnly To, decimal Days, string? Notes);

/// <summary>Annual leave earned, taken and left for one employee on a date.</summary>
public sealed record LeaveBalanceDto(Guid EmployeeId, decimal Earned, decimal Taken, decimal Balance);

/// <summary>Employees, the salary components they can be given, and their leave (brief section 10.4).</summary>
public sealed class EmployeeService(IPayrollStore store, IAccountStore accounts, ICostCenterStore costCenters, ICompanyFiles files)
{
    // ---------------------------------------------------------------- Employees

    public async Task<IReadOnlyList<EmployeeDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var inUse = await store.EmployeeIdsInUseAsync(cancellationToken);
        return (await store.ListEmployeesAsync(cancellationToken)).OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase).Select(e => ToDto(e, inUse.Contains(e.Id))).ToList();
    }

    public async Task<EmployeeDto> CreateAsync(EmployeeInput input, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var employee = new Employee { CompanyId = company.Id };
        await ApplyAsync(employee, input, cancellationToken);
        await store.AddEmployeeAsync(employee, cancellationToken);
        return ToDto(employee, false);
    }

    public async Task<EmployeeDto> UpdateAsync(Guid id, EmployeeInput input, CancellationToken cancellationToken = default)
    {
        var employee = await store.FindEmployeeAsync(id, cancellationToken) ?? throw new NotFoundException("employee");
        await ApplyAsync(employee, input, cancellationToken);
        await store.UpdateEmployeeAsync(employee, cancellationToken);
        return ToDto(employee, (await store.EmployeeIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task<EmployeeDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var employee = await store.FindEmployeeAsync(id, cancellationToken) ?? throw new NotFoundException("employee");
        employee.IsActive = active;
        await store.UpdateEmployeeAsync(employee, cancellationToken);
        return ToDto(employee, (await store.EmployeeIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await store.FindEmployeeAsync(id, cancellationToken) ?? throw new NotFoundException("employee");
        if ((await store.EmployeeIdsInUseAsync(cancellationToken)).Contains(id))
            throw Refused("employee", "employee.in-use");
        await store.DeleteEmployeeAsync(id, cancellationToken);
    }

    private async Task ApplyAsync(Employee employee, EmployeeInput input, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        var all = await store.ListEmployeesAsync(cancellationToken);
        var code = input.Code?.Trim() ?? "";
        if (code.Length == 0)
            issues.Add(new("code", "employee.code-required"));
        else if (all.Any(e => e.Id != employee.Id && string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("code", "employee.code-duplicate"));

        var nameEn = input.NameEn?.Trim() ?? "";
        var nameAr = input.NameAr?.Trim() ?? "";
        if (nameEn.Length == 0 && nameAr.Length == 0)
            issues.Add(new("name", "employee.name-required"));
        if (nameAr.Length == 0) nameAr = nameEn;
        if (nameEn.Length == 0) nameEn = nameAr;

        if (input.JoinDate == default)
            issues.Add(new("joinDate", "employee.join-required"));
        if (input.LeaveDate is { } left && left < input.JoinDate)
            issues.Add(new("leaveDate", "employee.leave-before-join"));
        if (input.BasicSalary < 0)
            issues.Add(new("basicSalary", "employee.basic-invalid"));
        if (input.AnnualLeaveDays is < 0 or > 366)
            issues.Add(new("annualLeaveDays", "employee.leave-days-invalid"));
        if (input.CostCenterId is { } center && center != Guid.Empty && !(await costCenters.ListAsync(cancellationToken)).Any(c => c.Id == center))
            issues.Add(new("costCenter", "employee.cost-center-unknown"));

        var components = (await store.ListComponentsAsync(cancellationToken)).ToDictionary(c => c.Id);
        var seen = new HashSet<Guid>();
        var rows = new List<EmployeeComponent>();
        for (var i = 0; i < (input.Components?.Count ?? 0); i++)
        {
            var row = input.Components![i];
            if (!components.TryGetValue(row.ComponentId, out var component) || !component.IsActive && !employee.Components.Any(c => c.ComponentId == row.ComponentId))
                issues.Add(new($"components[{i}].component", "employee.component-unknown"));
            else if (!seen.Add(row.ComponentId))
                issues.Add(new($"components[{i}].component", "employee.component-twice"));
            if (row.Value < 0)
                issues.Add(new($"components[{i}].value", "employee.component-value-invalid"));
            rows.Add(new EmployeeComponent { CompanyId = employee.CompanyId, EmployeeId = employee.Id, ComponentId = row.ComponentId, Value = row.Value });
        }

        if (issues.Count > 0)
            throw new ValidationException(issues);

        employee.Code = code;
        employee.NameAr = nameAr;
        employee.NameEn = nameEn;
        employee.JobTitle = Clean(input.JobTitle);
        employee.NationalId = Clean(input.NationalId);
        employee.IsNational = input.IsNational;
        employee.JoinDate = input.JoinDate;
        employee.LeaveDate = input.LeaveDate;
        employee.BasicSalary = input.BasicSalary;
        employee.BankName = Clean(input.BankName);
        employee.BankAccount = Clean(input.BankAccount);
        employee.CostCenterId = input.CostCenterId == Guid.Empty ? null : input.CostCenterId;
        employee.AnnualLeaveDays = input.AnnualLeaveDays;
        employee.LeaveBalanceDays = input.LeaveBalanceDays;
        employee.LeaveBalanceDate = input.LeaveBalanceDate;
        employee.Notes = Clean(input.Notes);
        employee.Components = rows;
    }

    // ---------------------------------------------------------------- Components

    public async Task<IReadOnlyList<SalaryComponentDto>> ListComponentsAsync(CancellationToken cancellationToken = default)
    {
        var inUse = await store.ComponentIdsInUseAsync(cancellationToken);
        return (await store.ListComponentsAsync(cancellationToken)).OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase).Select(c => ToDto(c, inUse.Contains(c.Id))).ToList();
    }

    public async Task<SalaryComponentDto> CreateComponentAsync(SalaryComponentInput input, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var component = new SalaryComponent { CompanyId = company.Id };
        await ApplyAsync(component, input, cancellationToken);
        await store.AddComponentAsync(component, cancellationToken);
        return ToDto(component, false);
    }

    public async Task<SalaryComponentDto> UpdateComponentAsync(Guid id, SalaryComponentInput input, CancellationToken cancellationToken = default)
    {
        var component = (await store.ListComponentsAsync(cancellationToken)).FirstOrDefault(c => c.Id == id) ?? throw new NotFoundException("component");
        await ApplyAsync(component, input, cancellationToken);
        await store.UpdateComponentAsync(component, cancellationToken);
        return ToDto(component, (await store.ComponentIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task<SalaryComponentDto> SetComponentActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var component = (await store.ListComponentsAsync(cancellationToken)).FirstOrDefault(c => c.Id == id) ?? throw new NotFoundException("component");
        component.IsActive = active;
        await store.UpdateComponentAsync(component, cancellationToken);
        return ToDto(component, (await store.ComponentIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task DeleteComponentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = (await store.ListComponentsAsync(cancellationToken)).FirstOrDefault(c => c.Id == id) ?? throw new NotFoundException("component");
        if ((await store.ComponentIdsInUseAsync(cancellationToken)).Contains(id))
            throw Refused("component", "component.in-use");
        await store.DeleteComponentAsync(id, cancellationToken);
    }

    private async Task ApplyAsync(SalaryComponent component, SalaryComponentInput input, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        var all = await store.ListComponentsAsync(cancellationToken);
        var code = input.Code?.Trim() ?? "";
        if (code.Length == 0)
            issues.Add(new("code", "component.code-required"));
        else if (all.Any(c => c.Id != component.Id && string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("code", "component.code-duplicate"));

        var nameEn = input.NameEn?.Trim() ?? "";
        var nameAr = input.NameAr?.Trim() ?? "";
        if (nameEn.Length == 0 && nameAr.Length == 0)
            issues.Add(new("name", "component.name-required"));
        if (nameAr.Length == 0) nameAr = nameEn;
        if (nameEn.Length == 0) nameEn = nameAr;

        if (input.DefaultValue < 0 || input.Calculation == ComponentCalculation.PercentOfBasic && input.DefaultValue > 1000)
            issues.Add(new("defaultValue", "component.value-invalid"));

        if (input.AccountId is { } accountId && accountId != Guid.Empty)
        {
            var account = (await accounts.ListAsync(cancellationToken)).FirstOrDefault(a => a.Id == accountId);
            if (account is null || !account.IsPosting || !account.IsActive)
                issues.Add(new("account", "component.account-invalid"));
        }
        else if (input.Kind == SalaryComponentKind.Deduction)
        {
            issues.Add(new("account", "component.account-required")); // a deduction has to go somewhere
        }

        if (issues.Count > 0)
            throw new ValidationException(issues);

        component.Code = code;
        component.NameAr = nameAr;
        component.NameEn = nameEn;
        component.Kind = input.Kind;
        component.Calculation = input.Calculation;
        component.DefaultValue = input.DefaultValue;
        component.AccountId = input.AccountId == Guid.Empty ? null : input.AccountId;
        component.IsInsurable = input.IsInsurable;
        component.InEndOfService = input.InEndOfService;
    }

    // ---------------------------------------------------------------- Leave

    public async Task<IReadOnlyList<LeaveDto>> ListLeaveAsync(CancellationToken cancellationToken = default) =>
        (await store.ListLeaveAsync(cancellationToken)).OrderByDescending(l => l.From).Select(ToDto).ToList();

    public async Task<LeaveDto> AddLeaveAsync(LeaveInput input, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var leave = new LeaveRecord { CompanyId = company.Id };
        await ApplyAsync(leave, input, cancellationToken);
        await store.AddLeaveAsync(leave, cancellationToken);
        return ToDto(leave);
    }

    public async Task<LeaveDto> UpdateLeaveAsync(Guid id, LeaveInput input, CancellationToken cancellationToken = default)
    {
        var leave = await store.FindLeaveAsync(id, cancellationToken) ?? throw new NotFoundException("leave");
        await ApplyAsync(leave, input, cancellationToken);
        await store.UpdateLeaveAsync(leave, cancellationToken);
        return ToDto(leave);
    }

    public async Task DeleteLeaveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await store.FindLeaveAsync(id, cancellationToken) ?? throw new NotFoundException("leave");
        await store.DeleteLeaveAsync(id, cancellationToken);
    }

    private async Task ApplyAsync(LeaveRecord leave, LeaveInput input, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        if (await store.FindEmployeeAsync(input.EmployeeId, cancellationToken) is null)
            issues.Add(new("employee", "leave.employee-unknown"));
        if (input.To < input.From || input.From == default)
            issues.Add(new("to", "leave.dates-invalid"));
        var days = input.Days ?? (input.To.DayNumber - input.From.DayNumber + 1);
        if (days <= 0)
            issues.Add(new("days", "leave.days-invalid"));
        if (issues.Count > 0)
            throw new ValidationException(issues);

        leave.EmployeeId = input.EmployeeId;
        leave.Kind = input.Kind;
        leave.From = input.From;
        leave.To = input.To;
        leave.Days = days;
        leave.Notes = Clean(input.Notes);
    }

    /// <summary>
    /// Annual leave on a date: the balance brought forward plus a twelfth of the yearly entitlement for each whole month since, less the
    /// annual leave taken after the balance date. (Sick, unpaid and other leave do not use the annual balance.)
    /// </summary>
    public async Task<IReadOnlyList<LeaveBalanceDto>> BalancesAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var leave = await store.ListLeaveAsync(cancellationToken);
        var result = new List<LeaveBalanceDto>();
        foreach (var employee in (await store.ListEmployeesAsync(cancellationToken)).Where(e => e.IsActive && e.JoinDate <= asOf).OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase))
        {
            var start = employee.LeaveBalanceDate ?? employee.JoinDate;
            var months = Math.Max(0, (asOf.Year * 12 + asOf.Month) - (start.Year * 12 + start.Month) - (asOf.Day < start.Day ? 1 : 0));
            var earned = employee.LeaveBalanceDays + Math.Round(months * employee.AnnualLeaveDays / 12m, 2, MidpointRounding.AwayFromZero);
            var taken = leave.Where(l => l.EmployeeId == employee.Id && l.Kind == LeaveKind.Annual && l.From >= start && l.From <= asOf).Sum(l => l.Days);
            result.Add(new LeaveBalanceDto(employee.Id, earned, taken, earned - taken));
        }

        return result;
    }

    // ---------------------------------------------------------------- Helpers

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static EmployeeDto ToDto(Employee e, bool inUse) => new(
        e.Id, e.Code, e.NameAr, e.NameEn, e.JobTitle, e.NationalId, e.IsNational, e.JoinDate, e.LeaveDate, e.BasicSalary, e.BankName, e.BankAccount,
        e.CostCenterId, e.AnnualLeaveDays, e.LeaveBalanceDays, e.LeaveBalanceDate, e.IsActive, e.Notes,
        [.. e.Components.Select(c => new EmployeeComponentInput(c.ComponentId, c.Value))], inUse);

    private static SalaryComponentDto ToDto(SalaryComponent c, bool inUse) => new(
        c.Id, c.Code, c.NameAr, c.NameEn, c.Kind, c.Calculation, c.DefaultValue, c.AccountId, c.IsInsurable, c.InEndOfService, c.IsActive, inUse);

    private static LeaveDto ToDto(LeaveRecord l) => new(l.Id, l.EmployeeId, l.Kind, l.From, l.To, l.Days, l.Notes);

    private static ValidationException Refused(string field, string code) => new([new ValidationIssue(field, code)]);
}
