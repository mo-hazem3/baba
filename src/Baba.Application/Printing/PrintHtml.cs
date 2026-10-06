using System.Net;
using Baba.Domain;

namespace Baba.Application.Printing;

/// <summary>The pieces every printout shares: bilingual labels, digit style, the page frame, the company header. All user text is encoded.</summary>
internal static class PrintHtml
{
    public static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    /// <summary>English, or Arabic, or both stacked (English above Arabic), as the layout says.</summary>
    public static string Label(PrintLayout layout, string en, string ar) => layout switch
    {
        PrintLayout.Arabic => E(ar),
        PrintLayout.English => E(en),
        _ => Both(en, ar),
    };

    public static string Both(string en, string ar) => $"{E(en)}<span class=\"ar\" lang=\"ar\" dir=\"rtl\">{E(ar)}</span>";

    /// <summary>A name that may exist in only one language: shows what exists, in the layout's language(s).</summary>
    public static string Name(PrintLayout layout, string? en, string? ar)
    {
        var hasEn = !string.IsNullOrWhiteSpace(en);
        var hasAr = !string.IsNullOrWhiteSpace(ar);
        return layout switch
        {
            PrintLayout.Arabic => E(hasAr ? ar : en),
            PrintLayout.English => E(hasEn ? en : ar),
            _ when hasEn && hasAr && !string.Equals(en, ar, StringComparison.Ordinal) => Both(en!, ar!),
            _ => E(hasEn ? en : ar),
        };
    }

    /// <summary>Numbers and dates use Arabic-Indic digits only in Arabic printouts and only when the company chose that.</summary>
    public static string Digits(string text, PrintLayout layout, bool arabicIndic) =>
        layout == PrintLayout.Arabic && arabicIndic ? NumberText.ToArabicIndic(text) : text;

    public static string Date(DateOnly date, PrintLayout layout, bool arabicIndic) =>
        Digits(date.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture), layout, arabicIndic);

    /// <summary>An amount isolated left to right, so a minus sign always sits in front of the digits, even inside Arabic text.</summary>
    public static string Amount(decimal value, int minorUnits, PrintLayout layout, bool arabicIndic) =>
        $"<bdi dir=\"ltr\">{Digits(NumberText.Amount(value, minorUnits, arabicIndic: false), layout, arabicIndic)}</bdi>";

    public static string Document(PrintLayout layout, string title, string fontCss, string fontFamily, bool landscape, string body)
    {
        var rtl = layout == PrintLayout.Arabic;
        return $$"""
            <!doctype html>
            <html lang="{{(rtl ? "ar" : "en")}}" dir="{{(rtl ? "rtl" : "ltr")}}">
            <head>
            <meta charset="utf-8">
            <title>{{E(title)}}</title>
            <style>
            {{fontCss}}
            @page { size: A4{{(landscape ? " landscape" : "")}}; margin: 14mm 14mm 18mm; @bottom-center { content: counter(page) " / " counter(pages); font: 9pt {{fontFamily}}; color: #444; } }
            * { box-sizing: border-box; }
            body { margin: 0; font-family: {{fontFamily}}; font-size: 10.5pt; line-height: 1.5; color: #111; }
            .ar { display: block; font-family: {{fontFamily}}; font-size: 1.05em; }
            h1 { margin: 0 0 2mm; font-size: 17pt; color: #12356b; }
            .head { display: flex; justify-content: space-between; align-items: flex-start; gap: 8mm; border-bottom: 2px solid #12356b; padding-bottom: 3mm; margin-bottom: 4mm; }
            .brand { display: flex; align-items: center; gap: 4mm; }
            .brand img { max-height: 18mm; max-width: 40mm; object-fit: contain; }
            .company { font-size: 13pt; font-weight: 700; }
            .muted { color: #444; font-size: 9.5pt; }
            .meta { display: grid; grid-template-columns: auto auto; column-gap: 5mm; row-gap: 0.5mm; margin: 0; }
            .meta dt { font-weight: 700; }
            .meta dd { margin: 0; unicode-bidi: plaintext; }
            table { width: 100%; border-collapse: collapse; margin-top: 3mm; }
            thead { display: table-header-group; }
            tr { break-inside: avoid; }
            th, td { border: 1px solid #555; padding: 1.4mm 2mm; vertical-align: top; }
            th { background: #eef2f8; text-align: start; }
            td.num, th.num { text-align: end; font-variant-numeric: tabular-nums; white-space: nowrap; }
            .neg { color: #b00020; }
            .r-group td { font-weight: 700; background: #f6f8fb; }
            .r-heading td { font-weight: 700; background: #e3e9f3; }
            .r-subtotal td { font-weight: 700; border-top: 2px solid #555; }
            .r-total td { font-weight: 700; border-top: 2px solid #111; border-bottom: 2px solid #111; background: #eef2f8; }
            .totals { margin-top: 3mm; margin-inline-start: auto; inline-size: 55%; }
            .totals td { border: none; border-bottom: 1px solid #aaa; }
            .totals tr.grand td { font-weight: 700; border-top: 2px solid #111; border-bottom: 2px solid #111; }
            .box { margin-top: 4mm; padding: 2.5mm 3mm; border: 1px solid #555; }
            .badge { display: inline-block; padding: 0.5mm 3mm; border: 2px solid #b00020; color: #b00020; font-weight: 700; letter-spacing: 1px; transform: rotate(-4deg); }
            .check-ok { color: #1b6e2d; }
            .check-bad { color: #b00020; font-weight: 700; }
            .sign { display: flex; justify-content: space-between; align-items: flex-end; gap: 8mm; margin-top: 16mm; break-inside: avoid; }
            .sign > div { flex: 1; border-top: 1px solid #111; padding-top: 1mm; text-align: center; }
            .sign img { display: block; margin: 0 auto 2mm; max-height: 24mm; max-width: 40mm; object-fit: contain; }
            .footer { margin-top: 8mm; padding-top: 2mm; border-top: 1px solid #999; font-size: 9pt; color: #444; text-align: center; }
            </style>
            </head>
            <body>
            {{body}}
            </body>
            </html>
            """;
    }

    /// <summary>The company block at the top of every printout: logo, name(s), header text, and a block of facts on the other side.</summary>
    public static string Header(
        PrintLayout layout, PrintSettings settings, string companyNameEn, string companyNameAr, string? logoDataUrl, string factsHtml)
    {
        var logo = settings.ShowLogo && logoDataUrl is not null ? $"<img src=\"{logoDataUrl}\" alt=\"\">" : "";
        var name = settings.ShowCompanyName ? $"<div class=\"company\">{Name(layout, companyNameEn, companyNameAr)}</div>" : "";
        var header = HeaderOrFooterText(layout, settings.HeaderTextEn, settings.HeaderTextAr, "muted");
        return $"<div class=\"head\"><div class=\"brand\">{logo}<div>{name}{header}</div></div>{factsHtml}</div>";
    }

    public static string Footer(PrintLayout layout, PrintSettings settings)
    {
        var text = HeaderOrFooterText(layout, settings.FooterTextEn, settings.FooterTextAr, "");
        return text.Length == 0 ? "" : $"<div class=\"footer\">{text}</div>";
    }

    private static string HeaderOrFooterText(PrintLayout layout, string? en, string? ar, string cssClass)
    {
        var text = Name(layout, en, ar);
        return string.IsNullOrWhiteSpace(en) && string.IsNullOrWhiteSpace(ar) ? "" : $"<div class=\"{cssClass}\">{text.Replace("\n", "<br>")}</div>";
    }
}
