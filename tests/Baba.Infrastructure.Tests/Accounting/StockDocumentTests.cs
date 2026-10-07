using Baba.Application.Inventory;
using Baba.Application.Trade;
using Baba.Domain.Inventory;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Opening stock, adjustments and transfers: movements, weighted-average value, ledger entries made from them, warehouses (brief section 10.4).</summary>
public class StockDocumentTests : AccountingFixture
{
    private static async Task<(Env Env, ProductDto Product, Guid Main)> StockEnvAsync()
    {
        var test = new StockDocumentTests();
        var e = await test.NewEnvAsync();
        var product = await e.Products.CreateAsync(new ProductInput("W1", "قطعة", "Widget", "pcs", 50m, 20m, e.Id("511"), null, IsStockItem: true));
        var main = (await e.Warehouses.ListAsync()).Single().Id;
        return (e, product, main);
    }

    private static StockDocumentInput Opening(Guid product, Guid warehouse, decimal qty, decimal? cost, DateOnly? date = null) =>
        new(StockDocumentKind.Opening, date ?? Oct1, null, null, [new StockLineInput(product, warehouse, null, qty, cost)]);

    private static StockDocumentInput Adjust(Guid product, Guid warehouse, decimal qty, decimal? cost = null, DateOnly? date = null) =>
        new(StockDocumentKind.Adjustment, date ?? Oct6, null, null, [new StockLineInput(product, warehouse, null, qty, cost)]);

    // ------------------------------------------------------------------ Opening and adjustments

    [Fact]
    public async Task Opening_stock_is_posted_at_its_cost_and_shows_on_hand_with_its_value()
    {
        var (e, product, main) = await StockEnvAsync();

        var opening = await e.StockDocs.SaveAsync(null, Opening(product.Id, main, 10m, 25m));

        Assert.Equal("OS-2026-0001", opening.Number);
        Assert.Equal(250m, opening.Value);
        Assert.Equal(250m, await BalanceAsync(e, "114"));     // the stock account
        Assert.Equal(-250m, await BalanceAsync(e, "429"));    // the other side: stock gains and losses
        var level = (await e.Stock.LevelsAsync()).Single();
        Assert.Equal((10m, 250m), (level.Quantity, level.Value));
        var voucher = await e.Vouchers.GetAsync(opening.VoucherId!.Value);
        Assert.StartsWith("SK-2026-", voucher!.Number);
    }

    [Fact]
    public async Task A_loss_takes_stock_out_at_the_average_cost_and_a_find_without_a_cost_comes_in_at_it()
    {
        var (e, product, main) = await StockEnvAsync();
        await e.StockDocs.SaveAsync(null, Opening(product.Id, main, 10m, 25m));

        var loss = await e.StockDocs.SaveAsync(null, Adjust(product.Id, main, -3m));
        Assert.Equal(-75m, loss.Value);
        Assert.Equal(175m, await BalanceAsync(e, "114"));
        Assert.Equal(-175m, await BalanceAsync(e, "429")); // the loss is a debit of 75 against the 250 credited

        var found = await e.StockDocs.SaveAsync(null, Adjust(product.Id, main, 2m));
        Assert.Equal(50m, found.Value); // 2 x 25
        Assert.Equal(225m, await BalanceAsync(e, "114"));
        Assert.Equal(9m, (await e.Stock.LevelsAsync()).Single().Quantity);
    }

    [Fact]
    public async Task Changing_the_opening_cost_works_out_the_later_adjustment_and_its_voucher_again()
    {
        var (e, product, main) = await StockEnvAsync();
        var opening = await e.StockDocs.SaveAsync(null, Opening(product.Id, main, 10m, 20m));
        var loss = await e.StockDocs.SaveAsync(null, Adjust(product.Id, main, -5m));
        Assert.Equal(-100m, loss.Value);
        Assert.Equal(100m, await BalanceAsync(e, "114"));

        await e.StockDocs.SaveAsync(opening.Id, Opening(product.Id, main, 10m, 30m));

        Assert.Equal(-150m, (await e.StockDocs.GetAsync(loss.Id))!.Value); // 5 x 30 now
        Assert.Equal(150m, await BalanceAsync(e, "114"));                  // 300 in, 150 out
        Assert.Equal(150m, (await e.Stock.LevelsAsync()).Single().Value);
    }

