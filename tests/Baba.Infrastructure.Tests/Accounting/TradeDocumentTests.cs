using Baba.Application;
using Baba.Application.Accounting;
using Baba.Application.Trade;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Trade;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Sales and purchase documents (brief section 10.3): maths, posting through a system voucher, conversions, products and prices.</summary>
public class TradeDocumentTests : AccountingFixture
{
    private static DocumentLineInput Line(Env e, string account, decimal quantity, decimal price, decimal discount = 0) =>
        new(null, null, e.Id(account), "item", quantity, price, discount);

    private static DocumentInput Invoice(Env e, DocumentKind kind, DateOnly date, params DocumentLineInput[] lines) => new(
        kind, date, null, kind.IsSales() ? e.Customer : e.Supplier, null, null, null, null, 0, lines);

    // ------------------------------------------------------------------ The maths

    [Fact]
    public void Document_math_adds_up_to_the_total_even_when_the_discount_leaves_a_rounding_remainder()
    {
        var currency = new Currency("BHD", 3);
        var lines = new[]
        {
            new DocumentLine { Quantity = 3, UnitPrice = 0.333m },
            new DocumentLine { Quantity = 1, UnitPrice = 0.667m },
            new DocumentLine { Quantity = 2, UnitPrice = 0.5m, DiscountPercent = 10 },
        };

        var totals = DocumentMath.Compute(lines, 7.5m, currency);

        Assert.Equal(totals.Total, totals.PostedAmounts.Sum());
        Assert.Equal(totals.Subtotal - totals.DiscountAmount, totals.Total);
    }

    // ------------------------------------------------------------------ Posting

