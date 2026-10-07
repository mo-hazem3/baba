using Baba.Application.Trade;
using Baba.Domain.Accounting;
using Baba.Domain.Trade;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Invoices and notes move stock and book its cost (brief section 10.4): purchases at their amount, sales at the average cost.</summary>
public class StockInvoiceTests : AccountingFixture
{
    private static DateOnly Sep20 => new(2026, 9, 20);
    private static DateOnly Oct5 => new(2026, 10, 5);
    private static DateOnly Oct10 => new(2026, 10, 10);

    private static async Task<(Env Env, ProductDto Product)> EnvAsync()
    {
        var e = await new StockInvoiceTests().NewEnvAsync();
        var product = await e.Products.CreateAsync(new ProductInput("W1", "قطعة", "Widget", "pcs", 50m, 20m, e.Id("511"), null, IsStockItem: true));
        return (e, product);
    }

    private static DocumentInput Buy(Env e, ProductDto p, decimal qty, decimal price, DateOnly date, string? currency = null, decimal? rate = null) => new(
        DocumentKind.PurchaseInvoice, date, null, e.Supplier, currency, rate, null, null, 0, [new DocumentLineInput(null, p.Id, null, null, qty, price)]);

    private static DocumentInput Sell(Env e, ProductDto p, decimal qty, decimal price, DateOnly date) => new(
        DocumentKind.SalesInvoice, date, null, e.Customer, null, null, null, null, 0, [new DocumentLineInput(null, p.Id, e.Id("511"), null, qty, price)]);

    private static async Task<(decimal Quantity, decimal Value)> OnHandAsync(Env e)
    {
        var levels = await e.Stock.LevelsAsync();
        return (levels.Sum(l => l.Quantity), levels.Sum(l => l.Value));
    }

    [Fact]
    public async Task A_purchase_brings_stock_in_at_its_amount_and_is_posted_to_the_stock_account_whatever_was_chosen()
    {
        var (e, product) = await EnvAsync();

        var invoice = await e.Trade.IssueAsync(null, Buy(e, product, 10m, 20m, Oct1) with
        {
            Lines = [new DocumentLineInput(null, product.Id, e.Id("422"), null, 10m, 20m)], // an expense account was picked: it is ignored
        });

        Assert.Equal(e.Id("114"), invoice.Lines.Single().AccountId);
        Assert.Equal(200m, await BalanceAsync(e, "114"));
        Assert.Equal(0m, await BalanceAsync(e, "422"));
        Assert.Equal(-200m, await BalanceAsync(e, "211"));
        Assert.Equal((10m, 200m), await OnHandAsync(e));
        Assert.NotNull(invoice.WarehouseId); // the default warehouse
    }

    [Fact]
    public async Task A_sale_takes_stock_out_at_the_average_cost_and_books_the_cost_of_sales()
    {
        var (e, product) = await EnvAsync();
        await e.Trade.IssueAsync(null, Buy(e, product, 10m, 20m, Oct1));

        await e.Trade.IssueAsync(null, Sell(e, product, 4m, 50m, Oct6));

        Assert.Equal(200m, await BalanceAsync(e, "113"));
        Assert.Equal(-200m, await BalanceAsync(e, "511"));
        Assert.Equal(80m, await BalanceAsync(e, "411"));   // what the 4 cost
        Assert.Equal(120m, await BalanceAsync(e, "114"));  // what the other 6 are worth
        Assert.Equal((6m, 120m), await OnHandAsync(e));
    }

    [Fact]
    public async Task Editing_a_sale_changes_its_cost_and_deleting_it_gives_the_stock_back()
    {
        var (e, product) = await EnvAsync();
        await e.Trade.IssueAsync(null, Buy(e, product, 10m, 20m, Oct1));
        var sale = await e.Trade.IssueAsync(null, Sell(e, product, 4m, 50m, Oct6));

        await e.Trade.IssueAsync(sale.Id, Sell(e, product, 5m, 50m, Oct6));
        Assert.Equal(100m, await BalanceAsync(e, "411"));
        Assert.Equal((5m, 100m), await OnHandAsync(e));

        await e.Trade.DeleteAsync(sale.Id);
        Assert.Equal(0m, await BalanceAsync(e, "411"));
        Assert.Equal(200m, await BalanceAsync(e, "114"));
        Assert.Equal((10m, 200m), await OnHandAsync(e));
    }

    [Fact]
    public async Task Selling_what_is_not_there_is_refused_and_nothing_is_posted()
    {
        var (e, product) = await EnvAsync();
        await e.Trade.IssueAsync(null, Buy(e, product, 3m, 20m, Oct1));

        var refused = await RefusedAsync(() => e.Trade.IssueAsync(null, Sell(e, product, 5m, 50m, Oct6)));

        Assert.Contains("stock.insufficient", Codes(refused));
        Assert.Contains(refused.Issues, i => i.Field == "lines[0].quantity");
        Assert.Equal(0m, await BalanceAsync(e, "113"));
        Assert.Equal((3m, 60m), await OnHandAsync(e));
    }

