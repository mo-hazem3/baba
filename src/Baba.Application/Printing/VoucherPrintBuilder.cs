using System.Text;
using Baba.Application.Accounting;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Localization;

namespace Baba.Application.Printing;

/// <summary>Everything a voucher printout shows, gathered by the caller.</summary>
public sealed record VoucherPrintData(
    VoucherDto Voucher,
    IReadOnlyDictionary<Guid, AccountDto> Accounts,
    string CompanyNameEn,
    string CompanyNameAr,
    Currency Currency,
    CurrencyWords Words);

/// <summary>
/// The printed voucher (brief section 10.1): logo, company name, number and date, the lines, the total, the amount in words,
/// signature boxes and the stamp. What shows is controlled by the company's print template.
/// </summary>
public static class VoucherPrintBuilder
{
    public static string Build(
        VoucherPrintData data, PrintLayout layout, PrintSettings settings, string fontCss, string fontFamily,
        string? logoDataUrl, string? stampDataUrl, DateTime printedAt)
    {
        var voucher = data.Voucher;
        var ai = settings.ArabicIndicDigits;
        var minor = data.Currency.MinorUnits;
        string L(string en, string ar) => PrintHtml.Label(layout, en, ar);
        string AccountName(Guid id) => data.Accounts.TryGetValue(id, out var a)
            ? PrintHtml.Digits($"{PrintHtml.E(a.Code)} {PrintHtml.Name(layout, a.NameEn, a.NameAr)}", layout, false)
            : "";

        var (titleEn, titleAr, cashEn, cashAr) = voucher.Kind switch
        {
            VoucherKind.Payment => ("Payment voucher — money paid", "سند صرف", "Paid from", "مدفوع من"),
            VoucherKind.Receipt => ("Receipt voucher — money received", "سند قبض", "Received into", "مستلم في"),
            VoucherKind.Transfer => ("Transfer voucher — money moved between accounts", "سند تحويل", "Transferred from", "محوَّل من"),
            VoucherKind.Opening => ("Opening balances", "أرصدة افتتاحية", "", ""),
            VoucherKind.Closing => ("Year-end closing entry", "قيد إقفال السنة المالية", "", ""),
            _ => ("Journal voucher", "قيد يومية", "", ""),
        };
        var numberText = voucher.Number is null ? L("Draft", "مسودة") : PrintHtml.E(PrintHtml.Digits(voucher.Number, layout, ai));

        var facts = new StringBuilder("<dl class=\"meta\">")
            .Append($"<dt>{L("Voucher no.", "رقم السند")}</dt><dd dir=\"ltr\">{numberText}</dd>")
            .Append($"<dt>{L("Date", "التاريخ")}</dt><dd dir=\"ltr\">{PrintHtml.Date(voucher.Date, layout, ai)}</dd>");
        if (voucher.Reference is not null)
            facts.Append($"<dt>{L("Reference", "المرجع")}</dt><dd>{PrintHtml.E(voucher.Reference)}</dd>");
        facts.Append($"<dt>{L("Currency", "العملة")}</dt><dd dir=\"ltr\">{PrintHtml.E(voucher.CurrencyCode)}</dd></dl>");

        var html = new StringBuilder();
        html.Append(PrintHtml.Header(layout, settings, data.CompanyNameEn, data.CompanyNameAr, logoDataUrl, facts.ToString()));
        html.Append($"<h1>{L(titleEn, titleAr)}{(voucher.Status == VoucherStatus.Draft ? $" <span class=\"badge\">{L("DRAFT", "مسودة")}</span>" : "")}</h1>");

        if (voucher.Kind.UsesCashAccount() && voucher.CashAccountId is { } cashId)
            html.Append($"<div><strong>{L(cashEn, cashAr)}:</strong> {AccountName(cashId)}</div>");

        var journal = voucher.Kind.HasFreeLines();
        html.Append("<table><thead><tr>")
            .Append($"<th>#</th><th>{L("Account", "الحساب")}</th><th>{L("Description", "البيان")}</th>");
        html.Append(journal
            ? $"<th class=\"num\">{L("Debit", "مدين")}</th><th class=\"num\">{L("Credit", "دائن")}</th>"
            : $"<th class=\"num\">{L("Amount", "المبلغ")}</th>");
        html.Append("</tr></thead><tbody>");