    [Fact]
    public async Task Issuing_a_sales_invoice_posts_to_the_customer_and_revenue_and_takes_the_voucher_number()
    {
        var e = await NewEnvAsync();

        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, Line(e, "511", 2, 100m), Line(e, "512", 1, 50m)));

        Assert.Equal(DocumentStatus.Issued, invoice.Status);
        Assert.StartsWith("SI-2026-", invoice.Number);
        Assert.Equal(250m, invoice.Total);
        Assert.Equal(250m, await BalanceAsync(e, "113"));
        Assert.Equal(-200m, await BalanceAsync(e, "511"));
        Assert.Equal(-50m, await BalanceAsync(e, "512"));
    }

    [Fact]
    public async Task A_document_discount_is_taken_off_revenue_and_the_customer_owes_the_discounted_total()
    {
        var e = await NewEnvAsync();

        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, Line(e, "511", 1, 1000m)) with { DiscountPercent = 10 });

        Assert.Equal(900m, invoice.Total);
        Assert.Equal(900m, await BalanceAsync(e, "113"));
        Assert.Equal(-900m, await BalanceAsync(e, "511"));
    }

    [Fact]
    public async Task A_credit_note_reverses_an_invoice_and_a_purchase_invoice_credits_the_supplier()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, Line(e, "511", 1, 400m)));

        var note = await e.Trade.ConvertAsync(invoice.Id, DocumentKind.SalesCreditNote);
        Assert.Equal(DocumentStatus.Draft, note.Status);
        Assert.Equal(invoice.Id, note.SourceDocumentId);
        var issued = await e.Trade.IssueSavedAsync(note.Id);

        Assert.StartsWith("SC-2026-", issued.Number);
        Assert.Equal(0m, await BalanceAsync(e, "113"));
        Assert.Equal(DocumentStatus.Issued, (await e.Trade.GetAsync(invoice.Id))!.Status); // an invoice can have notes and stays issued

        var purchase = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.PurchaseInvoice, Oct6, Line(e, "422", 1, 300m)));
        Assert.StartsWith("PI-2026-", purchase.Number);
        Assert.Equal(-300m, await BalanceAsync(e, "211"));
        Assert.Equal(300m, await BalanceAsync(e, "422"));
    }

    [Fact]
    public async Task An_invoice_in_a_foreign_currency_posts_in_both_currencies()
    {
        var e = await NewEnvAsync();
        await e.Rates.SetAsync(new CurrencyRateInput("USD", Oct1, 0.3m));

        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, Line(e, "511", 1, 1000m)) with { CurrencyCode = "USD" });

        Assert.Equal("USD", invoice.CurrencyCode);
        Assert.Equal(0.3m, invoice.ExchangeRate); // taken from the rate table
        Assert.Equal(300m, await BalanceAsync(e, "113")); // base currency
        Assert.Equal(-300m, await BalanceAsync(e, "511"));
    }

    [Fact]
    public async Task Editing_an_issued_invoice_makes_its_entries_again_and_deleting_it_removes_them()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, Line(e, "511", 1, 100m)));

        var edited = await e.Trade.IssueAsync(invoice.Id, Invoice(e, DocumentKind.SalesInvoice, Oct6, Line(e, "511", 1, 160m)));

        Assert.Equal(invoice.Number, edited.Number);
        Assert.Equal(160m, await BalanceAsync(e, "113"));

        await e.Trade.DeleteAsync(invoice.Id);
        Assert.Equal(0m, await BalanceAsync(e, "113"));
        Assert.Null(await e.Trade.GetAsync(invoice.Id));
    }

    [Fact]
    public async Task An_invoice_in_a_locked_month_is_refused()
    {
        var e = await NewEnvAsync();
        await e.Periods.SetLockedAsync(Oct1, true);

        var refused = await RefusedAsync(() => e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, Line(e, "511", 1, 100m))));

        Assert.Contains("date.locked-period", Codes(refused));
    }

    [Fact]
    public async Task Validation_names_the_field_and_the_code()
    {
        var e = await NewEnvAsync();

        var wrongParty = await RefusedAsync(() => e.Trade.SaveDraftAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, Line(e, "511", 1, 10m)) with { PartyId = e.Supplier }));
        var noAccount = await RefusedAsync(() => e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, new DocumentLineInput(null, null, null, "x", 1, 10m))));
        var zero = await RefusedAsync(() => e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, Line(e, "511", 1, 0m))));

        Assert.Contains("document.party-wrong-kind", Codes(wrongParty));
        Assert.Contains("line.account-required", Codes(noAccount));
        Assert.Contains("document.total-not-positive", Codes(zero));
    }

    // ------------------------------------------------------------------ Drafts and the conversion chain

    [Fact]
    public async Task A_draft_has_no_number_and_no_effect_on_the_books()
    {
        var e = await NewEnvAsync();

        var draft = await e.Trade.SaveDraftAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, Line(e, "511", 1, 100m)));

        Assert.Null(draft.Number);
        Assert.Null(draft.VoucherId);
        Assert.Equal(0m, await BalanceAsync(e, "113"));

        var issued = await e.Trade.IssueSavedAsync(draft.Id);
        Assert.NotNull(issued.Number);
        Assert.Equal(100m, await BalanceAsync(e, "113"));
    }

    [Fact]
    public async Task A_quote_becomes_an_order_then_a_delivery_note_then_an_invoice_and_each_step_is_used_up()
    {
        var e = await NewEnvAsync();
        var quote = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.Quote, Oct6, Line(e, "511", 2, 75m)));
        Assert.StartsWith("QT-2026-", quote.Number);
        Assert.Equal(0m, await BalanceAsync(e, "113")); // a quote posts nothing

        var order = await e.Trade.IssueSavedAsync((await e.Trade.ConvertAsync(quote.Id, DocumentKind.SalesOrder)).Id);
        Assert.StartsWith("SO-2026-", order.Number);
        Assert.Equal(DocumentStatus.Converted, (await e.Trade.GetAsync(quote.Id))!.Status);

        var delivery = await e.Trade.IssueSavedAsync((await e.Trade.ConvertAsync(order.Id, DocumentKind.DeliveryNote)).Id);
        Assert.StartsWith("DL-2026-", delivery.Number);

        var invoice = await e.Trade.IssueSavedAsync((await e.Trade.ConvertAsync(delivery.Id, DocumentKind.SalesInvoice)).Id);
        Assert.Equal(150m, invoice.Total);
        Assert.Equal(150m, await BalanceAsync(e, "113"));

        var again = await RefusedAsync(() => e.Trade.ConvertAsync(quote.Id, DocumentKind.SalesInvoice));
        Assert.Contains("document.converted", Codes(again));
        var skip = await RefusedAsync(() => e.Trade.ConvertAsync(invoice.Id, DocumentKind.Quote));
        Assert.Contains("document.cannot-convert", Codes(skip));
    }

    [Fact]
    public async Task Deleting_what_a_document_was_converted_into_gives_the_original_back()
    {
        var e = await NewEnvAsync();
        var order = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.PurchaseOrder, Oct6, Line(e, "422", 1, 80m)));
        var receipt = await e.Trade.ConvertAsync(order.Id, DocumentKind.GoodsReceipt);

        var refused = await RefusedAsync(() => e.Trade.DeleteAsync(order.Id));
        Assert.Contains("document.converted", Codes(refused));

        await e.Trade.DeleteAsync(receipt.Id);

        Assert.Equal(DocumentStatus.Issued, (await e.Trade.GetAsync(order.Id))!.Status);
        Assert.Null((await e.Trade.GetAsync(order.Id))!.ConvertedToId);
    }

    [Fact]
    public async Task Document_numbers_run_in_order_per_kind_and_fiscal_year()
    {
        var e = await NewEnvAsync();

        var first = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.Quote, Oct6, Line(e, "511", 1, 1m)));
        var second = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.Quote, Oct6, Line(e, "511", 1, 1m)));
        var order = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesOrder, Oct6, Line(e, "511", 1, 1m)));

        Assert.Equal("QT-2026-0001", first.Number);
        Assert.Equal("QT-2026-0002", second.Number);
        Assert.Equal("SO-2026-0001", order.Number);
    }

    [Fact]
    public async Task The_document_list_filters_by_kind_and_party()
    {
        var e = await NewEnvAsync();
        await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.Quote, Oct6, Line(e, "511", 1, 10m)));
        await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, Line(e, "511", 1, 20m)));
        await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.PurchaseInvoice, Oct6, Line(e, "422", 1, 30m)));

        var sales = await e.Trade.ListAsync(new DocumentSearch(DocumentKind.SalesInvoice, null, null));
        var supplier = await e.Trade.ListAsync(new DocumentSearch(null, null, e.Supplier, null, null));

        Assert.Equal([20m], sales.Select(s => s.Total));
        Assert.Equal([30m], supplier.Select(s => s.Total));
    }

    [Fact]
    public async Task A_party_or_account_used_on_a_document_cannot_be_deleted()
    {
        var e = await NewEnvAsync();
        await e.Trade.SaveDraftAsync(null, Invoice(e, DocumentKind.Quote, Oct6, Line(e, "511", 1, 10m)));

        await Assert.ThrowsAnyAsync<Exception>(() => e.Parties.DeleteAsync(e.Customer));
        await Assert.ThrowsAnyAsync<Exception>(() => e.Chart.DeleteAsync(e.Id("511")));
    }

    // ------------------------------------------------------------------ Products, price lists, pricing

    [Fact]
    public async Task A_product_used_on_a_document_can_be_switched_off_but_not_deleted_and_codes_are_unique()
    {
        var e = await NewEnvAsync();
        var product = await e.Products.CreateAsync(new ProductInput("P1", "منتج", "Widget", "pcs", 50m, 30m, e.Id("511"), e.Id("422")));
        await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.Quote, Oct6, new DocumentLineInput(null, product.Id, e.Id("511"), null, 1, 50m)));

        var duplicate = await RefusedAsync(() => e.Products.CreateAsync(new ProductInput("p1", "", "Other", null, 1m, 1m, null, null)));
        Assert.Contains("product.code-duplicate", Codes(duplicate));

        var inUse = await RefusedAsync(() => e.Products.DeleteAsync(product.Id));
        Assert.Contains("product.in-use", Codes(inUse));
        Assert.False((await e.Products.SetActiveAsync(product.Id, false)).IsActive);
    }

    [Fact]
    public async Task A_customers_price_list_wins_in_its_currency_otherwise_the_product_price_is_converted()
    {
        var e = await NewEnvAsync();
        var product = await e.Products.CreateAsync(new ProductInput("P1", "منتج", "Widget", "pcs", 100m, 60m, e.Id("511"), e.Id("422")));
        var list = await e.PriceLists.CreateAsync(new PriceListInput("جملة", "Wholesale", null, [new PriceListLineInput(product.Id, 80m)]));
        var customer = await e.Parties.GetAsync(e.Customer);
        await e.Parties.UpdateAsync(e.Customer, new PartyInput(
            PartyKind.Customer, customer.Code, customer.NameAr, customer.NameEn, null, null, null, null, 0, 30, null, list.Id));

        var withList = await e.Pricing.PriceAsync(product.Id, e.Customer, null, 1m, true);
        var other = await e.Pricing.PriceAsync(product.Id, null, null, 1m, true);
        var foreign = await e.Pricing.PriceAsync(product.Id, e.Customer, "USD", 0.25m, true); // list is in the company currency: not used
        var purchase = await e.Pricing.PriceAsync(product.Id, e.Supplier, null, 1m, false);

        Assert.Equal(80m, withList.Price);
        Assert.Equal(100m, other.Price);
        Assert.Equal(400m, foreign.Price); // 100 base units = 400 USD at 0.25 base per USD
        Assert.Equal(60m, purchase.Price);

        var inUse = await RefusedAsync(() => e.PriceLists.DeleteAsync(list.Id));
        Assert.Contains("price-list.in-use", Codes(inUse));
    }
}
