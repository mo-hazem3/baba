using System.Text;
using Baba.Application.Accounting;
using Baba.Application.Trade;
using Baba.Domain;
using Baba.Domain.Trade;
using Baba.Localization;

namespace Baba.Application.Printing;

/// <summary>Everything a document printout shows, gathered by the caller.</summary>
public sealed record TradePrintData(
    DocumentDto Document,
    PartyDto Party,
    string CompanyNameEn,
    string CompanyNameAr,
    Currency Currency,
    string BaseCurrencyCode,
    CurrencyWords Words,
    IReadOnlyList<(string En, string Ar, string Value)> CompanyTaxNumbers,
    /// <summary>The e-invoice QR code as an image address, when the country has one and the invoice is issued.</summary>
    string? QrDataUrl = null);

/// <summary>
/// The printed quote, order, delivery note, invoice, credit or debit note (brief section 10.3): logo, company name, number and date,
/// who it is for, the lines, the totals, the amount in words, signature boxes and the stamp, in Arabic, English or both.
/// </summary>
public static class TradePrintBuilder
{
    public static string Build(
        TradePrintData data, PrintLayout layout, PrintSettings settings, string fontCss, string fontFamily,
        string? logoDataUrl, string? stampDataUrl)
    {
        var document = data.Document;
        var party = data.Party;
        var ai = settings.ArabicIndicDigits;
        var minor = data.Currency.MinorUnits;
        string L(string en, string ar) => PrintHtml.Label(layout, en, ar);

        var (titleEn0, titleAr0) = document.Kind switch
        {
            DocumentKind.Quote => ("Quotation", "عرض سعر"),
            DocumentKind.SalesOrder => ("Sales order", "أمر بيع"),
            DocumentKind.DeliveryNote => ("Delivery note", "إذن تسليم"),
            DocumentKind.SalesInvoice => ("Sales invoice", "فاتورة مبيعات"),
            DocumentKind.SalesCreditNote => ("Credit note", "إشعار دائن"),
            DocumentKind.PurchaseOrder => ("Purchase order", "أمر شراء"),
            DocumentKind.GoodsReceipt => ("Goods receipt", "إذن استلام"),
            DocumentKind.PurchaseInvoice => ("Purchase invoice", "فاتورة مشتريات"),
            _ => ("Debit note", "إشعار مدين"),
        };
        var sales = document.Kind.IsSales();
        var taxed = document.Lines.Any(l => l.TaxCodeId is not null);
        // A taxed sale or credit note is a tax invoice (bilingual where the law asks for it, as chosen in the print template).
        var (titleEn, titleAr) = taxed && document.Kind == DocumentKind.SalesInvoice ? ("Tax invoice", "فاتورة ضريبية")
            : taxed && document.Kind == DocumentKind.SalesCreditNote ? ("Tax credit note", "إشعار دائن ضريبي")
            : taxed && document.Kind == DocumentKind.PurchaseDebitNote ? ("Tax debit note", "إشعار مدين ضريبي")
            : (titleEn0, titleAr0);
        var numberText = document.Number is null ? L("Draft", "مسودة") : PrintHtml.E(PrintHtml.Digits(document.Number, layout, ai));

        var facts = new StringBuilder("<dl class=\"meta\">")
            .Append($"<dt>{L("No.", "الرقم")}</dt><dd dir=\"ltr\">{numberText}</dd>")
            .Append($"<dt>{L("Date", "التاريخ")}</dt><dd dir=\"ltr\">{PrintHtml.Date(document.Date, layout, ai)}</dd>");
        if (document.Kind.Posts() && document.DueDate is { } due)
            facts.Append($"<dt>{L("Due date", "تاريخ الاستحقاق")}</dt><dd dir=\"ltr\">{PrintHtml.Date(due, layout, ai)}</dd>");
        if (document.Reference is not null)
            facts.Append($"<dt>{L("Reference", "المرجع")}</dt><dd>{PrintHtml.E(document.Reference)}</dd>");
        facts.Append($"<dt>{L("Currency", "العملة")}</dt><dd dir=\"ltr\">{PrintHtml.E(document.CurrencyCode)}</dd>");
        if (document.CurrencyCode != data.BaseCurrencyCode)
            facts.Append($"<dt>{L("Rate", "سعر الصرف")}</dt><dd dir=\"ltr\">1 {PrintHtml.E(document.CurrencyCode)} = {PrintHtml.Digits(document.ExchangeRate.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), layout, ai)} {PrintHtml.E(data.BaseCurrencyCode)}</dd>");
        foreach (var (en, ar, value) in data.CompanyTaxNumbers)
            facts.Append($"<dt>{L(en, ar)}</dt><dd dir=\"ltr\">{PrintHtml.E(PrintHtml.Digits(value, layout, ai))}</dd>");
        facts.Append("</dl>");

        var html = new StringBuilder();
        html.Append(PrintHtml.Header(layout, settings, data.CompanyNameEn, data.CompanyNameAr, logoDataUrl, facts.ToString()));
        html.Append($"<h1>{L(titleEn, titleAr)}{(document.Status == DocumentStatus.Draft ? $" <span class=\"badge\">{L("DRAFT", "مسودة")}</span>" : "")}</h1>");

