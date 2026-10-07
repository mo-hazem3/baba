using Baba.Application.Accounting;
using Baba.Application.Trade;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Trade;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Tax codes and tax on documents (brief sections 8 and 10.3). These companies have a country whose pack has VAT codes.</summary>
public class TaxTests : AccountingFixture
{
    private const string Country = "SA";

    private static async Task<TaxCodeDto> CodeAsync(Env e, string code) => (await e.Tax.ListAsync()).Single(c => c.Code == code);

    private static DocumentInput Doc(Env e, DocumentKind kind, DateOnly date, Guid? taxCode, params decimal[] prices) => new(
        kind, date, null, kind.IsSales() ? e.Customer : e.Supplier, null, null, null, null, 0,
        prices.Select(p => new DocumentLineInput(null, null, e.Id(kind.IsSales() ? "511" : "422"), "item", 1, p, 0, null, taxCode)).ToList());

    // ------------------------------------------------------------------ The codes

    [Fact]
    public async Task A_company_gets_the_tax_codes_of_its_country_pointing_at_its_tax_accounts()
    {
        var e = await NewEnvAsync(countryCode: Country);

        var codes = await e.Tax.ListAsync();

        Assert.Equal(4, codes.Count);
        var standard = codes.Single(c => c.Rate == 15m);
        Assert.Equal(TaxTreatment.Standard, standard.Treatment);
        Assert.True(standard.IsDefault);
        Assert.True(standard.FromPack);
        Assert.Equal(e.Id("214"), standard.OutputAccountId);
        Assert.Equal(e.Id("116"), standard.InputAccountId);
        Assert.All(codes.Where(c => c.Treatment != TaxTreatment.Standard), c => Assert.Equal(0m, c.Rate));
        Assert.Single(codes, c => c.IsDefault);
    }

