using System.Text;
using Baba.Application.Reporting;
using Baba.Domain;

namespace Baba.Application.Printing;

/// <summary>Turns any <see cref="ReportResult"/> into a printable page in Arabic, English or both (brief section 11: export to PDF).</summary>
public static class ReportHtmlBuilder
{
    /// <summary>Wide tables (more than six columns, such as the trial balance) print sideways.</summary>
    public static bool NeedsLandscape(ReportResult report) => report.Columns.Count > 6;

    public static string Build(
        ReportResult report, PrintLayout layout, PrintSettings settings, string fontCss, string fontFamily, string? logoDataUrl, DateTime printedAt)
    {
        var ai = settings.ArabicIndicDigits;
        string L(string en, string ar) => PrintHtml.Label(layout, en, ar);

        var facts = new StringBuilder("<dl class=\"meta\">")
            .Append($"<dt>{L("Currency", "العملة")}</dt><dd dir=\"ltr\">{PrintHtml.E(report.CurrencyCode)}</dd>")
            .Append($"<dt>{L("Printed", "تاريخ الطباعة")}</dt><dd dir=\"ltr\">{PrintHtml.Date(DateOnly.FromDateTime(printedAt), layout, ai)}</dd>")
            .Append("</dl>")
            .ToString();

        var html = new StringBuilder();
        html.Append(PrintHtml.Header(layout, settings, report.CompanyNameEn, report.CompanyNameAr, logoDataUrl, facts));
        html.Append($"<h1>{L(report.TitleEn, report.TitleAr)}</h1>");
        html.Append($"<div class=\"muted\">{L(report.SubtitleEn, report.SubtitleAr)}</div>");

        html.Append("<table><thead><tr>");
        foreach (var column in report.Columns)
            html.Append($"<th{(column.Kind == ColumnKind.Amount ? " class=\"num\"" : "")}>{L(column.TitleEn, column.TitleAr)}</th>");
        html.Append("</tr></thead><tbody>");

        var nameColumn = report.Columns.ToList().FindIndex(c => c.Key == "name");
        foreach (var row in report.Rows)
        {
            html.Append($"<tr class=\"r-{row.Style.ToString().ToLowerInvariant()}\">");
            for (var i = 0; i < report.Columns.Count; i++)
            {
                var column = report.Columns[i];
                var cell = i < row.Cells.Count ? row.Cells[i] : ReportCell.Blank;
                var indent = i == nameColumn && row.Level > 0 ? $" style=\"padding-inline-start: {row.Level * 4 + 2}mm\"" : "";

                switch (column.Kind)
                {
                    case ColumnKind.Amount:
                        var negative = cell.Amount < 0 ? " neg" : "";
                        html.Append($"<td class=\"num{negative}\">{(cell.Amount is { } amount ? PrintHtml.Amount(amount, report.MinorUnits, layout, ai) : "")}</td>");
                        break;
                    case ColumnKind.Date:
                        html.Append($"<td>{(cell.Date is { } date ? PrintHtml.Date(date, layout, ai) : PrintHtml.Name(layout, cell.Text, cell.TextAr))}</td>");
                        break;
                    default:
                        html.Append($"<td{indent}>{PrintHtml.Digits(PrintHtml.Name(layout, cell.Text, cell.TextAr), layout, false)}</td>");
                        break;
                }
            }

            html.Append("</tr>");
        }

        html.Append("</tbody></table>");

        foreach (var check in report.Checks)
        {
            html.Append(check.Passed
                ? $"<div class=\"box check-ok\">✓ {L(check.TextEn, check.TextAr)}</div>"
                : $"<div class=\"box check-bad\">✗ {L(check.TextEn, check.TextAr)} — {L("please review", "يرجى المراجعة")}</div>");
        }

        html.Append(PrintHtml.Footer(layout, settings));
        return PrintHtml.Document(layout, report.TitleEn, fontCss, fontFamily, NeedsLandscape(report), html.ToString());
    }
}