        var number = 0;
        foreach (var line in voucher.Lines)
        {
            number++;
            html.Append($"<tr><td>{PrintHtml.Digits(number.ToString(), layout, ai)}</td><td>{AccountName(line.AccountId)}</td><td>{PrintHtml.E(line.Description)}</td>");
            html.Append(journal
                ? $"<td class=\"num\">{(line.Debit > 0 ? PrintHtml.Amount(line.Debit, minor, layout, ai) : "")}</td><td class=\"num\">{(line.Credit > 0 ? PrintHtml.Amount(line.Credit, minor, layout, ai) : "")}</td>"
                : $"<td class=\"num\">{PrintHtml.Amount(voucher.Kind == VoucherKind.Receipt ? line.Credit : line.Debit, minor, layout, ai)}</td>");
            html.Append("</tr>");
        }

        var totalDebit = voucher.Lines.Sum(l => l.Debit);
        var totalCredit = voucher.Lines.Sum(l => l.Credit);
        html.Append("<tr class=\"r-total\">").Append($"<td colspan=\"3\">{L("Total", "الإجمالي")} ({PrintHtml.E(voucher.CurrencyCode)})</td>");
        html.Append(journal
            ? $"<td class=\"num\">{PrintHtml.Amount(totalDebit, minor, layout, ai)}</td><td class=\"num\">{PrintHtml.Amount(totalCredit, minor, layout, ai)}</td>"
            : $"<td class=\"num\">{PrintHtml.Amount(voucher.Total, minor, layout, ai)}</td>");
        html.Append("</tr></tbody></table>");

        if (settings.ShowAmountInWords && voucher.Total > 0)
        {
            html.Append("<div class=\"box\">");
            if (layout != PrintLayout.Arabic)
                html.Append($"<div><strong>Amount in words:</strong> {PrintHtml.E(AmountInWords.English(voucher.Total, data.Words))}</div>");
            if (layout != PrintLayout.English)
                html.Append($"<div dir=\"rtl\" lang=\"ar\"><strong>المبلغ كتابةً:</strong> {PrintHtml.E(AmountInWords.Arabic(voucher.Total, data.Words))}</div>");
            html.Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(voucher.Memo))
            html.Append($"<div class=\"box\"><strong>{L("Notes", "ملاحظات")}:</strong> {PrintHtml.E(voucher.Memo)}</div>");

        if (settings.ShowSignatures || (settings.ShowStamp && stampDataUrl is not null))
        {
            var (first, second, third) = voucher.Kind switch
            {
                VoucherKind.Payment => (("Prepared by", "إعداد"), ("Approved by", "اعتماد"), ("Received by", "توقيع المستلم")),
                VoucherKind.Receipt => (("Prepared by", "إعداد"), ("Approved by", "اعتماد"), ("Paid by", "توقيع الدافع")),
                VoucherKind.Transfer => (("Prepared by", "إعداد"), ("Approved by", "اعتماد"), ("Received by", "توقيع المستلم")),
                _ => (("Prepared by", "إعداد"), ("Reviewed by", "مراجعة"), ("Approved by", "اعتماد")),
            };

            html.Append("<div class=\"sign\">");
            if (settings.ShowSignatures)
                html.Append($"<div>{L(first.Item1, first.Item2)}</div><div>{L(second.Item1, second.Item2)}</div><div>{L(third.Item1, third.Item2)}</div>");
            if (settings.ShowStamp && stampDataUrl is not null)
                html.Append($"<div style=\"border-top: none\"><img src=\"{stampDataUrl}\" alt=\"\">{L("Stamp", "الختم")}</div>");
            html.Append("</div>");
        }

        html.Append(PrintHtml.Footer(layout, settings));
        return PrintHtml.Document(layout, titleEn, fontCss, fontFamily, landscape: false, html.ToString());
    }
}