    [Fact]
    public async Task A_purchase_entered_late_changes_what_the_later_sale_cost_and_its_voucher()
    {
        var (e, product) = await EnvAsync();
        await e.Trade.IssueAsync(null, Buy(e, product, 10m, 20m, Oct1));       // 20 each
        await e.Trade.IssueAsync(null, Sell(e, product, 5m, 50m, Oct10));      // costs 100
        Assert.Equal(100m, await BalanceAsync(e, "411"));

        await e.Trade.IssueAsync(null, Buy(e, product, 10m, 40m, Oct5));       // entered later, dated before the sale: 40 each

        Assert.Equal(150m, await BalanceAsync(e, "411"));                      // average at the sale is now 30: 5 x 30
        Assert.Equal(450m, await BalanceAsync(e, "114"));                      // 600 bought, 150 gone
        Assert.Equal((15m, 450m), await OnHandAsync(e));
        Assert.Equal(await BalanceAsync(e, "114"), (await OnHandAsync(e)).Value); // the books and the stock agree
    }

    [Fact]
    public async Task A_purchase_that_later_sales_depend_on_cannot_be_deleted_or_cut()
    {
        var (e, product) = await EnvAsync();
        var buy = await e.Trade.IssueAsync(null, Buy(e, product, 10m, 20m, Oct1));
        await e.Trade.IssueAsync(null, Sell(e, product, 8m, 50m, Oct6));

        var delete = await RefusedAsync(() => e.Trade.DeleteAsync(buy.Id));
        var cut = await RefusedAsync(() => e.Trade.IssueAsync(buy.Id, Buy(e, product, 5m, 20m, Oct1)));

        Assert.Contains("stock.would-leave-later-sales-short", Codes(delete));
        Assert.Contains("stock.would-leave-later-sales-short", Codes(cut));
        Assert.Equal((2m, 40m), await OnHandAsync(e));
    }

    [Fact]
    public async Task A_credit_note_brings_the_stock_back_at_what_it_cost_and_reverses_the_cost_of_sales()
    {
        var (e, product) = await EnvAsync();
        await e.Trade.IssueAsync(null, Buy(e, product, 10m, 20m, Oct1));
        var sale = await e.Trade.IssueAsync(null, Sell(e, product, 4m, 50m, Oct6));
        await e.Trade.IssueAsync(null, Buy(e, product, 10m, 60m, Oct10));   // the average moves up afterwards

        var note = await e.Trade.ConvertAsync(sale.Id, DocumentKind.SalesCreditNote);
        await e.Trade.IssueSavedAsync(note.Id);

        Assert.Equal(0m, await BalanceAsync(e, "411"));      // the 80 of cost is taken back whatever the average is now
        Assert.Equal(0m, await BalanceAsync(e, "113"));
        Assert.Equal((20m, 800m), await OnHandAsync(e));     // 200 + 600
        Assert.Equal(800m, await BalanceAsync(e, "114"));
    }

    [Fact]
    public async Task A_debit_note_sends_stock_back_to_the_supplier_at_the_amount_of_the_note()
    {
        var (e, product) = await EnvAsync();
        var buy = await e.Trade.IssueAsync(null, Buy(e, product, 10m, 20m, Oct1));

        var note = await e.Trade.ConvertAsync(buy.Id, DocumentKind.PurchaseDebitNote);
        await e.Trade.SaveDraftAsync(note.Id, new DocumentInput(
            DocumentKind.PurchaseDebitNote, Oct6, null, e.Supplier, null, null, null, null, 0, [new DocumentLineInput(null, product.Id, null, null, 3m, 20m)]));
        await e.Trade.IssueSavedAsync(note.Id);

        Assert.Equal((7m, 140m), await OnHandAsync(e));
        Assert.Equal(140m, await BalanceAsync(e, "114"));
        Assert.Equal(-140m, await BalanceAsync(e, "211"));
    }

    [Fact]
    public async Task A_purchase_in_dollars_is_stocked_at_its_value_in_the_companys_currency()
    {
        var (e, product) = await EnvAsync();

        await e.Trade.IssueAsync(null, Buy(e, product, 10m, 10m, Oct1, "USD", 0.3m)); // 100 dollars = 30 dinars

        Assert.Equal((10m, 30m), await OnHandAsync(e));
        Assert.Equal(30m, await BalanceAsync(e, "114"));
    }

    [Fact]
    public async Task A_service_never_moves_stock_and_a_locked_month_refuses_a_change_that_would_alter_its_cost()
    {
        var (e, product) = await EnvAsync();
        var service = await e.Products.CreateAsync(new ProductInput("S1", "", "Consulting", "hr", 10m, 0m, e.Id("512"), null));
        await e.Trade.IssueAsync(null, new DocumentInput(DocumentKind.SalesInvoice, Oct6, null, e.Customer, null, null, null, null, 0, [new DocumentLineInput(null, service.Id, e.Id("512"), null, 2m, 10m)]));
        Assert.Empty(await e.Stock.LevelsAsync());

        await e.Trade.IssueAsync(null, Buy(e, product, 10m, 20m, Oct1));
        await e.Trade.IssueAsync(null, Sell(e, product, 5m, 50m, Oct10));
        await e.Periods.SetLockedAsync(new DateOnly(2026, 10, 1), true);

        // A purchase dated in September (open) would change the cost of the October sale (locked).
        var refused = await RefusedAsync(() => e.Trade.IssueAsync(null, Buy(e, product, 10m, 40m, Sep20)));

        Assert.Contains("stock.locked-period", Codes(refused));
        Assert.Equal((5m, 100m), await OnHandAsync(e));
    }
}
