using System.Text;
using Baba.Application.Importing;
using Baba.Application.Trade;
using Baba.Infrastructure.Accounting;
using Baba.Infrastructure.Printing;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Excel and CSV import of stock items and of opening stock; export of the stock columns of the product list (brief section 11).</summary>
public class StockImportTests : AccountingFixture
{
    private static ListImportService Importer(Env e) => new(
        new TabularReader(), new AccountStore(e.Files), new PartyStore(e.Files), new CostCenterStore(e.Files), e.Tax,
        e.Products, e.Rates, e.Vouchers, e.StockDocs, e.Warehouses, e.FixedAssets, e.Employees, e.Files);

    private static byte[] Csv(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task Products_import_with_their_stock_columns_and_are_listed_with_them()
    {
        var e = await NewEnvAsync();

        var result = await Importer(e).ImportProductsAsync("p.csv", Csv("Code,Name,Sale price,Stock item,Barcode,Reorder level\nW1,Widget,10,yes,6281000000011,5\nS1,Consulting,90,no,,\n"));

        Assert.Empty(result.Issues);
        var widget = (await e.Products.ListAsync()).Single(p => p.Code == "W1");
        Assert.True(widget.IsStockItem);
        Assert.Equal("6281000000011", widget.Barcode);
        Assert.Equal(5m, widget.ReorderLevel);
        Assert.False((await e.Products.ListAsync()).Single(p => p.Code == "S1").IsStockItem);

        var listing = await e.Listings.ProductsAsync();
        Assert.Contains(listing.Columns, c => c.Key == "barcode");
        var row = listing.Rows.Single(r => r.Cells[0].Text == "W1");
        Assert.Equal("6281000000011", row.Cells[8].Text);
        Assert.Equal(5m, row.Cells[9].Amount);
    }

    [Fact]
    public async Task Opening_stock_imports_as_one_document_by_code_or_barcode_and_posts_to_the_books()
    {
        var e = await NewEnvAsync();
        await e.Products.CreateAsync(new ProductInput("W1", "", "Widget", "pcs", 50m, 20m, e.Id("511"), null, IsStockItem: true, Barcode: "111"));
        await e.Products.CreateAsync(new ProductInput("W2", "", "Gadget", "pcs", 50m, 20m, e.Id("511"), null, IsStockItem: true));

        var result = await Importer(e).ImportOpeningStockAsync("stock.csv", Csv("Product code,Barcode,Warehouse code,Quantity,Unit cost\nW1,,MAIN,10,25\n,111,,5,30\nW2,,,4,\n"));

        Assert.Empty(result.Issues);
        Assert.Equal(3, result.Imported);
        Assert.Single(await e.StockDocs.ListAsync(null)); // one document for the whole file
        var levels = await e.Stock.LevelsAsync();
        Assert.Equal(19m, levels.Sum(l => l.Quantity));
        Assert.Equal(400m, levels.Sum(l => l.Value)); // 10 x 25 + 5 x 30 (a line with no cost comes in free)
        Assert.Equal(400m, await BalanceAsync(e, "114"));
    }

    [Fact]
    public async Task One_wrong_opening_stock_row_imports_nothing_and_every_wrong_row_is_named()
    {
        var e = await NewEnvAsync();
        await e.Products.CreateAsync(new ProductInput("W1", "", "Widget", "pcs", 50m, 20m, e.Id("511"), null, IsStockItem: true));
        await e.Products.CreateAsync(new ProductInput("S1", "", "Consulting", "hr", 50m, 0m, e.Id("512"), null));

        var result = await Importer(e).ImportOpeningStockAsync("stock.csv", Csv("Product code,Warehouse code,Quantity,Unit cost\nW1,MAIN,10,25\nNOPE,MAIN,1,1\nS1,MAIN,1,1\nW1,ELSEWHERE,1,1\nW1,MAIN,0,1\nW1,MAIN,2,abc\n"));

        Assert.Equal(0, result.Imported);
        Assert.Contains(new ImportIssue(3, "opening-stock.product-unknown"), result.Issues);
        Assert.Contains(new ImportIssue(4, "opening-stock.product-not-stock"), result.Issues);
        Assert.Contains(new ImportIssue(5, "opening-stock.warehouse-unknown"), result.Issues);
        Assert.Contains(new ImportIssue(6, "opening-stock.quantity-invalid"), result.Issues);
        Assert.Contains(new ImportIssue(7, "opening-stock.cost-invalid"), result.Issues);
        Assert.Empty(await e.Stock.LevelsAsync());
        Assert.Empty(await e.StockDocs.ListAsync(null));
    }

    [Fact]
    public async Task An_opening_stock_file_without_a_product_column_is_refused()
    {
        var e = await NewEnvAsync();

        var result = await Importer(e).ImportOpeningStockAsync("stock.csv", Csv("Quantity,Unit cost\n1,1\n"));

        Assert.Equal([new ImportIssue(0, "opening-stock.product-column-missing")], result.Issues);
    }
}