    [Fact]
    public async Task Many_screens_asking_for_the_codes_at_once_make_them_only_once()
    {
        var e = await NewEnvAsync(countryCode: Country);

        var lists = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => e.Tax.ListAsync()));

        Assert.All(lists, l => Assert.Equal(4, l.Count));
        Assert.Equal(4, (await e.Tax.ListAsync()).Count);
    }

    [Fact]
    public async Task A_country_without_tax_has_no_codes()
    {
        var e = await NewEnvAsync();

        Assert.Empty(await e.Tax.ListAsync());
    }

    [Fact]
    public async Task A_pack_code_keeps_its_rate_and_cannot_be_deleted_while_a_custom_code_can_be_changed_and_deleted()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var standard = (await e.Tax.ListAsync()).Single(c => c.Rate == 15m);

        var renamed = await e.Tax.UpdateAsync(standard.Id, new TaxCodeInput("ignored", "ضريبة", "Sales VAT", 99m, TaxTreatment.Zero, null, null, standard.OutputAccountId, standard.InputAccountId));
        Assert.Equal(standard.Code, renamed.Code);
        Assert.Equal(15m, renamed.Rate);
        Assert.Equal("Sales VAT", renamed.NameEn);
        Assert.Contains("tax.pack-code", Codes(await RefusedAsync(() => e.Tax.DeleteAsync(standard.Id))));

        var custom = await e.Tax.CreateAsync(new TaxCodeInput("LOCAL-5", "", "Local 5%", 5m, TaxTreatment.Standard, null, null, e.Id("214"), e.Id("116")));
        Assert.False(custom.FromPack);
        var changed = await e.Tax.UpdateAsync(custom.Id, new TaxCodeInput("LOCAL-7", "", "Local 7%", 7m, TaxTreatment.Standard, null, null, e.Id("214"), e.Id("116")));
        Assert.Equal(7m, changed.Rate);
        await e.Tax.DeleteAsync(custom.Id);
        Assert.DoesNotContain(await e.Tax.ListAsync(), c => c.Id == custom.Id);
    }

    [Fact]
    public async Task Codes_are_checked_and_one_can_be_made_the_default()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var zero = await CodeAsync(e, "SA-VAT-ZERO");

        var duplicate = await RefusedAsync(() => e.Tax.CreateAsync(new TaxCodeInput("sa-vat-zero", "", "Again", 0m, TaxTreatment.Zero, null, null, null, null)));
        var badRate = await RefusedAsync(() => e.Tax.CreateAsync(new TaxCodeInput("X", "", "Too much", 120m, TaxTreatment.Standard, null, null, null, null)));
        var badAccount = await RefusedAsync(() => e.Tax.CreateAsync(new TaxCodeInput("Y", "", "Wrong account", 5m, TaxTreatment.Standard, null, null, e.Id("511"), null)));

        Assert.Contains("tax.code-duplicate", Codes(duplicate));
        Assert.Contains("tax.rate-invalid", Codes(badRate));
        Assert.Contains("tax.account-invalid", Codes(badAccount));

        await e.Tax.SetDefaultAsync(zero.Id);
        var codes = await e.Tax.ListAsync();
        Assert.Equal(["SA-VAT-ZERO"], codes.Where(c => c.IsDefault).Select(c => c.Code));
        await e.Tax.SetActiveAsync(zero.Id, false);
        Assert.DoesNotContain(await e.Tax.ListAsync(), c => c.IsDefault);
    }

    // ------------------------------------------------------------------ Posting

    [Fact]
    public async Task A_taxed_sales_invoice_posts_the_tax_to_the_output_account_and_the_customer_owes_the_total()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var vat = await CodeAsync(e, "SA-VAT-STD");

        var invoice = await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, vat.Id, 1000m));

        Assert.Equal(150m, invoice.TaxTotal);
        Assert.Equal(1000m, invoice.Net);
        Assert.Equal(1150m, invoice.Total);
        Assert.Equal(1150m, await BalanceAsync(e, "113"));
        Assert.Equal(-1000m, await BalanceAsync(e, "511"));
        Assert.Equal(-150m, await BalanceAsync(e, "214"));
        Assert.Equal(150m, invoice.Lines.Single().TaxAmount);
        Assert.Equal(15m, invoice.Lines.Single().TaxRate);
    }

    [Fact]
    public async Task The_document_discount_comes_off_before_tax()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var vat = await CodeAsync(e, "SA-VAT-STD");

        var invoice = await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, vat.Id, 1000m) with { DiscountPercent = 10 });

        Assert.Equal(900m, invoice.Net);
        Assert.Equal(135m, invoice.TaxTotal);
        Assert.Equal(1035m, invoice.Total);
        Assert.Equal(-135m, await BalanceAsync(e, "214"));
    }

    [Fact]
    public async Task Zero_rated_and_untaxed_lines_make_no_tax_line_and_taxed_lines_add_up()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var vat = await CodeAsync(e, "SA-VAT-STD");
        var zero = await CodeAsync(e, "SA-VAT-ZERO");
        var input = Doc(e, DocumentKind.SalesInvoice, Oct6, null, 100m, 200m, 300m) with { };
        input = input with
        {
            Lines =
            [
                input.Lines[0] with { TaxCodeId = vat.Id },
                input.Lines[1] with { TaxCodeId = zero.Id },
                input.Lines[2],
            ],
        };

        var invoice = await e.Trade.IssueAsync(null, input);

        Assert.Equal(15m, invoice.TaxTotal);
        Assert.Equal(615m, invoice.Total);
        Assert.Equal(-15m, await BalanceAsync(e, "214"));
        Assert.Equal(615m, await BalanceAsync(e, "113"));
    }

    [Fact]
    public async Task A_purchase_claims_the_tax_back_and_a_credit_note_takes_the_tax_back_too()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var vat = await CodeAsync(e, "SA-VAT-STD");

        var bill = await e.Trade.IssueAsync(null, Doc(e, DocumentKind.PurchaseInvoice, Oct6, vat.Id, 400m));
        Assert.Equal(460m, bill.Total);
        Assert.Equal(400m, await BalanceAsync(e, "422"));
        Assert.Equal(60m, await BalanceAsync(e, "116"));
        Assert.Equal(-460m, await BalanceAsync(e, "211"));

        var sale = await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, vat.Id, 1000m));
        await e.Trade.IssueSavedAsync((await e.Trade.ConvertAsync(sale.Id, DocumentKind.SalesCreditNote)).Id);
        Assert.Equal(0m, await BalanceAsync(e, "113"));
        Assert.Equal(0m, await BalanceAsync(e, "214"));
    }

    [Fact]
    public async Task Tax_on_a_dollar_invoice_is_posted_in_both_currencies()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var vat = await CodeAsync(e, "SA-VAT-STD");

        var invoice = await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, vat.Id, 1000m) with { CurrencyCode = "USD", ExchangeRate = 0.3m });

        Assert.Equal(1150m, invoice.Total);
        Assert.Equal(345m, await BalanceAsync(e, "113"));
        Assert.Equal(-300m, await BalanceAsync(e, "511"));
        Assert.Equal(-45m, await BalanceAsync(e, "214"));
    }

    [Fact]
    public async Task A_taxed_invoice_is_paid_off_with_its_tax_included()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var vat = await CodeAsync(e, "SA-VAT-STD");
        var invoice = await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, vat.Id, 1000m));

        Assert.Equal(1150m, (await e.Settlements.OutstandingAsync(null)).Single().Outstanding);
        await e.Settlements.SettleAsync(new SettlementInput(e.Customer, Oct6, e.Id("112"), null, null, null, null, [new SettlementLineInput(invoice.Id, 1150m)], 0));

        Assert.Equal(0m, (await e.Settlements.OutstandingAsync(null)).Single().Outstanding);
        Assert.Equal(0m, await BalanceAsync(e, "113"));
    }

    [Fact]
    public async Task A_line_keeps_the_rate_it_was_written_with_when_the_code_changes_later()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var custom = await e.Tax.CreateAsync(new TaxCodeInput("LOCAL", "", "Local", 5m, TaxTreatment.Standard, null, null, e.Id("214"), e.Id("116")));
        var invoice = await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, custom.Id, 1000m));

        await e.Tax.UpdateAsync(custom.Id, new TaxCodeInput("LOCAL", "", "Local", 20m, TaxTreatment.Standard, null, null, e.Id("214"), e.Id("116")));

        var again = await e.Trade.GetAsync(invoice.Id);
        Assert.Equal(50m, again!.TaxTotal);
        Assert.Equal(1050m, again.Total);
        Assert.Contains("tax.in-use", Codes(await RefusedAsync(() => e.Tax.DeleteAsync(custom.Id))));
    }

    // ------------------------------------------------------------------ Checks

    [Fact]
    public async Task A_code_that_does_not_exist_is_off_or_does_not_apply_on_the_date_is_refused_on_its_line()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var vat = await CodeAsync(e, "SA-VAT-STD");
        var zero = await CodeAsync(e, "SA-VAT-ZERO");
        var old = await e.Tax.CreateAsync(new TaxCodeInput("OLD", "", "Old", 5m, TaxTreatment.Standard, null, new DateOnly(2026, 1, 31), e.Id("214"), e.Id("116")));
        await e.Tax.SetActiveAsync(zero.Id, false);

        var unknown = await RefusedAsync(() => e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, Guid.NewGuid(), 10m)));
        var off = await RefusedAsync(() => e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, zero.Id, 10m)));
        var expired = await RefusedAsync(() => e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, old.Id, 10m)));

        Assert.Contains("line.tax-code-unknown", Codes(unknown));
        Assert.Contains("line.tax-code-inactive", Codes(off));
        Assert.Contains("line.tax-code-not-effective", Codes(expired));
        Assert.Equal(0m, await BalanceAsync(e, "113"));
        Assert.NotNull(vat);
    }

    [Fact]
    public async Task A_taxed_invoice_needs_the_tax_account_to_post_to()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var vat = await CodeAsync(e, "SA-VAT-STD");
        await e.Tax.UpdateAsync(vat.Id, new TaxCodeInput(vat.Code, vat.NameAr, vat.NameEn, vat.Rate, vat.Treatment, null, null, null, vat.InputAccountId));

        var refused = await RefusedAsync(() => e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, vat.Id, 100m)));

        Assert.Contains("line.tax-account-missing", Codes(refused));
        var draft = await e.Trade.SaveDraftAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, vat.Id, 100m)); // a draft needs no account yet
        Assert.Equal(15m, draft.TaxTotal);
    }

    [Fact]
    public async Task A_product_brings_its_tax_code_and_conversion_keeps_it()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var vat = await CodeAsync(e, "SA-VAT-STD");
        var product = await e.Products.CreateAsync(new ProductInput("P1", "", "Widget", null, 100m, 60m, e.Id("511"), e.Id("422"), vat.Id));

        var price = await e.Pricing.PriceAsync(product.Id, e.Customer, null, 1m, true);
        Assert.Equal(vat.Id, price.TaxCodeId);

        var quote = await e.Trade.IssueAsync(null, Doc(e, DocumentKind.Quote, Oct6, vat.Id, 100m));
        var invoice = await e.Trade.ConvertAsync(quote.Id, DocumentKind.SalesInvoice);
        Assert.Equal(vat.Id, invoice.Lines.Single().TaxCodeId);
        Assert.Equal(115m, invoice.Total);
    }

    // ------------------------------------------------------------------ The return

    private static decimal? TaxOf(Baba.Application.Reporting.ReportResult report, string label) =>
        report.Rows.Single(r => r.Cells[0].Text == label).Cells[^1].Amount;

    [Fact]
    public async Task The_return_adds_up_sales_and_purchases_by_code_nets_the_notes_and_agrees_with_the_ledger()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var vat = await CodeAsync(e, "SA-VAT-STD");
        var zero = await CodeAsync(e, "SA-VAT-ZERO");
        await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, vat.Id, 1000m));
        await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, zero.Id, 200m));
        var small = await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, vat.Id, 100m));
        await e.Trade.IssueSavedAsync((await e.Trade.ConvertAsync(small.Id, DocumentKind.SalesCreditNote)).Id); // takes the 100 and its 15 back
        await e.Trade.IssueAsync(null, Doc(e, DocumentKind.PurchaseInvoice, Oct6, vat.Id, 400m));
        await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, vat.Id, 1000m) with { CurrencyCode = "USD", ExchangeRate = 0.3m }); // 300 dinars, 45 tax
        await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, new DateOnly(2026, 11, 3), vat.Id, 5000m)); // outside the period

        var report = await e.Reports.TaxReturnAsync(Oct1, Oct31);

        Assert.Equal(195m, TaxOf(report, "Tax on sales (output)"));  // 150 + 0 + (15 - 15) + 45
        Assert.Equal(60m, TaxOf(report, "Tax on purchases (input)"));
        Assert.Equal(135m, report.Rows.Last().Cells[^1].Amount);
        var standard = report.Rows.First(r => r.Cells[0].Text == "SA-VAT-STD");
        Assert.Equal("15%", standard.Cells[2].Text);
        Assert.Equal(1300m, standard.Cells[3].Amount); // 1000 + 100 - 100 + 300
        Assert.Equal(195m, standard.Cells[4].Amount);
        Assert.All(report.Checks, c => Assert.True(c.Passed));
    }

    [Fact]
    public async Task A_manual_entry_on_a_tax_account_makes_the_return_check_fail()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var vat = await CodeAsync(e, "SA-VAT-STD");
        await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, vat.Id, 1000m));
        await e.Vouchers.SaveAndPostAsync(null, Journal(e, Oct6, ("111", 10m, 0), ("214", 0, 10m)));

        var report = await e.Reports.TaxReturnAsync(Oct1, Oct31);

        Assert.False(report.Checks.Single().Passed);
    }

    // ------------------------------------------------------------------ Printing

    [Fact]
    public async Task A_taxed_invoice_prints_as_a_tax_invoice_with_the_tax_lines_and_the_sellers_tax_number()
    {
        var e = await NewEnvAsync(countryCode: Country);
        var vat = await CodeAsync(e, "SA-VAT-STD");
        var invoice = await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, vat.Id, 1000m));

        await e.TradePrint.RenderAsync(invoice.Id, PrintLayout.Both);
        var html = e.Renderer.Html!;

        Assert.Contains("Tax invoice", html);
        Assert.Contains("فاتورة ضريبية", html);
        Assert.Contains("12345", html); // the company's tax number
        Assert.Contains("Total before tax", html);
        Assert.Contains("<bdi dir=\"ltr\">150.000</bdi>", html); // the tax
        Assert.Contains("<bdi dir=\"ltr\">1,150.000</bdi>", html); // the total
        Assert.Contains("15%", html);

        var plain = await e.Trade.IssueAsync(null, Doc(e, DocumentKind.SalesInvoice, Oct6, null, 100m));
        await e.TradePrint.RenderAsync(plain.Id, PrintLayout.English);
        Assert.DoesNotContain("Tax invoice", e.Renderer.Html!);
        Assert.Contains("Sales invoice", e.Renderer.Html!);
    }
}
