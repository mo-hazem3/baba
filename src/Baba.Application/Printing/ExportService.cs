using System.Globalization;
using System.Text;
using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Application.Reporting;
using Baba.Domain;
using Baba.Localization;

namespace Baba.Application.Printing;

public enum ExportFormat
{
    Pdf,
    Xlsx,
    Csv,
}

public sealed record ExportFile(string FileName, string ContentType, byte[] Content);

/// <summary>Writes a report as an Excel file. Implemented by Infrastructure (ClosedXML).</summary>
public interface IReportXlsxWriter
{
    byte[] Write(ReportResult report, PrintLayout layout);
}

/// <summary>A report as comma-separated text: one header row, then the rows. Opens in Excel with Arabic intact when saved with a byte-order mark.</summary>
public static class ReportCsv
{
    public static string Build(ReportResult report, PrintLayout layout)
    {
        var csv = new StringBuilder();
        csv.Append(string.Join(',', report.Columns.Select(c => Escape(Plain(layout, c.TitleEn, c.TitleAr))))).Append("\r\n");

        foreach (var row in report.Rows)
        {
            var fields = report.Columns.Select((column, i) =>
            {
                var cell = i < row.Cells.Count ? row.Cells[i] : ReportCell.Blank;
                return column.Kind switch
                {
                    ColumnKind.Amount => cell.Amount?.ToString("F" + report.MinorUnits, CultureInfo.InvariantCulture) ?? "",
                    ColumnKind.Date => cell.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? Escape(Plain(layout, cell.Text, cell.TextAr)),
                    _ => Escape(Plain(layout, cell.Text, cell.TextAr)),
                };
            });
            csv.Append(string.Join(',', fields)).Append("\r\n");
        }

        return csv.ToString();
    }

    /// <summary>Plain text in the layout's language(s): "English / العربية" for both.</summary>
    public static string Plain(PrintLayout layout, string? en, string? ar)
    {
        var hasEn = !string.IsNullOrWhiteSpace(en);
        var hasAr = !string.IsNullOrWhiteSpace(ar);
        return layout switch
        {
            PrintLayout.Arabic => (hasAr ? ar : en) ?? "",
            PrintLayout.English => (hasEn ? en : ar) ?? "",
            _ when hasEn && hasAr && en != ar => $"{en} / {ar}",
            _ => (hasEn ? en : ar) ?? "",
        };
    }

    private static string Escape(string text) =>
        text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
}

/// <summary>
/// Makes the PDF of a voucher or a report, in Arabic, English or both, using the company's print template, logo and stamp
/// (brief sections 10.1 and 11). Needs the host's PDF renderer.
/// </summary>
public sealed class DocumentPrintService(
    IPdfRenderer? renderer,
    IPrintFonts fonts,
    ICompanyFiles files,
    IBrandingStore branding,
    VoucherService vouchers,
    ChartOfAccountsService chart,
    TimeProvider clock)
{
    public bool IsAvailable => renderer is not null;

    /// <summary>The voucher as a PDF. Without a layout, the company's default is used.</summary>
    public async Task<byte[]> RenderVoucherAsync(Guid voucherId, PrintLayout? layout, CancellationToken cancellationToken = default)
    {
        var renderer = Renderer();
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var voucher = await vouchers.GetAsync(voucherId, cancellationToken) ?? throw new NotFoundException("voucher");
        var settings = await branding.GetSettingsAsync(cancellationToken);
        var accounts = (await chart.ListAsync(cancellationToken)).ToDictionary(a => a.Id);

        var currency = CurrencyCatalog.Find(company.BaseCurrencyCode)?.Currency ?? new Currency(company.BaseCurrencyCode, 2);
        var words = CurrencyWordsCatalog.Find(company.BaseCurrencyCode) ?? CurrencyWords.Generic(company.BaseCurrencyCode, currency.MinorUnits);

        var html = VoucherPrintBuilder.Build(
            new VoucherPrintData(voucher, accounts, company.NameEn, company.NameAr, currency, words),
            layout ?? settings.DefaultLayout, settings, fonts.CssFontFaces(), fonts.FontFamily,
            await ImageAsync(BrandingImage.Logo, cancellationToken), await ImageAsync(BrandingImage.Stamp, cancellationToken),
            clock.GetLocalNow().DateTime);

        return await renderer.RenderAsync(html, new PdfOptions(), cancellationToken);
    }

    /// <summary>A report as a PDF (wide tables print sideways).</summary>
    public async Task<byte[]> RenderReportAsync(ReportResult report, PrintLayout? layout, CancellationToken cancellationToken = default)
    {
        var renderer = Renderer();
        var settings = await branding.GetSettingsAsync(cancellationToken);

        var html = ReportHtmlBuilder.Build(
            report, layout ?? settings.DefaultLayout, settings, fonts.CssFontFaces(), fonts.FontFamily,
            await ImageAsync(BrandingImage.Logo, cancellationToken), clock.GetLocalNow().DateTime);

        return await renderer.RenderAsync(html, new PdfOptions(Landscape: ReportHtmlBuilder.NeedsLandscape(report)), cancellationToken);
    }

    private IPdfRenderer Renderer() => renderer ?? throw new InvalidOperationException("This host cannot create PDFs.");

    private async Task<string?> ImageAsync(BrandingImage image, CancellationToken cancellationToken) =>
        await branding.GetImageAsync(image, cancellationToken) is { } file
            ? $"data:{file.ContentType};base64,{Convert.ToBase64String(file.Content)}"
            : null;
}

/// <summary>Exports a report as PDF, Excel or CSV (brief section 11: "export everything").</summary>
public sealed class ExportService(IReportXlsxWriter xlsx, DocumentPrintService printing, IBrandingStore branding)
{
    public async Task<ExportFile> ExportAsync(ReportResult report, ExportFormat format, PrintLayout? layout, CancellationToken cancellationToken = default)
    {
        var chosen = layout ?? (await branding.GetSettingsAsync(cancellationToken)).DefaultLayout;
        var name = FileName(report);

        return format switch
        {
            ExportFormat.Pdf => new ExportFile(name + ".pdf", "application/pdf", await printing.RenderReportAsync(report, chosen, cancellationToken)),
            ExportFormat.Xlsx => new ExportFile(name + ".xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Write(report, chosen)),
            ExportFormat.Csv => new ExportFile(name + ".csv", "text/csv; charset=utf-8",
                [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(ReportCsv.Build(report, chosen))]),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    /// <summary>"Trial balance - From 01-10-2026 to 31-10-2026", safe to use as a file name.</summary>
    public static string FileName(ReportResult report)
    {
        var text = $"{report.TitleEn} - {report.SubtitleEn}".Replace('/', '-');
        return new string(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ':' ? '-' : c).ToArray()).Trim();
    }
}
