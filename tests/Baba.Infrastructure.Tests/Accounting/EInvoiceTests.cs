using Baba.Application.Trade;
using Baba.Domain;
using Baba.Domain.Trade;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>What a country's e-invoicing rules change: issued invoices are locked, and carry a QR code when printed (brief section 8).</summary>
public class EInvoiceTests : AccountingFixture
{
    private static DocumentInput Invoice(Env e, decimal price, string? currency = null, decimal? rate = null, Guid? taxCode = null) => new(
        DocumentKind.SalesInvoice, Oct6, null, e.Customer, currency, rate, null, null, 0,
        [new DocumentLineInput(null, null, e.Id("511"), "item", 1, price, 0, null, taxCode)]);

    private static async Task<Guid> VatAsync(Env e) => (await e.Tax.ListAsync()).Single(c => c.Rate == 15m).Id;

    [Fact]
    public async Task An_issued_invoice_cannot_be_edited_or_deleted_where_the_rules_say_so_but_a_credit_note_reverses_it()
    {
        var e = await NewEnvAsync(countryCode: "SA");
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, 100m, taxCode: await VatAsync(e)));
        Assert.True(invoice.Immutable);
        Assert.NotNull(invoice.IssuedAt);

        var edit = await RefusedAsync(() => e.Trade.IssueAsync(invoice.Id, Invoice(e, 200m)));
        var delete = await RefusedAsync(() => e.Trade.DeleteAsync(invoice.Id));
        Assert.Contains("document.immutable", Codes(edit));
        Assert.Contains("document.immutable", Codes(delete));

        var note = await e.Trade.IssueSavedAsync((await e.Trade.ConvertAsync(invoice.Id, DocumentKind.SalesCreditNote)).Id);
        Assert.Equal(0m, await BalanceAsync(e, "113"));
        Assert.True(note.Immutable);
    }

    [Fact]
    public async Task Drafts_and_documents_that_do_not_post_stay_editable_and_so_does_everything_where_the_rules_do_not_say_so()
    {
        var sa = await NewEnvAsync(countryCode: "SA");
        var draft = await sa.Trade.SaveDraftAsync(null, Invoice(sa, 100m));
        Assert.False(draft.Immutable);
        await sa.Trade.SaveDraftAsync(draft.Id, Invoice(sa, 150m));
        await sa.Trade.DeleteAsync(draft.Id);

        var quote = await sa.Trade.IssueAsync(null, Invoice(sa, 100m) with { Kind = DocumentKind.Quote });
        Assert.False(quote.Immutable);
        await sa.Trade.IssueAsync(quote.Id, Invoice(sa, 120m) with { Kind = DocumentKind.Quote });

        var other = await NewEnvAsync(); // a country without e-invoicing
        var invoice = await other.Trade.IssueAsync(null, Invoice(other, 100m));
        Assert.False(invoice.Immutable);
        await other.Trade.IssueAsync(invoice.Id, Invoice(other, 130m));
        await other.Trade.DeleteAsync(invoice.Id);
    }

    [Fact]
    public async Task An_issued_invoice_prints_a_qr_code_only_in_the_companys_own_currency_and_only_where_the_country_has_one()
    {
        var sa = await NewEnvAsync(countryCode: "SA");
        var vat = await VatAsync(sa);
        var invoice = await sa.Trade.IssueAsync(null, Invoice(sa, 1000m, taxCode: vat));

        await sa.TradePrint.RenderAsync(invoice.Id, PrintLayout.Both);
        Assert.Contains("class=\"qr\"", sa.Renderer.Html!);
        Assert.Contains("data:image/svg+xml;base64,", sa.Renderer.Html!);

        var draft = await sa.Trade.SaveDraftAsync(null, Invoice(sa, 10m));
        await sa.TradePrint.RenderAsync(draft.Id, PrintLayout.English);
        Assert.DoesNotContain("class=\"qr\"", sa.Renderer.Html!);

        var dollars = await sa.Trade.IssueAsync(null, Invoice(sa, 10m, "USD", 0.3m));
        await sa.TradePrint.RenderAsync(dollars.Id, PrintLayout.English);
        Assert.DoesNotContain("class=\"qr\"", sa.Renderer.Html!);

        var other = await NewEnvAsync();
        var plain = await other.Trade.IssueAsync(null, Invoice(other, 10m));
        await other.TradePrint.RenderAsync(plain.Id, PrintLayout.English);
        Assert.DoesNotContain("class=\"qr\"", other.Renderer.Html!);
    }
}
