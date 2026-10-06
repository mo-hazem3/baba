namespace Baba.Application.Reporting;

public enum ColumnKind
{
    Text,
    Date,
    Amount,
}

/// <summary>How a row looks: group rows and totals are bold, headings stand alone.</summary>
public enum RowStyle
{
    Normal,
    Group,
    Subtotal,
    Total,
    Heading,
}

public sealed record ReportColumn(string Key, ColumnKind Kind, string TitleEn, string TitleAr);

/// <summary>
/// One cell. Text may have an Arabic variant (<see cref="TextAr"/>), used when the report is shown or printed in Arabic.
/// A blank amount (null) is shown as an empty cell, which is how accountants like zeros in a trial balance.
/// </summary>
public sealed record ReportCell(string? Text = null, string? TextAr = null, decimal? Amount = null, DateOnly? Date = null)
{
    public static ReportCell Blank { get; } = new();
}

/// <summary>Where clicking a row goes: an account's statement (for a date range) or a voucher. Drill-down everywhere (brief section 11).</summary>
public sealed record ReportLink(string Kind, Guid Id, DateOnly? From = null, DateOnly? To = null)
{
    public const string Account = "account";
    public const string Voucher = "voucher";
}

public sealed record ReportRow(IReadOnlyList<ReportCell> Cells, int Level = 0, RowStyle Style = RowStyle.Normal, ReportLink? Link = null);

/// <summary>A sanity check the report ran on itself, such as "total debits equal total credits".</summary>
public sealed record ReportCheck(string TextEn, string TextAr, bool Passed);

/// <summary>A finished report in both languages. The screen, the PDF, the Excel file and the CSV are all made from this.</summary>
public sealed record ReportResult(
    string Key,
    string TitleEn,
    string TitleAr,
    string SubtitleEn,
    string SubtitleAr,
    string CompanyNameEn,
    string CompanyNameAr,
    string CurrencyCode,
    int MinorUnits,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<ReportRow> Rows,
    IReadOnlyList<ReportCheck> Checks);

public enum Comparison
{
    None,

    /// <summary>The same dates one year earlier, shown in a second amount column.</summary>
    PreviousYear,
}