        html.Append("<div class=\"box\">")
            .Append($"<div><strong>{(sales ? L("Customer", "العميل") : L("Supplier", "المورّد"))}:</strong> {PrintHtml.Name(layout, party.NameEn, party.NameAr)}</div>");
        if (!string.IsNullOrWhiteSpace(party.TaxNumber))
            html.Append($"<div><strong>{L("Tax number", "الرقم الضريبي")}:</strong> <bdi dir=\"ltr\">{PrintHtml.E(party.TaxNumber)}</bdi></div>");
        if (!string.IsNullOrWhiteSpace(party.Address))
            html.Append($"<div>{PrintHtml.E(party.Address)}</div>");
        if (!string.IsNullOrWhiteSpace(party.Phone))
            html.Append($"<div><bdi dir=\"ltr\">{PrintHtml.E(party.Phone)}</bdi></div>");
        html.Append("</div>");

        var cols = taxed ? 8 : 6; // columns in the table, so the totals can span all but the last
        var lastAmount = taxed ? 2 : 1;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        html.Append("<table><thead><tr>")
            .Append($"<th>#</th><th>{L("Description", "البيان")}</th><th class=\"num\">{L("Qty", "الكمية")}</th><th class=\"num\">{L("Price", "السعر")}</th>")
            .Append($"<th class=\"num\">{L("Disc. %", "الخصم %")}</th><th class=\"num\">{L("Amount", "المبلغ")}</th>");
        if (taxed)
            html.Append($"<th class=\"num\">{L("Tax %", "الضريبة %")}</th><th class=\"num\">{L("Tax", "الضريبة")}</th>");
        html.Append("</tr></thead><tbody>");

        var number = 0;
        foreach (var line in document.Lines)
        {
            number++;
            html.Append($"<tr><td>{PrintHtml.Digits(number.ToString(), layout, ai)}</td><td>{PrintHtml.E(line.Description)}</td>")
                .Append($"<td class=\"num\"><bdi dir=\"ltr\">{PrintHtml.Digits(line.Quantity.ToString("0.####", inv), layout, ai)}</bdi></td>")
                .Append($"<td class=\"num\">{PrintHtml.Amount(line.UnitPrice, minor, layout, ai)}</td>")
                .Append($"<td class=\"num\">{(line.DiscountPercent > 0 ? PrintHtml.Digits(line.DiscountPercent.ToString("0.##", inv), layout, ai) : "")}</td>")
                .Append($"<td class=\"num\">{PrintHtml.Amount(line.Amount, minor, layout, ai)}</td>");
            if (taxed)
                html.Append($"<td class=\"num\">{(line.TaxCodeId is null ? "" : PrintHtml.Digits(line.TaxRate.ToString("0.##", inv), layout, ai) + "%")}</td>")
                    .Append($"<td class=\"num\">{(line.TaxCodeId is null ? "" : PrintHtml.Amount(line.TaxAmount, minor, layout, ai))}</td>");
            html.Append("</tr>");
        }

        string Total(string en, string ar, decimal value, bool strong) =>
            $"<tr{(strong ? " class=\"r-total\"" : "")}><td colspan=\"{cols - lastAmount}\">{L(en, ar)}</td><td class=\"num\"{(taxed ? " colspan=\"2\"" : "")}>{PrintHtml.Amount(value, minor, layout, ai)}</td></tr>";

        html.Append(Total("Subtotal", "الإجمالي قبل الخصم", document.Subtotal, true));
        if (document.DiscountAmount != 0)
            html.Append(Total($"Discount ({document.DiscountPercent.ToString("0.##", inv)}%)", $"الخصم ({PrintHtml.Digits(document.DiscountPercent.ToString("0.##", inv), layout, ai)}%)", -document.DiscountAmount, false));
        if (taxed)
        {
            html.Append(Total("Total before tax", "الإجمالي قبل الضريبة", document.Net, false));
            html.Append(Total("Tax", "الضريبة", document.TaxTotal, false));
        }

        html.Append(Total($"Total ({document.CurrencyCode})", $"الإجمالي ({document.CurrencyCode})", document.Total, true));
        html.Append("</tbody></table>");

        if (settings.ShowAmountInWords && document.Total > 0)
        {
            html.Append("<div class=\"box\">");
            if (layout != PrintLayout.Arabic)
                html.Append($"<div><strong>Amount in words:</strong> {PrintHtml.E(AmountInWords.English(document.Total, data.Words))}</div>");
            if (layout != PrintLayout.English)
                html.Append($"<div dir=\"rtl\" lang=\"ar\"><strong>المبلغ كتابةً:</strong> {PrintHtml.E(AmountInWords.Arabic(document.Total, data.Words))}</div>");
            html.Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(document.Memo))
            html.Append($"<div class=\"box\"><strong>{L("Notes", "ملاحظات")}:</strong> {PrintHtml.E(document.Memo)}</div>");

        if (data.QrDataUrl is not null)
            html.Append($"<div class=\"qr\"><img src=\"{data.QrDataUrl}\" alt=\"QR\" width=\"120\" height=\"120\"></div>");

        if (settings.ShowSignatures || (settings.ShowStamp && stampDataUrl is not null))
        {
            var third = document.Kind switch
            {
                DocumentKind.Quote => ("Customer acceptance", "موافقة العميل"),
                DocumentKind.DeliveryNote => ("Received by", "توقيع المستلم"),
                DocumentKind.GoodsReceipt => ("Received by", "توقيع المستلم"),
                _ => ("Approved by", "اعتماد"),
            };

            html.Append("<div class=\"sign\">");
            if (settings.ShowSignatures)
                html.Append($"<div>{L("Prepared by", "إعداد")}</div><div>{L("Reviewed by", "مراجعة")}</div><div>{L(third.Item1, third.Item2)}</div>");
            if (settings.ShowStamp && stampDataUrl is not null)
                html.Append($"<div style=\"border-top: none\"><img src=\"{stampDataUrl}\" alt=\"\">{L("Stamp", "الختم")}</div>");
            html.Append("</div>");
        }

        html.Append(PrintHtml.Footer(layout, settings));
        return PrintHtml.Document(layout, titleEn, fontCss, fontFamily, landscape: false, html.ToString());
    }
}

