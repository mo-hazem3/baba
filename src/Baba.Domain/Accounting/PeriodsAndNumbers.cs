namespace Baba.Domain.Accounting;

/// <summary>
/// A month that can be locked (for example after filing a VAT return). Only months that were ever locked have a row; any date
/// without a locked period is open.
/// </summary>
public sealed class Period : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public bool IsLocked { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public static (DateOnly Start, DateOnly End) MonthOf(DateOnly date) =>
        (new DateOnly(date.Year, date.Month, 1), new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)));
}

/// <summary>The last number given for one kind of voucher in one fiscal year, so numbers run 0001, 0002, 0003 within a year.</summary>
public sealed class NumberSequence : Entity, ICompanyScoped, INotAudited
{
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = "";
    public int FiscalYear { get; set; }
    public int LastNumber { get; set; }
}

/// <summary>Fiscal years are named by the calendar year in which they start (a year starting in April 2026 is "2026").</summary>
public static class FiscalYear
{
    public static int Of(DateOnly date, int startMonth) => date.Month >= startMonth ? date.Year : date.Year - 1;

    public static (DateOnly Start, DateOnly End) Range(int fiscalYear, int startMonth)
    {
        var start = new DateOnly(fiscalYear, startMonth, 1);
        return (start, start.AddYears(1).AddDays(-1));
    }
}

public static class VoucherNumber
{
    public static string Prefix(VoucherKind kind) => kind switch
    {
        VoucherKind.Payment => "PV",
        VoucherKind.Receipt => "RV",
        VoucherKind.Journal => "JV",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Format(VoucherKind kind, int fiscalYear, int sequence) => $"{Prefix(kind)}-{fiscalYear}-{sequence:0000}";
}
