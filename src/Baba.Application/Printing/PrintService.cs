using Baba.Application.Companies;
using Baba.Domain;
using Baba.Localization;

namespace Baba.Application.Printing;

/// <summary>Prints documents to PDF. Needs a PDF renderer, which only some hosts provide (the desktop app does).</summary>
public sealed class PrintService(IPdfRenderer? renderer, IPrintFonts fonts, ICompanyFiles files, TimeProvider clock)
{
    private static readonly Currency SampleCurrency = new("USD", 2);

    /// <summary>False when this host cannot make PDFs (for example the browser-only development setup).</summary>
    public bool IsAvailable => renderer is not null;

    /// <summary>A sample sales invoice for the open company (or a sample company), in Arabic, English or both.</summary>
    public Task<byte[]> RenderTestPageAsync(PrintLayout layout, CancellationToken cancellationToken = default)
    {
        if (renderer is null)
            throw new InvalidOperationException("This host cannot create PDFs.");

        var company = files.Current;
        var currency = company is null
            ? SampleCurrency
            : CurrencyCatalog.Find(company.BaseCurrencyCode)?.Currency ?? SampleCurrency;

        var html = TestPageBuilder.Build(
            layout,
            fonts.CssFontFaces(),
            fonts.FontFamily,
            companyNameAr: company?.NameAr ?? "شركة نموذجية",
            companyNameEn: company?.NameEn ?? "Sample Company",
            currency,
            DateOnly.FromDateTime(clock.GetLocalNow().DateTime));

        return renderer.RenderAsync(html, new PdfOptions(), cancellationToken);
    }
}