/// <summary>Makes the PDF of a sales or purchase document. Needs the host's PDF renderer.</summary>
public sealed class TradePrintService(
    IPdfRenderer? renderer,
    IPrintFonts fonts,
    Companies.ICompanyFiles files,
    IBrandingStore branding,
    DocumentService documents,
    PartyService parties,
    CountryPackRegistry countryPacks,
    IQrImageMaker qrImages)
{
    public bool IsAvailable => renderer is not null;

    public async Task<byte[]> RenderAsync(Guid documentId, PrintLayout? layout, CancellationToken cancellationToken = default)
    {
        var pdf = renderer ?? throw new InvalidOperationException("This host cannot create PDFs.");
        var company = files.Current ?? throw new Companies.CompanyFileException(Companies.CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var document = await documents.GetAsync(documentId, cancellationToken) ?? throw new NotFoundException("document");
        var party = await parties.GetAsync(document.PartyId, cancellationToken);
        var settings = await branding.GetSettingsAsync(cancellationToken);

        var currency = CurrencyCatalog.Find(document.CurrencyCode)?.Currency ?? new Currency(document.CurrencyCode, 2);
        var words = CurrencyWordsCatalog.Find(document.CurrencyCode) ?? CurrencyWords.Generic(document.CurrencyCode, currency.MinorUnits);

        // The company's tax numbers, each under the name its country gives it (VAT number, commercial registration ...).
        var rules = countryPacks.Find(company.CountryCode)?.TaxRegistration ?? [];
        var numbers = (company.TaxNumbers ?? new Dictionary<string, string>())
            .Where(n => !string.IsNullOrWhiteSpace(n.Value))
            .Select(n => (Rule: rules.FirstOrDefault(r => r.Key == n.Key), n.Key, n.Value))
            .Select(n => (n.Rule?.NameEn ?? n.Key, n.Rule?.NameAr ?? n.Key, n.Value))
            .ToList();

        // The QR code of an e-invoice: on issued sales invoices and credit notes in the company's own currency, when the country has one.
        string? qr = null;
        var pack = countryPacks.Find(company.CountryCode);
        if (pack?.EInvoicing is { } provider
            && document is { Status: DocumentStatus.Issued, IssuedAt: { } issuedAt }
            && document.Kind is DocumentKind.SalesInvoice or DocumentKind.SalesCreditNote
            && document.CurrencyCode == company.BaseCurrencyCode
            && company.TaxNumbers?.GetValueOrDefault(provider.SellerTaxNumberKey) is { Length: > 0 } sellerNumber)
        {
            var text = provider.BuildQrCode(new EInvoiceFacts(
                company.NameEn, company.NameAr, sellerNumber, new DateTimeOffset(DateTime.SpecifyKind(issuedAt, DateTimeKind.Utc)), document.Total, document.TaxTotal));
            qr = text is null ? null : qrImages.SvgDataUrl(text);
        }

        var html = TradePrintBuilder.Build(
            new TradePrintData(document, party, company.NameEn, company.NameAr, currency, company.BaseCurrencyCode, words, numbers, qr),
            layout ?? settings.DefaultLayout, settings, fonts.CssFontFaces(), fonts.FontFamily,
            await ImageAsync(BrandingImage.Logo, cancellationToken), await ImageAsync(BrandingImage.Stamp, cancellationToken));

        return await pdf.RenderAsync(html, new PdfOptions(), cancellationToken);
    }

    private async Task<string?> ImageAsync(BrandingImage image, CancellationToken cancellationToken) =>
        await branding.GetImageAsync(image, cancellationToken) is { } file
            ? $"data:{file.ContentType};base64,{Convert.ToBase64String(file.Content)}"
            : null;
}

/// <summary>Draws a QR code. Implemented by Infrastructure.</summary>
public interface IQrImageMaker
{
    /// <summary>The QR code of a text as an image address (a data URL) that can sit in a printout.</summary>
    string SvgDataUrl(string text);
}
