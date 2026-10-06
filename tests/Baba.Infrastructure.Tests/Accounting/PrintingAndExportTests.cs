using System.Text;
using Baba.Application;
using Baba.Application.Accounting;
using Baba.Application.Printing;
using Baba.Application.Reporting;
using Baba.Domain;
using Baba.Domain.Accounting;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Tests.Accounting;

public class PrintingAndExportTests : AccountingFixture
{
    // A real 1x1 PNG and a minimal JPEG header.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00];

    private static PrintSettingsInput Settings(PrintSettingsDto s, Func<PrintSettingsInput, PrintSettingsInput>? change = null)
    {
        var input = new PrintSettingsInput(
            s.ShowCompanyName, s.ShowLogo, s.ShowStamp, s.ShowSignatures, s.ShowAmountInWords,
            s.HeaderTextEn, s.HeaderTextAr, s.FooterTextEn, s.FooterTextAr, s.DefaultLayout, s.ArabicIndicDigits);
        return change?.Invoke(input) ?? input;
    }

    // ------------------------------------------------------------------ Logo, stamp and the print template

    [Fact]
    public async Task Only_real_png_and_jpeg_pictures_up_to_one_megabyte_are_accepted_as_logo_or_stamp()
    {
        var e = await NewEnvAsync();

        Assert.Contains("image.required", Codes(await RefusedAsync(() => e.Branding.SetImageAsync(BrandingImage.Logo, "x.png", []))));
        Assert.Contains("image.too-large", Codes(await RefusedAsync(() => e.Branding.SetImageAsync(BrandingImage.Logo, "big.png", [.. Png, .. new byte[BrandingService.MaxImageBytes]]))));
        // A script-bearing SVG, and a text file renamed to .png, are not pictures we accept.
        Assert.Contains("image.unsupported-type", Codes(await RefusedAsync(() => e.Branding.SetImageAsync(BrandingImage.Logo, "logo.svg", Encoding.UTF8.GetBytes("<svg onload=\"alert(1)\"></svg>")))));
        Assert.Contains("image.unsupported-type", Codes(await RefusedAsync(() => e.Branding.SetImageAsync(BrandingImage.Logo, "fake.png", Encoding.UTF8.GetBytes("not really a picture")))));
        Assert.Null(await e.Branding.GetImageAsync(BrandingImage.Logo));

        await e.Branding.SetImageAsync(BrandingImage.Logo, "logo.png", Png);
        await e.Branding.SetImageAsync(BrandingImage.Stamp, @"C:\Users\someone\stamp.jpg", Jpeg);

        var logo = await e.Branding.GetImageAsync(BrandingImage.Logo);
        Assert.Equal(("logo.png", "image/png"), (logo!.Name, logo.ContentType));
        Assert.Equal(Png, logo.Content);
        var stamp = await e.Branding.GetImageAsync(BrandingImage.Stamp);
        Assert.Equal(("stamp.jpg", "image/jpeg"), (stamp!.Name, stamp.ContentType)); // never a path from the user's computer
    }

    [Fact]
    public async Task Replacing_or_removing_a_picture_does_not_leave_the_old_one_in_the_company_file()
    {
        var e = await NewEnvAsync();
        await e.Branding.SetImageAsync(BrandingImage.Logo, "one.png", Png);
        await e.Branding.SetImageAsync(BrandingImage.Logo, "two.png", Png);
        using (var context = e.Files.Create())
            Assert.Equal(["two.png"], await context.Files.Select(f => f.Name).ToListAsync());

        await e.Branding.ClearImageAsync(BrandingImage.Logo);

        Assert.Null(await e.Branding.GetImageAsync(BrandingImage.Logo));
        using (var context = e.Files.Create())
            Assert.Empty(context.Files);
        await e.Branding.ClearImageAsync(BrandingImage.Logo); // nothing to remove: fine
    }

