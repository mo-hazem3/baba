using Baba.Application.Printing;
using Baba.Application.Reporting;
using Baba.Domain;
using ClosedXML.Excel;

namespace Baba.Infrastructure.Printing;

/// <summary>
/// Writes a report as an Excel workbook: real numbers and dates (not text), the company and report title above the table,
/// bold group and total rows, a frozen header, and a right-to-left sheet for Arabic.
/// </summary>
public sealed class ClosedXmlReportWriter : IReportXlsxWriter
{
    public byte[] Write(ReportResult report, PrintLayout layout)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(report, layout));
        sheet.RightToLeft = layout == PrintLayout.Arabic;

        string Text(string? en, string? ar) => ReportCsv.Plain(layout, en, ar);

        sheet.Cell(1, 1).Value = Text(report.CompanyNameEn, report.CompanyNameAr);
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Cell(2, 1).Value = Text(report.TitleEn, report.TitleAr);
        sheet.Cell(2, 1).Style.Font.SetBold().Font.SetFontSize(12);
        sheet.Cell(3, 1).Value = Text(report.SubtitleEn, report.SubtitleAr);
        sheet.Cell(4, 1).Value = Text("Currency: ", "العملة: ") + report.CurrencyCode;

        const int headerRow = 6;
        for (var c = 0; c < report.Columns.Count; c++)
        {
            var header = sheet.Cell(headerRow, c + 1);
            header.Value = Text(report.Columns[c].TitleEn, report.Columns[c].TitleAr);
            header.Style.Font.SetBold();
            header.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#EEF2F8"));
            header.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            header.Style.Alignment.SetHorizontal(report.Columns[c].Kind == ColumnKind.Amount ? XLAlignmentHorizontalValues.Right : XLAlignmentHorizontalValues.Left);
        }

        var numberFormat = report.MinorUnits == 0
            ? "#,##0;[Red]-#,##0"
            : $"#,##0.{new string('0', report.MinorUnits)};[Red]-#,##0.{new string('0', report.MinorUnits)}";
        var nameColumn = report.Columns.ToList().FindIndex(c => c.Key == "name");

        var rowNumber = headerRow;
        foreach (var row in report.Rows)
        {
            rowNumber++;
            for (var c = 0; c < report.Columns.Count; c++)
            {
                var cell = sheet.Cell(rowNumber, c + 1);
                var source = c < row.Cells.Count ? row.Cells[c] : ReportCell.Blank;
                switch (report.Columns[c].Kind)
                {
                    case ColumnKind.Amount when source.Amount is { } amount:
                        cell.Value = amount;
                        cell.Style.NumberFormat.Format = numberFormat;
                        break;
                    case ColumnKind.Date when source.Date is { } date:
                        cell.Value = date.ToDateTime(TimeOnly.MinValue);
                        cell.Style.DateFormat.Format = "dd/mm/yyyy";
                        cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
                        break;
                    default:
                        var text = Text(source.Text, source.TextAr);
                        if (text.Length > 0)
                            cell.Value = text;
                        break;
                }

                if (c == nameColumn && row.Level > 0)
                    cell.Style.Alignment.Indent = Math.Min(row.Level, 15);
            }

            var line = sheet.Range(rowNumber, 1, rowNumber, report.Columns.Count);
            switch (row.Style)
            {
                case RowStyle.Group or RowStyle.Subtotal:
                    line.Style.Font.SetBold();
                    break;
                case RowStyle.Heading:
                    line.Style.Font.SetBold();
                    line.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E3E9F3"));
                    break;
                case RowStyle.Total:
                    line.Style.Font.SetBold();
                    line.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#EEF2F8"));
                    line.Style.Border.SetTopBorder(XLBorderStyleValues.Thin).Border.SetBottomBorder(XLBorderStyleValues.Double);
                    break;
            }
        }

        foreach (var check in report.Checks)
        {
            rowNumber++;
            sheet.Cell(rowNumber + 1, 1).Value = (check.Passed ? "✓ " : "✗ ") + Text(check.TextEn, check.TextAr);
        }

        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns().AdjustToContents(headerRow, rowNumber);
        foreach (var column in sheet.ColumnsUsed())
            column.Width = Math.Clamp(column.Width, 10, 60);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Excel sheet names are at most 31 characters and cannot contain : \ / ? * [ ].</summary>
    private static string SheetName(ReportResult report, PrintLayout layout)
    {
        var name = new string(ReportCsv.Plain(layout, report.TitleEn, report.TitleAr).Where(c => !":\\/?*[]".Contains(c)).ToArray()).Trim();
        return name.Length == 0 ? "Report" : name[..Math.Min(name.Length, 31)];
    }
}
