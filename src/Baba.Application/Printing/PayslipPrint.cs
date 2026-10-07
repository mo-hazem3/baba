using System.Text;
using Baba.Application.Payroll;
using Baba.Domain;
using Baba.Domain.Payroll;

namespace Baba.Application.Printing;

/// <summary>Everything one payslip printout shows, gathered by the caller.</summary>
public sealed record PayslipPrintData(
    DateOnly Month,
    PayslipDto Payslip,
    EmployeeDto Employee,
    string CompanyNameEn,
    string CompanyNameAr,
    Currency Currency,
    string? InsuranceNameEn,
    string? InsuranceNameAr);

/// <summary>The printed payslip (brief section 10.4): who, which month, what was earned, what was taken, the net pay, in Arabic, English or both.</summary>
public static class PayslipPrintBuilder
{
    /// <summary>One payslip as a page; several can be joined into one document with <see cref="Document"/>.</summary>
    public static string Page(PayslipPrintData data, PrintLayout layout, PrintSettings settings, string? logoDataUrl, string? stampDataUrl)
    {
        var employee = data.Employee;
        var slip = data.Payslip;
        var ai = settings.ArabicIndicDigits;
        var minor = data.Currency.MinorUnits;
        string L(string en, string ar) => PrintHtml.Label(layout, en, ar);

        var month = data.Month.ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var facts = new StringBuilder("<dl class=\"meta\">")
            .Append($"<dt>{L("Month", "الشهر")}</dt><dd dir=\"ltr\">{PrintHtml.Digits(month, layout, ai)}</dd>")
            .Append($"<dt>{L("Currency", "العملة")}</dt><dd dir=\"ltr\">{PrintHtml.E(data.Currency.Code)}</dd>")
            .Append("</dl>");

        var html = new StringBuilder("<section style=\"break-after: page\">");
        html.Append(PrintHtml.Header(layout, settings, data.CompanyNameEn, data.CompanyNameAr, logoDataUrl, facts.ToString()));
        html.Append($"<h1>{L("Payslip", "قسيمة راتب")}</h1>");

        html.Append("<div class=\"box\">")
            .Append($"<div><strong>{L("Employee", "الموظف")}:</strong> {PrintHtml.Name(layout, employee.NameEn, employee.NameAr)} (<bdi dir=\"ltr\">{PrintHtml.E(employee.Code)}</bdi>)</div>");
        if (!string.IsNullOrWhiteSpace(employee.JobTitle))
            html.Append($"<div><strong>{L("Job title", "المسمى الوظيفي")}:</strong> {PrintHtml.E(employee.JobTitle)}</div>");
        if (!string.IsNullOrWhiteSpace(employee.NationalId))
            html.Append($"<div><strong>{L("ID number", "رقم الهوية")}:</strong> <bdi dir=\"ltr\">{PrintHtml.E(PrintHtml.Digits(employee.NationalId, layout, ai))}</bdi></div>");
        html.Append($"<div><strong>{L("Joined", "تاريخ التعيين")}:</strong> <bdi dir=\"ltr\">{PrintHtml.Date(employee.JoinDate, layout, ai)}</bdi></div>");
        if (!string.IsNullOrWhiteSpace(employee.BankAccount))
            html.Append($"<div><strong>{L("Paid to", "تحويل إلى")}:</strong> {PrintHtml.E(employee.BankName)} <bdi dir=\"ltr\">{PrintHtml.E(employee.BankAccount)}</bdi></div>");
        html.Append("</div>");

        string Label(PayslipItemDto item) => PrintHtml.Name(layout, item.Label, item.LabelAr);
        string Row(string label, decimal amount, string css = "") =>
            $"<tr{(css.Length == 0 ? "" : $" class=\"{css}\"")}><td>{label}</td><td class=\"num\">{PrintHtml.Amount(amount, minor, layout, ai)}</td></tr>";

        html.Append($"<table><thead><tr><th>{L("Earnings", "المستحقات")}</th><th class=\"num\">{L("Amount", "المبلغ")}</th></tr></thead><tbody>");
        foreach (var item in slip.Items.Where(i => i.Kind == SalaryComponentKind.Earning && i.Amount != 0))
            html.Append(Row(Label(item), item.Amount));
        html.Append(Row(L("Total earnings", "إجمالي المستحقات"), slip.Earnings, "r-subtotal"));
        html.Append("</tbody></table>");

        var hasDeductions = slip.Deductions != 0 || slip.EmployeeInsurance != 0;
        if (hasDeductions)
        {
            html.Append($"<table><thead><tr><th>{L("Deductions", "الاستقطاعات")}</th><th class=\"num\">{L("Amount", "المبلغ")}</th></tr></thead><tbody>");
            if (slip.EmployeeInsurance != 0)
                html.Append(Row(data.InsuranceNameEn is null ? L("Social insurance", "التأمينات الاجتماعية") : PrintHtml.Label(layout, data.InsuranceNameEn, data.InsuranceNameAr ?? data.InsuranceNameEn), slip.EmployeeInsurance));
            foreach (var item in slip.Items.Where(i => i.Kind == SalaryComponentKind.Deduction && i.Amount != 0))
                html.Append(Row(Label(item), item.Amount));
            html.Append(Row(L("Total deductions", "إجمالي الاستقطاعات"), slip.Deductions + slip.EmployeeInsurance, "r-subtotal"));
            html.Append("</tbody></table>");
        }

        html.Append($"<table class=\"totals\"><tbody><tr class=\"grand\"><td>{L("Net pay", "صافي الراتب")}</td><td class=\"num\">{PrintHtml.Amount(slip.Net, minor, layout, ai)}</td></tr></tbody></table>");

        if (settings.ShowSignatures || (settings.ShowStamp && stampDataUrl is not null))
        {
            html.Append("<div class=\"sign\">");
            if (settings.ShowSignatures)
                html.Append($"<div>{L("Prepared by", "إعداد")}</div><div>{L("Employee", "الموظف")}</div>");
            if (settings.ShowStamp && stampDataUrl is not null)
                html.Append($"<div style=\"border-top: none\"><img src=\"{stampDataUrl}\" alt=\"\">{L("Stamp", "الختم")}</div>");
            html.Append("</div>");
        }

        html.Append(PrintHtml.Footer(layout, settings));
        html.Append("</section>");
        return html.ToString();
    }

    public static string Document(PrintLayout layout, string fontCss, string fontFamily, IEnumerable<string> pages) =>
        PrintHtml.Document(layout, "Payslip", fontCss, fontFamily, landscape: false, string.Concat(pages));
}