    [Fact]
    public async Task The_print_template_has_sensible_defaults_and_remembers_changes()
    {
        var e = await NewEnvAsync("amal");
        var defaults = await e.Branding.GetSettingsAsync();
        Assert.True(defaults.ShowCompanyName && defaults.ShowLogo && defaults.ShowStamp && defaults.ShowSignatures && defaults.ShowAmountInWords);
        Assert.Equal(PrintLayout.Both, defaults.DefaultLayout);
        Assert.False(defaults.ArabicIndicDigits);
        Assert.False(defaults.HasLogo);

        await e.Branding.SetImageAsync(BrandingImage.Logo, "l.png", Png);
        var saved = await e.Branding.SaveSettingsAsync(Settings(defaults, s => s with
        {
            ShowStamp = false,
            HeaderTextEn = "  Block 5, Sharq  ",
            HeaderTextAr = "قطعة ٥، شرق",
            FooterTextEn = "",
            DefaultLayout = PrintLayout.Arabic,
            ArabicIndicDigits = true,
        }));

        Assert.False(saved.ShowStamp);
        Assert.Equal("Block 5, Sharq", saved.HeaderTextEn);
        Assert.Null(saved.FooterTextEn); // blank text is stored as nothing
        Assert.Equal(PrintLayout.Arabic, saved.DefaultLayout);
        Assert.True(saved.HasLogo);
        Assert.Equal(saved, await e.Branding.GetSettingsAsync());

        // A second save updates the one row (one print template per company) and is audited.
        await e.Branding.SaveSettingsAsync(Settings(saved, s => s with { ShowSignatures = false }));
        using var context = e.Files.Create();
        Assert.Equal(1, await context.PrintSettings.CountAsync());
        Assert.Contains(await context.AuditLog.Where(l => l.EntityName == nameof(PrintSettings)).ToListAsync(), l => l.Action == AuditAction.Updated && l.UserId == "amal");
    }

    [Fact]
    public async Task Invalid_print_settings_are_refused()
    {
        var e = await NewEnvAsync();
        var current = await e.Branding.GetSettingsAsync();

        var tooLong = await RefusedAsync(() => e.Branding.SaveSettingsAsync(Settings(current, s => s with { FooterTextAr = new string('x', 501) })));
        Assert.Contains("print.text-too-long", Codes(tooLong));

        var unknown = await RefusedAsync(() => e.Branding.SaveSettingsAsync(Settings(current, s => s with { DefaultLayout = (PrintLayout)99 })));
        Assert.Contains("print.layout-unknown", Codes(unknown));
    }

    // ------------------------------------------------------------------ The voucher printout