    [Fact]
    public async Task Stock_cannot_go_below_zero_and_what_leaves_later_stock_short_is_refused()
    {
        var (e, product, main) = await StockEnvAsync();
        var opening = await e.StockDocs.SaveAsync(null, Opening(product.Id, main, 10m, 20m));

        var tooMuch = await RefusedAsync(() => e.StockDocs.SaveAsync(null, Adjust(product.Id, main, -11m)));
        Assert.Contains("stock.insufficient", Codes(tooMuch));

        await e.StockDocs.SaveAsync(null, Adjust(product.Id, main, -8m));
        var deleteOpening = await RefusedAsync(() => e.StockDocs.DeleteAsync(opening.Id));
        Assert.Contains("stock.would-leave-later-sales-short", Codes(deleteOpening));
        var shrink = await RefusedAsync(() => e.StockDocs.SaveAsync(opening.Id, Opening(product.Id, main, 5m, 20m)));
        Assert.Contains("stock.would-leave-later-sales-short", Codes(shrink));
        Assert.Equal(2m, (await e.Stock.LevelsAsync()).Single().Quantity); // nothing changed
    }

    [Fact]
    public async Task Deleting_a_stock_document_removes_its_movements_and_its_voucher()
    {
        var (e, product, main) = await StockEnvAsync();
        var opening = await e.StockDocs.SaveAsync(null, Opening(product.Id, main, 10m, 20m));

        await e.StockDocs.DeleteAsync(opening.Id);

        Assert.Empty(await e.Stock.LevelsAsync());
        Assert.Equal(0m, await BalanceAsync(e, "114"));
        Assert.Null(await e.Vouchers.GetAsync(opening.VoucherId!.Value));
        Assert.Null(await e.StockDocs.GetAsync(opening.Id));
    }

    [Fact]
    public async Task A_locked_month_refuses_stock_changes_that_would_change_its_entries()
    {
        var (e, product, main) = await StockEnvAsync();
        await e.StockDocs.SaveAsync(null, Opening(product.Id, main, 10m, 20m));
        await e.Periods.SetLockedAsync(Oct1, true);

        var refused = await RefusedAsync(() => e.StockDocs.SaveAsync(null, Adjust(product.Id, main, -1m, date: Oct6)));

        Assert.Contains("stock.locked-period", Codes(refused));
        Assert.Equal(200m, await BalanceAsync(e, "114"));
    }

    // ------------------------------------------------------------------ Transfers and warehouses

    [Fact]
    public async Task A_transfer_moves_stock_between_warehouses_without_touching_the_books()
    {
        var (e, product, main) = await StockEnvAsync();
        var shop = await e.Warehouses.CreateAsync(new WarehouseInput("SHOP", "المعرض", "Shop"));
        await e.StockDocs.SaveAsync(null, Opening(product.Id, main, 10m, 20m));

        var transfer = await e.StockDocs.SaveAsync(null, new StockDocumentInput(StockDocumentKind.Transfer, Oct6, null, null, [new StockLineInput(product.Id, main, shop.Id, 4m, null)]));

        Assert.StartsWith("TR-2026-", transfer.Number);
        Assert.Null(transfer.VoucherId);
        Assert.Equal(200m, await BalanceAsync(e, "114"));
        var levels = await e.Stock.LevelsAsync();
        Assert.Equal(6m, levels.Single(l => l.WarehouseId == main).Quantity);
        Assert.Equal(4m, levels.Single(l => l.WarehouseId == shop.Id).Quantity);
        Assert.Equal(80m, levels.Single(l => l.WarehouseId == shop.Id).Value + 0m); // 4 x 20 follows the stock

        var tooMuch = await RefusedAsync(() => e.StockDocs.SaveAsync(null, new StockDocumentInput(StockDocumentKind.Transfer, Oct6, null, null, [new StockLineInput(product.Id, shop.Id, main, 5m, null)])));
        Assert.Contains("stock.insufficient", Codes(tooMuch));
    }

