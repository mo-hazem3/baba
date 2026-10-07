using Baba.Application.Inventory;
using Baba.Application.Trade;
using Baba.Domain.Inventory;
using Baba.Domain.Trade;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>The stock reports: valuation that agrees with the ledger, movements, and the reorder list (brief sections 10.4 and 11).</summary>
public class StockReportTests : AccountingFixture
{
    private static DateOnly Oct5 => new(2026, 10, 5);
    private static DateOnly Oct10 => new(2026, 10, 10);

    private static async Task<(Env Env, ProductDto Product)> EnvAsync(decimal reorderLevel = 0m)
    {
        var e = await new StockReportTests().NewEnvAsync();
        var product = await e.Products.CreateAsync(new ProductInput("W1", "قطعة", "Widget", "pcs", 50m, 20m, e.Id("511"), null, IsStockItem: true, ReorderLevel: reorderLevel));
        return (e, product);
    }

    private static DocumentInput Buy(Env e, ProductDto p, decimal qty, decimal price, DateOnly date) => new(
        DocumentKind.PurchaseInvoice, date, null, e.Supplier, null, null, null, null, 0, [new DocumentLineInput(null, p.Id, null, null, qty, price)]);

    private static DocumentInput Sell(Env e, ProductDto p, decimal qty, DateOnly date) => new(
        DocumentKind.SalesInvoice, date, null, e.Customer, null, null, null, null, 0, [new DocumentLineInput(null, p.Id, e.Id("511"), null, qty, 50m)]);

    [Fact]
    public async Task The_valuation_lists_each_product_with_its_average_cost_and_agrees_with_the_ledger()
    {
        var (e, product) = await EnvAsync();
        await e.Trade.IssueAsync(null, Buy(e, product, 10m, 20m, Oct1));
        await e.Trade.IssueAsync(null, Buy(e, product, 10m, 30m, Oct5));
        await e.Trade.IssueAsync(null, Sell(e, product, 5m, Oct6));

        var report = await e.Listings.StockValuationAsync(Oct10, null);

        var row = report.Rows.First();
        Assert.Equal("W1", row.Cells[0].Text);
        Assert.Equal(15m, row.Cells[3].Amount);     // 20 bought, 5 sold
        Assert.Equal(25m, row.Cells[4].Amount);     // average of 20 and 30
        Assert.Equal(375m, row.Cells[5].Amount);
        Assert.Equal(375m, report.Rows.Last().Cells[5].Amount);
        Assert.Equal(375m, await BalanceAsync(e, "114"));
        Assert.All(report.Checks, c => Assert.True(c.Passed));
        Assert.Single(report.Checks);
    }

    [Fact]
    public async Task The_valuation_as_of_an_earlier_date_leaves_out_what_came_later_and_one_warehouse_has_no_ledger_check()
    {
        var (e, product) = await EnvAsync();
        await e.Trade.IssueAsync(null, Buy(e, product, 10m, 20m, Oct1));
        await e.Trade.IssueAsync(null, Sell(e, product, 4m, Oct6));

        var before = await e.Listings.StockValuationAsync(Oct5, null);
        var main = (await e.Warehouses.ListAsync()).Single().Id;
        var one = await e.Listings.StockValuationAsync(Oct10, main);

        Assert.Equal(200m, before.Rows.Last().Cells[5].Amount);
        Assert.Equal(120m, one.Rows.Last().Cells[5].Amount);
        Assert.Empty(one.Checks);
    }

    [Fact]
    public async Task Movements_show_what_came_in_and_went_out_with_the_document_number()
    {
        var (e, product) = await EnvAsync();
        var buy = await e.Trade.IssueAsync(null, Buy(e, product, 10m, 20m, Oct1));
        await e.Trade.IssueAsync(null, Sell(e, product, 4m, Oct6));

        var report = await e.Listings.StockMovementsAsync(null, null, product.Id, null);

        Assert.Equal(2, report.Rows.Count);
        Assert.Equal(buy.Number, report.Rows[0].Cells[1].Text);
        Assert.Equal(10m, report.Rows[0].Cells[6].Amount);
        Assert.Equal(200m, report.Rows[0].Cells[8].Amount);
        Assert.Equal(4m, report.Rows[1].Cells[7].Amount);
        Assert.Equal(-80m, report.Rows[1].Cells[8].Amount);
        Assert.Single((await e.Listings.StockMovementsAsync(Oct5, Oct10, null, null)).Rows);
    }

    [Fact]
    public async Task A_product_at_or_below_its_reorder_level_is_listed_and_one_above_it_is_not()
    {
        var (e, product) = await EnvAsync(reorderLevel: 5m);
        await e.Trade.IssueAsync(null, Buy(e, product, 10m, 20m, Oct1));
        Assert.Empty((await e.Listings.StockReorderAsync()).Rows);

        await e.Trade.IssueAsync(null, Sell(e, product, 5m, Oct6));

        var row = Assert.Single((await e.Listings.StockReorderAsync()).Rows);
        Assert.Equal(5m, row.Cells[3].Amount);
        Assert.Equal(5m, row.Cells[4].Amount);
        Assert.Equal(0m, row.Cells[5].Amount);
        Assert.Single(await e.Stock.LowStockAsync());
    }

    [Fact]
    public async Task Warehouses_and_stock_documents_can_be_listed_for_export()
    {
        var (e, product) = await EnvAsync();
        var main = (await e.Warehouses.ListAsync()).Single().Id;
        await e.StockDocs.SaveAsync(null, new StockDocumentInput(StockDocumentKind.Opening, Oct1, null, null, [new StockLineInput(product.Id, main, null, 10m, 25m)]));

        var warehouses = await e.Listings.WarehousesAsync();
        var documents = await e.Listings.StockDocumentsAsync(null, null, null);

        Assert.Single(warehouses.Rows);
        var row = Assert.Single(documents.Rows);
        Assert.Equal("OS-2026-0001", row.Cells[0].Text);
        Assert.Equal(250m, row.Cells[5].Amount);
        Assert.Empty((await e.Listings.StockDocumentsAsync(StockDocumentKind.Transfer, null, null)).Rows);
    }
}