/// <summary>Makes the PDF of one payslip, or of every payslip of a month in one file.</summary>
public sealed class PayslipPrintService(
    IPdfRenderer? renderer,
    IPrintFonts fonts,
    Companies.ICompanyFiles files,
    IBrandingStore branding,
    PayrollService payroll,
    EmployeeService employees)
{
    public bool IsAvailable => renderer is not null;

    /// <summary>The payslip of one employee (<paramref name="payslipId"/>), or all of the month's when it is null.</summary>
    public async Task<byte[]> RenderAsync(Guid runId, Guid? payslipId, PrintLayout? layout, CancellationToken cancellationToken = default)
    {
        var pdf = renderer ?? throw new InvalidOperationException("This host cannot create PDFs.");
        var company = files.Current ?? throw new Companies.CompanyFileException(Companies.CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var run = await payroll.GetRunAsync(runId, cancellationToken) ?? throw new NotFoundException("payroll-run");
        var slips = payslipId is { } id ? run.Payslips.Where(p => p.Id == id).ToList() : run.Payslips.ToList();
        if (slips.Count == 0)
            throw new NotFoundException("payslip");

        var settings = await branding.GetSettingsAsync(cancellationToken);
        var currency = Localization.CurrencyCatalog.Find(company.BaseCurrencyCode)?.Currency ?? new Currency(company.BaseCurrencyCode, 2);
        var payrollSettings = await payroll.GetSettingsAsync(cancellationToken);
        var staff = (await employees.ListAsync(cancellationToken)).ToDictionary(e => e.Id);
        var effective = layout ?? settings.DefaultLayout;
        var logo = await ImageAsync(BrandingImage.Logo, cancellationToken);
        var stamp = await ImageAsync(BrandingImage.Stamp, cancellationToken);

        var pages = slips
            .Where(s => staff.ContainsKey(s.EmployeeId))
            .OrderBy(s => staff[s.EmployeeId].Code, StringComparer.OrdinalIgnoreCase)
            .Select(s => PayslipPrintBuilder.Page(
                new PayslipPrintData(run.Summary.Month, s, staff[s.EmployeeId], company.NameEn, company.NameAr, currency, payrollSettings.InsuranceNameEn, payrollSettings.InsuranceNameAr),
                effective, settings, logo, stamp));
        var html = PayslipPrintBuilder.Document(effective, fonts.CssFontFaces(), fonts.FontFamily, pages);
        return await pdf.RenderAsync(html, new PdfOptions(), cancellationToken);
    }

    private async Task<string?> ImageAsync(BrandingImage image, CancellationToken cancellationToken) =>
        await branding.GetImageAsync(image, cancellationToken) is { } file
            ? $"data:{file.ContentType};base64,{Convert.ToBase64String(file.Content)}"
            : null;
}