    [Fact]
    public async Task Stock_documents_are_checked_line_by_line()
    {
        var (e, product, main) = await StockEnvAsync();
        var service = await e.Products.CreateAsync(new ProductInput("S1", "", "Consulting", "hr", 10m, 0m, null, null));

        var notStock = await RefusedAsync(() => e.StockDocs.SaveAsync(null, Opening(service.Id, main, 1m, 5m)));
        var zero = await RefusedAsync(() => e.StockDocs.SaveAsync(null, Adjust(product.Id, main, 0m)));
        var noWarehouse = await RefusedAsync(() => e.StockDocs.SaveAsync(null, Opening(product.Id, Guid.NewGuid(), 1m, 5m)));
        var sameWarehouse = await RefusedAsync(() => e.StockDocs.SaveAsync(null, new StockDocumentInput(StockDocumentKind.Transfer, Oct6, null, null, [new StockLineInput(product.Id, main, main, 1m, null)])));
        var negativeCost = await RefusedAsync(() => e.StockDocs.SaveAsync(null, Opening(product.Id, main, 1m, -5m)));
        var empty = await RefusedAsync(() => e.StockDocs.SaveAsync(null, Opening(product.Id, main, 1m, 5m) with { Lines = [] }));

        Assert.Contains("stockdoc.product-not-stock", Codes(notStock));
        Assert.Contains("stockdoc.quantity-zero", Codes(zero));
        Assert.Contains("stockdoc.warehouse-invalid", Codes(noWarehouse));
        Assert.Contains("stockdoc.same-warehouse", Codes(sameWarehouse));
        Assert.Contains("stockdoc.cost-negative", Codes(negativeCost));
        Assert.Contains("lines.required", Codes(empty));
    }

    [Fact]
    public async Task Warehouses_have_a_default_unique_codes_and_cannot_be_deleted_while_used_or_the_last()
    {
        var (e, product, main) = await StockEnvAsync();
        var first = (await e.Warehouses.ListAsync()).Single();
        Assert.True(first.IsDefault);
        Assert.Equal("MAIN", first.Code);

        var duplicate = await RefusedAsync(() => e.Warehouses.CreateAsync(new WarehouseInput("main", "", "Again")));
        Assert.Contains("warehouse.code-duplicate", Codes(duplicate));

        var shop = await e.Warehouses.CreateAsync(new WarehouseInput("SHOP", "", "Shop"));
        await e.Warehouses.SetDefaultAsync(shop.Id);
        Assert.Equal(["SHOP"], (await e.Warehouses.ListAsync()).Where(w => w.IsDefault).Select(w => w.Code));

        await e.StockDocs.SaveAsync(null, Opening(product.Id, main, 1m, 5m));
        var used = await RefusedAsync(() => e.Warehouses.DeleteAsync(main));
        Assert.Contains("warehouse.in-use", Codes(used));

        await e.Warehouses.DeleteAsync((await e.Warehouses.CreateAsync(new WarehouseInput("TMP", "", "Temporary"))).Id);
        await e.Warehouses.SetActiveAsync(shop.Id, false);
        var last = await RefusedAsync(() => e.Warehouses.SetActiveAsync(main, false));
        Assert.Contains("warehouse.last-one", Codes(last));
        Assert.True((await e.Warehouses.ListAsync()).Single(w => w.Id == main).IsDefault); // the default moved when the default was switched off
    }

    // ------------------------------------------------------------------ Products

    [Fact]
    public async Task A_product_with_stock_keeps_being_a_stock_item_cannot_be_deleted_and_barcodes_are_unique()
    {
        var (e, product, main) = await StockEnvAsync();
        await e.StockDocs.SaveAsync(null, Opening(product.Id, main, 3m, 5m));

        var asService = await RefusedAsync(() => e.Products.UpdateAsync(product.Id, new ProductInput("W1", "", "Widget", "pcs", 50m, 20m, null, null, IsStockItem: false)));
        var delete = await RefusedAsync(() => e.Products.DeleteAsync(product.Id));
        Assert.Contains("product.has-stock", Codes(asService));
        Assert.Contains("product.in-use", Codes(delete));

        await e.Products.UpdateAsync(product.Id, new ProductInput("W1", "", "Widget", "pcs", 50m, 20m, null, null, IsStockItem: true, Barcode: "6291041500213", ReorderLevel: 5m));
        var other = await RefusedAsync(() => e.Products.CreateAsync(new ProductInput("W2", "", "Other", null, 1m, 1m, null, null, Barcode: "6291041500213")));
        var badLevel = await RefusedAsync(() => e.Products.CreateAsync(new ProductInput("W3", "", "Level", null, 1m, 1m, null, null, ReorderLevel: -1m)));
        Assert.Contains("product.barcode-duplicate", Codes(other));
        Assert.Contains("product.reorder-negative", Codes(badLevel));
        Assert.Equal(5m, (await e.Products.ListAsync()).Single(p => p.Code == "W1").ReorderLevel);
    }
}
