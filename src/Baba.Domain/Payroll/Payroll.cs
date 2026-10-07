using Baba.Domain.Accounting;

namespace Baba.Domain.Payroll;

public enum SalaryComponentKind
{
    /// <summary>Added to the pay: housing, transport, overtime.</summary>
    Earning,

    /// <summary>Taken from the pay: a loan repayment, a penalty.</summary>
    Deduction,
}

public enum ComponentCalculation
{
    /// <summary>A fixed amount a month.</summary>
    Fixed,

    /// <summary>A percentage of the basic salary.</summary>
    PercentOfBasic,
}

public enum PayrollStatus
{
    Draft,
    Posted,
}

public enum LeaveKind
{
    Annual,
    Sick,
    Unpaid,
    Other,
}

/// <summary>An employee (brief section 10.4). The social insurance applies by <see cref="IsNational"/>, as each country's pack decides.</summary>
public sealed class Employee : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string? JobTitle { get; set; }

    /// <summary>The civil, national or residence number.</summary>
    public string? NationalId { get; set; }

    /// <summary>A national of the country: the insurance scheme for nationals applies (the pack says what that is).</summary>
    public bool IsNational { get; set; }

    public DateOnly JoinDate { get; set; }

    /// <summary>The last day of work, once the employee has left. Pay stops after it.</summary>
    public DateOnly? LeaveDate { get; set; }

    public long BasicSalaryScaled { get; set; }
    public string? BankName { get; set; }

    /// <summary>The IBAN or account number the salary is transferred to.</summary>
    public string? BankAccount { get; set; }

    public Guid? CostCenterId { get; set; }

    /// <summary>Days of annual leave earned in a year.</summary>
    public int AnnualLeaveDays { get; set; } = 30;

    /// <summary>The leave balance on <see cref="LeaveBalanceDate"/> (days brought forward); more is earned each month after it.</summary>
    public long LeaveBalanceDaysScaled { get; set; }

    public DateOnly? LeaveBalanceDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public List<EmployeeComponent> Components { get; set; } = [];

    public decimal BasicSalary
    {
        get => Scaled.ToDecimal(BasicSalaryScaled);
        set => BasicSalaryScaled = Scaled.ToScaled(value);
    }

    public decimal LeaveBalanceDays
    {
        get => Scaled.ToDecimal(LeaveBalanceDaysScaled);
        set => LeaveBalanceDaysScaled = Scaled.ToScaled(value);
    }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>A kind of allowance or deduction that employees can be given, such as housing or a loan repayment.</summary>
public sealed class SalaryComponent : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public SalaryComponentKind Kind { get; set; }
    public ComponentCalculation Calculation { get; set; }

    /// <summary>The amount, or the percentage of the basic salary (times 10,000), an employee gets unless it says otherwise.</summary>
    public long DefaultValueScaled { get; set; }

    /// <summary>An earning: the expense it is charged to (the salaries account when empty). A deduction: the account it is credited to (required).</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Counts toward the wage the social insurance is worked out on (housing usually does).</summary>
    public bool IsInsurable { get; set; }

    /// <summary>Counts toward the wage the end-of-service gratuity is worked out on.</summary>
    public bool InEndOfService { get; set; }

    public bool IsActive { get; set; } = true;

    public decimal DefaultValue
    {
        get => Scaled.ToDecimal(DefaultValueScaled);
        set => DefaultValueScaled = Scaled.ToScaled(value);
    }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>A component given to one employee, with the employee's own value.</summary>
public sealed class EmployeeComponent : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ComponentId { get; set; }
    public long ValueScaled { get; set; }

    public decimal Value
    {
        get => Scaled.ToDecimal(ValueScaled);
        set => ValueScaled = Scaled.ToScaled(value);
    }
}

