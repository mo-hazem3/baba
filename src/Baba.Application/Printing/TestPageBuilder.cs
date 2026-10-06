using System.Net;
using System.Text;
using Baba.Domain;

namespace Baba.Application.Printing;

/// <summary>
/// Builds the printable test page: a sample sales invoice in Arabic, English, or both side by side.
/// It exists to prove Arabic printing end to end (shaping, right-to-left, digits, bilingual layout); the real
/// document templates in Phase 1 use the same ideas. All text from the user is HTML-encoded.
/// </summary>
public static class TestPageBuilder
{
    private sealed record Line(string En, string Ar, decimal Quantity, decimal Price);

    private static readonly Line[] SampleLines =
    [
        new("Accounting consultancy services — October", "خدمات استشارات محاسبية — شهر أكتوبر", 1m, 850m),
        new("Ledger books — 2026 edition", "دفاتر الأستاذ (كُتُب حسابات) — طبعة ٢٠٢٦", 10m, 35.5m),
        new("Trade discount 10% on line 2", "خصم تجاري ١٠٪ على البند ٢", 1m, -35.5m),
        new("Baba software — annual licence", "برنامج Baba — ترخيص سنوي", 1m, 80m),
    ];

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    /// <param name="companyNameAr">The open company's Arabic name, or a sample name when no company is open.</param>
    /// <param name="currency">The company's base currency, which sets the decimals (2, or 3 for dinars).</param>
    public static string Build(PrintLayout layout, string fontCss, string fontFamily, string companyNameAr, string companyNameEn, Currency currency, DateOnly date)
    {
        var ar = layout == PrintLayout.Arabic;
        var both = layout == PrintLayout.Both;
        var arabicDigits = ar;
        var decimals = currency.MinorUnits;

        string Pick(string en, string arabic) => ar ? arabic : en;
        // Labels: one language, or "English / العربية" together.
        string Label(string en, string arabic) =>
            both ? $"{E(en)} <span class=\"ar\" lang=\"ar\" dir=\"rtl\">{E(arabic)}</span>" : E(Pick(en, arabic));

        var lineAmounts = SampleLines.Select(l => Money.Of(l.Quantity * l.Price, currency).Amount).ToArray();
        var subtotal = lineAmounts.Sum();
        var total = Money.Of(subtotal, currency).Amount;

        // Numbers are isolated left to right so a minus sign always sits in front of the digits, even inside Arabic text.
        string Number(decimal value) => $"<bdi dir=\"ltr\">{NumberText.Amount(value, decimals, arabicDigits)}</bdi>";
        string Css(decimal value) => value < 0 ? " class=\"num neg\"" : " class=\"num\"";

        var rows = new StringBuilder();
        for (var i = 0; i < SampleLines.Length; i++)
        {
            var line = SampleLines[i];
            var description = both
                ? $"{E(line.En)}<div class=\"ar\" lang=\"ar\" dir=\"rtl\">{E(line.Ar)}</div>"
                : E(Pick(line.En, line.Ar));
            rows.Append($"<tr><td>{NumberText.Integer(i + 1, arabicDigits)}</td><td>{description}</td>")
                .Append($"<td class=\"num\">{NumberText.Integer((int)line.Quantity, arabicDigits)}</td>")
                .Append($"<td{Css(line.Price)}>{Number(line.Price)}</td><td{Css(lineAmounts[i])}>{Number(lineAmounts[i])}</td></tr>");
        }

        var language = ar ? "ar" : "en";
        var direction = ar ? "rtl" : "ltr";
        var companyName = both
            ? $"{E(companyNameEn)}<div class=\"ar\" lang=\"ar\" dir=\"rtl\">{E(companyNameAr)}</div>"
            : E(Pick(companyNameEn, companyNameAr));
        var invoiceDate = date.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var dateText = arabicDigits ? NumberText.ToArabicIndic(invoiceDate) : invoiceDate;
        var number = arabicDigits ? NumberText.ToArabicIndic("TEST-0001") : "TEST-0001"; // mixed letters and digits also exercises bidi

        return $$"""
            <!doctype html>
            <html lang="{{language}}" dir="{{direction}}">
            <head>
            <meta charset="utf-8">
            <title>{{E(Pick("Test page", "صفحة تجريبية"))}}</title>
            <style>
            {{fontCss}}
            @page { size: A4; margin: 0; }
            * { box-sizing: border-box; }
            body { margin: 0; font-family: {{fontFamily}}; font-size: 11pt; line-height: 1.55; color: #111; }
            .ar { display: block; font-family: {{fontFamily}}; font-size: 11.5pt; }
            h1 { margin: 0 0 2mm; font-size: 20pt; color: #12356b; }
            .head { display: flex; justify-content: space-between; gap: 8mm; border-bottom: 2px solid #12356b; padding-bottom: 4mm; margin-bottom: 5mm; }
            .company { font-size: 13pt; font-weight: 700; }
            .meta { display: grid; grid-template-columns: auto 1fr; column-gap: 6mm; row-gap: 1mm; margin: 0; }
            .meta dt { font-weight: 700; }
            .meta dd { margin: 0; unicode-bidi: plaintext; }
            table { width: 100%; border-collapse: collapse; margin-top: 3mm; }
            th, td { border: 1px solid #555; padding: 2mm 2.5mm; vertical-align: top; }
            th { background: #eef2f8; text-align: start; }
            td.num, th.num { text-align: end; font-variant-numeric: tabular-nums; white-space: nowrap; }
            .neg { color: #b00020; }
            .totals { margin-top: 3mm; margin-inline-start: auto; inline-size: 60%; }
            .totals td { border: none; border-bottom: 1px solid #aaa; }
            .totals tr.grand td { font-weight: 700; border-top: 2px solid #111; border-bottom: 2px solid #111; }
            .note { margin-top: 6mm; padding: 3mm; border: 1px solid #555; }
            .sign { display: flex; justify-content: space-between; gap: 10mm; margin-top: 20mm; }
            .sign div { flex: 1; border-top: 1px solid #111; padding-top: 1mm; text-align: center; }
            </style>
            </head>
            <body>
            <div class="page" style="padding: 14mm">
            <div class="head">
              <div><div class="company">{{companyName}}</div><div>{{Label("Sample document for testing printing", "مستند تجريبي لاختبار الطباعة")}}</div></div>
              <dl class="meta">
                <dt>{{Label("Invoice no.", "رقم الفاتورة")}}</dt><dd dir="ltr">{{E(number)}}</dd>
                <dt>{{Label("Date", "التاريخ")}}</dt><dd dir="ltr">{{E(dateText)}}</dd>
                <dt>{{Label("Currency", "العملة")}}</dt><dd dir="ltr">{{E(currency.Code)}}</dd>
              </dl>
            </div>
            <h1>{{Label("Sales invoice", "فاتورة مبيعات")}}</h1>
            <table>
              <thead><tr>
                <th>#</th><th>{{Label("Description", "البيان")}}</th>
                <th class="num">{{Label("Qty", "الكمية")}}</th><th class="num">{{Label("Unit price", "سعر الوحدة")}}</th><th class="num">{{Label("Amount", "المبلغ")}}</th>
              </tr></thead>
              <tbody>{{rows}}</tbody>
            </table>
            <table class="totals">
              <tr><td>{{Label("Subtotal", "المجموع الفرعي")}}</td><td class="num">{{Number(subtotal)}}</td></tr>
              <tr class="grand"><td>{{Label("Total due", "الإجمالي المستحق")}}</td><td class="num">{{E(currency.Code)}} {{Number(total)}}</td></tr>
            </table>
            <div class="note">{{Label("This page checks Arabic printing: letter joining, right-to-left layout, digits and amounts.", "تتحقق هذه الصفحة من الطباعة العربية: اتصال الحروف، والاتجاه من اليمين إلى اليسار، والأرقام والمبالغ.")}}</div>
            <div class="sign"><div>{{Label("Received by", "توقيع المستلم")}}</div><div>{{Label("Authorised signature and stamp", "الختم والتوقيع المعتمد")}}</div></div>
            </div>
            </body>
            </html>
            """;
    }
}