    private async Task<(Env Env, VoucherDto Voucher)> PostedPaymentAsync()
    {
        var e = await NewEnvAsync();
        var voucher = await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 750m), ("423", 120.5m)) with { Memo = "October rent & utilities" });
        return (e, voucher);
    }

    [Fact]
    public async Task A_payment_voucher_prints_in_both_languages_with_the_amount_in_words()
    {
        var (e, voucher) = await PostedPaymentAsync();

        var pdf = await e.Documents.RenderVoucherAsync(voucher.Id, PrintLayout.Both);
        var html = e.Renderer.Html!;

        Assert.Equal("%PDF-fake", Encoding.ASCII.GetString(pdf));
        Assert.Contains("Payment voucher", html);
        Assert.Contains("سند صرف", html);
        Assert.Contains("PV-2026-0001", html);
        Assert.Contains("06/10/2026", html);
        Assert.Contains("Paid from", html);
        Assert.Contains("111 Cash on hand", html);
        Assert.Contains("422 Rent", html);
        Assert.Contains("<span class=\"ar\" lang=\"ar\" dir=\"rtl\">الإيجار</span>", html); // the Arabic name sits under the English one
        Assert.Contains("<bdi dir=\"ltr\">870.500</bdi>", html); // the total, three decimals for dinars
        Assert.Contains("Eight hundred seventy Kuwaiti dinars and five hundred fils only", html);
        Assert.Contains("فقط ثمانمائة وسبعون ديناراً كويتياً وخمسمائة فلس لا غير", html);
        Assert.Contains("Prepared by", html);
        Assert.Contains("Received by", html);
        Assert.Contains("October rent &amp; utilities", html); // notes are encoded
        Assert.DoesNotContain("DRAFT", html);
        Assert.False(e.Renderer.Options!.Landscape);
    }

    [Fact]
    public async Task The_arabic_only_and_english_only_printouts_contain_only_their_language()
    {
        var (e, voucher) = await PostedPaymentAsync();

        await e.Documents.RenderVoucherAsync(voucher.Id, PrintLayout.Arabic);
        var arabic = e.Renderer.Html!;
        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", arabic);
        Assert.Contains("المبلغ كتابةً", arabic);
        Assert.DoesNotContain("Paid from", arabic);
        Assert.DoesNotContain("Amount in words", arabic);

        await e.Documents.RenderVoucherAsync(voucher.Id, PrintLayout.English);
        var english = e.Renderer.Html!;
        Assert.Contains("<html lang=\"en\" dir=\"ltr\">", english);
        Assert.Contains("Amount in words", english);
        Assert.DoesNotContain("المبلغ كتابةً", english);
        Assert.DoesNotContain("سند صرف", english);
    }

    [Fact]
    public async Task Without_a_layout_the_companys_default_is_used()
    {
        var (e, voucher) = await PostedPaymentAsync();
        await e.Branding.SaveSettingsAsync(Settings(await e.Branding.GetSettingsAsync(), s => s with { DefaultLayout = PrintLayout.Arabic }));

        await e.Documents.RenderVoucherAsync(voucher.Id, null);

        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", e.Renderer.Html);
    }

    [Fact]
    public async Task The_print_template_controls_what_shows_and_arabic_indic_digits_are_optional()
    {
        var (e, voucher) = await PostedPaymentAsync();
        await e.Branding.SetImageAsync(BrandingImage.Logo, "logo.png", Png);
        await e.Branding.SetImageAsync(BrandingImage.Stamp, "stamp.png", Png);
        var settings = await e.Branding.GetSettingsAsync();

        await e.Documents.RenderVoucherAsync(voucher.Id, PrintLayout.English);
        Assert.Equal(2, Occurrences(e.Renderer.Html!, "data:image/png;base64,")); // logo and stamp

        await e.Branding.SaveSettingsAsync(Settings(settings, s => s with { ShowLogo = false, ShowStamp = false, ShowSignatures = false, ShowAmountInWords = false, ShowCompanyName = false }));
        await e.Documents.RenderVoucherAsync(voucher.Id, PrintLayout.English);
        var plain = e.Renderer.Html!;
        Assert.DoesNotContain("data:image/png", plain);
        Assert.DoesNotContain("Prepared by", plain);
        Assert.DoesNotContain("Amount in words", plain);
        Assert.DoesNotContain("class=\"company\"", plain);

        await e.Branding.SaveSettingsAsync(Settings(settings, s => s with { ArabicIndicDigits = true }));
        await e.Documents.RenderVoucherAsync(voucher.Id, PrintLayout.Arabic);
        Assert.Contains("<bdi dir=\"ltr\">٨٧٠٫٥٠٠</bdi>", e.Renderer.Html);
        Assert.Contains("PV-٢٠٢٦-٠٠٠١", e.Renderer.Html);
        await e.Documents.RenderVoucherAsync(voucher.Id, PrintLayout.English); // English printouts keep Western digits
        Assert.Contains("<bdi dir=\"ltr\">870.500</bdi>", e.Renderer.Html);
    }

    [Fact]
    public async Task A_draft_prints_with_a_draft_mark_and_a_receipt_and_journal_use_their_own_wording()
    {
        var e = await NewEnvAsync();
        var draft = await e.Vouchers.SaveDraftAsync(null, Payment(e, Oct6, ("422", 5m)));
        var receipt = await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 40m)));
        var journal = await e.Vouchers.SaveAndPostAsync(null, Journal(e, Oct6, ("422", 9m, 0), ("512", 0, 9m)));

        await e.Documents.RenderVoucherAsync(draft.Id, PrintLayout.English);
        Assert.Contains("<span class=\"badge\">DRAFT</span>", e.Renderer.Html);
        Assert.DoesNotContain("PV-2026", e.Renderer.Html);

        await e.Documents.RenderVoucherAsync(receipt.Id, PrintLayout.English);
        Assert.Contains("Receipt voucher", e.Renderer.Html);
        Assert.Contains("Received into", e.Renderer.Html);
        Assert.Contains("112 Bank account", e.Renderer.Html);

        await e.Documents.RenderVoucherAsync(journal.Id, PrintLayout.English);
        var journalHtml = e.Renderer.Html!;
        Assert.Contains("Journal voucher", journalHtml);
        Assert.Contains(">Debit</th>", journalHtml);
        Assert.Contains(">Credit</th>", journalHtml);
        Assert.DoesNotContain("Paid from", journalHtml);
    }

    [Fact]
    public async Task Printing_a_missing_voucher_or_without_a_pdf_engine_says_so()
    {
        var e = await NewEnvAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => e.Documents.RenderVoucherAsync(Guid.NewGuid(), null));

        var noEngine = new DocumentPrintService(null, new Printing.EmbeddedPrintFonts(), e.Files, new Printing.BrandingStore(e.Files),
            e.Vouchers, e.Chart, Clock);
        Assert.False(noEngine.IsAvailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noEngine.RenderVoucherAsync(Guid.NewGuid(), null));
    }

    // ------------------------------------------------------------------ Report exports

    [Fact]
    public async Task A_wide_report_prints_sideways_and_a_narrow_one_does_not()
    {
        var e = await SeedMonthAsync();
        var trialBalance = await e.Reports.TrialBalanceAsync(Oct1, Oct31);
        var profitAndLoss = await e.Reports.ProfitAndLossAsync(Oct1, Oct31);

        var pdf = await e.Exports.ExportAsync(trialBalance, ExportFormat.Pdf, PrintLayout.Both);
        Assert.Equal(("Trial balance - From 01-10-2026 to 31-10-2026.pdf", "application/pdf"), (pdf.FileName, pdf.ContentType));
        Assert.True(e.Renderer.Options!.Landscape);
        Assert.Contains("size: A4 landscape", e.Renderer.Html);
        Assert.Contains("Trial balance", e.Renderer.Html);
        Assert.Contains("ميزان المراجعة", e.Renderer.Html);
        Assert.Contains("✓ ", e.Renderer.Html); // the "debits equal credits" check is printed
        Assert.Contains("<bdi dir=\"ltr\">8,600.000</bdi>", e.Renderer.Html);

        await e.Exports.ExportAsync(profitAndLoss, ExportFormat.Pdf, PrintLayout.English);
        Assert.False(e.Renderer.Options!.Landscape);
        Assert.DoesNotContain("landscape", e.Renderer.Html);
        Assert.DoesNotContain("ميزان", e.Renderer.Html);
    }

    [Fact]
    public async Task A_csv_export_has_a_byte_order_mark_for_excel_exact_amounts_and_quotes_where_needed()
    {
        var e = await SeedMonthAsync();
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, new DateOnly(2026, 10, 20), ("424", 1m)) with { Memo = "Pens, \"blue\"" });
        var journal = await e.Reports.JournalAsync(Oct1, Oct31);

        var file = await e.Exports.ExportAsync(journal, ExportFormat.Csv, PrintLayout.English);

        Assert.Equal("Journal - From 01-10-2026 to 31-10-2026.csv", file.FileName);
        Assert.Equal([0xEF, 0xBB, 0xBF], file.Content.Take(3)); // so Excel reads Arabic correctly
        var lines = Encoding.UTF8.GetString(file.Content, 3, file.Content.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Date,Voucher,Account,Description,Debit,Credit", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("2026-10-01,RV-2026-0001,112 Bank account,") && l.EndsWith(",10000.000,"));
        Assert.Contains(lines, l => l.Contains("\"Pens, \"\"blue\"\"\"")); // a comma and quotes inside a field
        Assert.EndsWith(",19071.500,19071.500", lines[^1]);

        var both = ReportCsv.Build(journal, PrintLayout.Both).Split("\r\n")[0];
        Assert.Equal("Date / التاريخ,Voucher / السند,Account / الحساب,Description / البيان,Debit / مدين,Credit / دائن", both);
        Assert.StartsWith("التاريخ,", ReportCsv.Build(journal, PrintLayout.Arabic));
    }

    [Fact]
    public async Task An_excel_export_has_real_numbers_and_dates_bold_totals_and_a_right_to_left_sheet_for_arabic()
    {
        var e = await SeedMonthAsync();
        var trialBalance = await e.Reports.TrialBalanceAsync(Oct1, Oct31);
        var journal = await e.Reports.JournalAsync(Oct1, Oct31);

        var file = await e.Exports.ExportAsync(trialBalance, ExportFormat.Xlsx, PrintLayout.Arabic);
        Assert.EndsWith(".xlsx", file.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);

        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var sheet = workbook.Worksheet(1);
        Assert.Equal("ميزان المراجعة", sheet.Name);
        Assert.True(sheet.RightToLeft);
        Assert.Equal("شركة النور للتجارة", sheet.Cell(1, 1).GetString()); // the company name (in the sheet's language) is on top
        Assert.Equal("الرمز", sheet.Cell(6, 1).GetString());

        // Find the bank row; its closing debit is a real number, formatted with three decimals.
        var bankRow = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "112");
        var closing = bankRow.Cell(7);
        Assert.Equal(XLDataType.Number, closing.DataType);
        Assert.Equal(8600d, closing.GetDouble());
        Assert.Contains("0.000", closing.Style.NumberFormat.Format);
        Assert.Equal(XLDataType.Blank, bankRow.Cell(8).DataType); // a zero is left empty

        var totals = sheet.RowsUsed().Single(r => r.Cell(2).GetString() == "الإجماليات");
        Assert.True(totals.Cell(2).Style.Font.Bold);
        Assert.Equal(sheet.Cell(totals.RowNumber(), 7).GetDouble(), sheet.Cell(totals.RowNumber(), 8).GetDouble());

        // The journal has real dates.
        var journalFile = await e.Exports.ExportAsync(journal, ExportFormat.Xlsx, PrintLayout.English);
        using var journalBook = new XLWorkbook(new MemoryStream(journalFile.Content));
        var firstEntry = journalBook.Worksheet(1).Cell(7, 1);
        Assert.Equal(XLDataType.DateTime, firstEntry.DataType);
        Assert.Equal(new DateTime(2026, 10, 1), firstEntry.GetDateTime());
        Assert.False(journalBook.Worksheet(1).RightToLeft);
    }

    [Fact]
    public async Task The_chart_of_accounts_and_the_voucher_list_export_like_any_other_report()
    {
        var e = await SeedMonthAsync();
        await e.Vouchers.SaveDraftAsync(null, Payment(e, Oct6, ("424", 4m)));

        var chart = await e.Listings.ChartOfAccountsAsync();
        var codes = chart.Rows.Select(r => r.Cells[0].Text).ToList();
        Assert.Equal((await e.Chart.ListAsync()).Count, chart.Rows.Count);
        Assert.Equal(codes.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).Count(), codes.Count);
        Assert.Equal(RowStyle.Group, chart.Rows.Single(r => r.Cells[0].Text == "11").Style);
        Assert.Equal(chart.Rows.Single(r => r.Cells[0].Text == "11").Level + 1, chart.Rows.Single(r => r.Cells[0].Text == "111").Level);

        var vouchers = await e.Listings.VouchersAsync(new VoucherSearch());
        Assert.Equal(10, vouchers.Rows.Count); // 8 posted + 1 draft + the total row
        Assert.Equal(RowStyle.Total, vouchers.Rows[^1].Style);
        Assert.Equal(19_474.5m, vouchers.Rows[^1].Cells[6].Amount); // all 9 vouchers added up: 400 + 10,000 + 2,000 + 870.5 + 3,500 + 1,200 + 500 + 1,000 + 4

        var csv = Encoding.UTF8.GetString((await e.Exports.ExportAsync(vouchers, ExportFormat.Csv, PrintLayout.English)).Content);
        Assert.Contains("PV-2026-0001", csv);
        Assert.Contains("Draft", csv);
    }

    // ------------------------------------------------------------------ Summary page numbers

    [Fact]
    public async Task The_dashboard_shows_cash_receivables_payables_and_this_months_profit()
    {
        var e = await SeedMonthAsync();
        await e.Vouchers.SaveDraftAsync(null, Payment(e, Oct6, ("424", 4m)));
        await e.Vouchers.SaveAndPostAsync(null, Journal(e, new DateOnly(2026, 10, 20), ("423", 100m, 0), ("211", 0, 100m))); // a bill we owe

        var dashboard = await e.Dashboard.GetAsync();

        Assert.Equal(("KWD", 3), (dashboard.CurrencyCode, dashboard.MinorUnits));
        Assert.Equal(1_629.5m + 8_600m, dashboard.Cash);             // cash on hand + the bank (all-time, live)
        Assert.Equal(2_300m, dashboard.Receivables);
        Assert.Equal(100m, dashboard.Payables);
        Assert.Equal(4_000m - 1_970.5m, dashboard.ProfitThisMonth);  // October revenue less October expenses (clock is 6 October 2026)
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), (dashboard.MonthStart, dashboard.MonthEnd));
        Assert.Equal(1, dashboard.DraftVouchers);
    }

    [Fact]
    public async Task A_brand_new_company_has_a_dashboard_of_zeros()
    {
        var e = await NewEnvAsync();

        var dashboard = await e.Dashboard.GetAsync();

        Assert.Equal((0m, 0m, 0m, 0m, 0), (dashboard.Cash, dashboard.Receivables, dashboard.Payables, dashboard.ProfitThisMonth, dashboard.DraftVouchers));
    }

    private static int Occurrences(string text, string find) => text.Split(find).Length - 1;
}