/// <summary>
/// The company's payroll choices: where payroll posts, whether new months are made and posted by themselves, and the social insurance
/// rates (copied from the country's pack the first time, then the company's own to correct).
/// </summary>
public sealed class PayrollSettings : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }

    /// <summary>The first day of the first month payroll was run. From then on later months that have ended are made by themselves.</summary>
    public DateOnly? StartMonth { get; set; }

    /// <summary>Whether a month that is made by itself is also posted (it can always be corrected afterwards).</summary>
    public bool AutoPost { get; set; } = true;

    public long NationalEmployeePercentScaled { get; set; }
    public long NationalEmployerPercentScaled { get; set; }
    public long ForeignEmployeePercentScaled { get; set; }
    public long ForeignEmployerPercentScaled { get; set; }
    public long? InsuranceFloorScaled { get; set; }
    public long? InsuranceCeilingScaled { get; set; }

    public Guid? SalaryExpenseAccountId { get; set; }
    public Guid? SalariesPayableAccountId { get; set; }
    public Guid? InsuranceExpenseAccountId { get; set; }
    public Guid? InsurancePayableAccountId { get; set; }
    public Guid? EndOfServiceExpenseAccountId { get; set; }
    public Guid? EndOfServiceProvisionAccountId { get; set; }
}

/// <summary>One month's payroll: a payslip for each employee, posted to the books as one voucher.</summary>
public sealed class PayrollRun : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }

    /// <summary>The first day of the month.</summary>
    public DateOnly Month { get; set; }

    public PayrollStatus Status { get; set; }
    public string? Memo { get; set; }
    public Guid? VoucherId { get; set; }

    /// <summary>The voucher that paid the salaries out of the bank, once they were paid.</summary>
    public Guid? PaymentVoucherId { get; set; }

    public DateOnly? PaidDate { get; set; }
    public List<Payslip> Payslips { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class Payslip : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid RunId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>What the employee pays into social insurance, taken from the pay.</summary>
    public long EmployeeInsuranceScaled { get; set; }

    /// <summary>What the employer pays on top, an expense of the company.</summary>
    public long EmployerInsuranceScaled { get; set; }

    public List<PayslipItem> Items { get; set; } = [];

    public decimal EmployeeInsurance
    {
        get => Scaled.ToDecimal(EmployeeInsuranceScaled);
        set => EmployeeInsuranceScaled = Scaled.ToScaled(value);
    }

    public decimal EmployerInsurance
    {
        get => Scaled.ToDecimal(EmployerInsuranceScaled);
        set => EmployerInsuranceScaled = Scaled.ToScaled(value);
    }

    public decimal Earnings => Items.Where(i => i.Kind == SalaryComponentKind.Earning).Sum(i => i.Amount);
    public decimal Deductions => Items.Where(i => i.Kind == SalaryComponentKind.Deduction).Sum(i => i.Amount);

    /// <summary>What the employee is paid.</summary>
    public decimal Net => Earnings - Deductions - EmployeeInsurance;
}

/// <summary>A line of a payslip: the basic salary, an allowance, a deduction.</summary>
public sealed class PayslipItem : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid PayslipId { get; set; }
    public int LineNumber { get; set; }
    public SalaryComponentKind Kind { get; set; }
    public string Label { get; set; } = "";
    public string LabelAr { get; set; } = "";
    public Guid? ComponentId { get; set; }
    public long AmountScaled { get; set; }

    /// <summary>An earning: the expense account. A deduction: the account credited.</summary>
    public Guid? AccountId { get; set; }

    public bool IsInsurable { get; set; }
    public bool InEndOfService { get; set; }

    /// <summary>The basic salary line (always the first of the payslip).</summary>
    public bool IsBasic { get; set; }

    public decimal Amount
    {
        get => Scaled.ToDecimal(AmountScaled);
        set => AmountScaled = Scaled.ToScaled(value);
    }
}

/// <summary>Leave an employee took.</summary>
public sealed class LeaveRecord : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public LeaveKind Kind { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public long DaysScaled { get; set; }
    public string? Notes { get; set; }

    public decimal Days
    {
        get => Scaled.ToDecimal(DaysScaled);
        set => DaysScaled = Scaled.ToScaled(value);
    }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>The end-of-service provision entry of a month (the amount can be negative when the provision went down).</summary>
public sealed class EndOfServiceAccrual : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }

    /// <summary>The first day of the month.</summary>
    public DateOnly Month { get; set; }

    public long AmountScaled { get; set; }
    public Guid VoucherId { get; set; }

    public decimal Amount
    {
        get => Scaled.ToDecimal(AmountScaled);
        set => AmountScaled = Scaled.ToScaled(value);
    }
}
