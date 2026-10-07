using Baba.Domain.Accounting;
using Baba.Domain.Inventory;

namespace Baba.Domain.Tests.Inventory;

/// <summary>Weighted-average costing, worked out by replaying every movement of a product in date order (brief section 10.4).</summary>
public class StockEngineTests
{
    private static readonly Currency Dinar = new("KWD", 3);
    private static readonly Guid Widget = Guid.NewGuid();
    private static readonly Guid Shop = Guid.NewGuid();
    private static readonly Guid Store = Guid.NewGuid();
    private static int _tick;

    private static DateOnly Day(int d) => new(2026, 10, d);

    private static MovementInput Buy(int day, decimal qty, decimal cost, Guid? warehouse = null, Guid? owner = null) =>
        new(Guid.NewGuid(), Widget, warehouse ?? Shop, Day(day), new DateTime(2026, 1, 1).AddSeconds(++_tick), Scaled.ToScaled(qty), CostMode.Given, Scaled.ToScaled(cost), owner ?? Guid.NewGuid());

    private static MovementInput Sell(int day, decimal qty, Guid? warehouse = null, Guid? owner = null) =>
        new(Guid.NewGuid(), Widget, warehouse ?? Shop, Day(day), new DateTime(2026, 1, 1).AddSeconds(++_tick), -Scaled.ToScaled(qty), CostMode.OutAtAverage, 0, owner ?? Guid.NewGuid());

    private static decimal ValueOf(ReplayResult result, MovementInput m) => Scaled.ToDecimal(result.Values[m.Id]);

    [Fact]
    public void A_sale_leaves_at_the_weighted_average_of_what_was_bought()
    {
        var first = Buy(1, 10, 1000m);   // 100 each
        var second = Buy(2, 10, 1200m);  // 120 each
        var sale = Sell(3, 5);

        var result = StockEngine.Replay([first, second, sale], Dinar);

        Assert.Null(result.Shortfall);
        Assert.Equal(-550m, ValueOf(result, sale)); // 5 x 110
    }

    [Fact]
    public void Selling_everything_takes_out_exactly_the_value_so_nothing_is_left_over()
    {
        var buy = Buy(1, 3, 10m); // 3.333... each
        var one = Sell(2, 1);
        var rest = Sell(3, 2);

        var result = StockEngine.Replay([buy, one, rest], Dinar);

        Assert.Equal(-3.333m, ValueOf(result, one));
        Assert.Equal(-6.667m, ValueOf(result, rest)); // not 6.666: the value is used up to the last fils
        Assert.Equal(0m, ValueOf(result, buy) + ValueOf(result, one) + ValueOf(result, rest));
    }

    [Fact]
    public void A_sale_and_the_purchase_that_covers_it_on_the_same_day_work_in_either_order_of_entry()
    {
        var sale = Sell(5, 4);
        var buy = Buy(5, 4, 400m); // entered after the sale, same day

        var result = StockEngine.Replay([sale, buy], Dinar);

        Assert.Null(result.Shortfall);
        Assert.Equal(-400m, ValueOf(result, sale));
    }

    [Fact]
    public void A_purchase_entered_late_changes_what_the_sales_after_it_cost()
    {
        var early = Buy(1, 10, 1000m);   // 100 each
        var sale = Sell(10, 10);
        Assert.Equal(-1000m, ValueOf(StockEngine.Replay([early, sale], Dinar), sale));

        var late = Buy(5, 10, 2000m);    // 200 each, dated before the sale but entered afterwards
        var sale2 = Sell(10, 10);
        var again = StockEngine.Replay([early, late, sale, sale2], Dinar);

        Assert.Equal(-1500m, ValueOf(again, sale)); // average is now 150: 10 x 150
        Assert.Equal(-1500m, ValueOf(again, sale2));
    }

    [Fact]
    public void Selling_more_than_is_there_is_reported_with_where_and_when()
    {
        var buy = Buy(1, 3, 300m);
        var sale = Sell(4, 5);

        var result = StockEngine.Replay([buy, sale], Dinar);

        Assert.NotNull(result.Shortfall);
        Assert.Equal(Day(4), result.Shortfall!.Date);
        Assert.Equal(Widget, result.Shortfall.ProductId);
    }

    [Fact]
    public void A_transfer_moves_stock_between_warehouses_without_changing_its_value_and_each_warehouse_must_hold_what_it_gives()
    {
        var buy = Buy(1, 10, 1000m, Shop);
        var out1 = new MovementInput(Guid.NewGuid(), Widget, Shop, Day(2), new DateTime(2026, 1, 1).AddSeconds(++_tick), -Scaled.ToScaled(4m), CostMode.Transfer);
        var in1 = new MovementInput(Guid.NewGuid(), Widget, Store, Day(2), new DateTime(2026, 1, 1).AddSeconds(_tick), Scaled.ToScaled(4m), CostMode.Transfer);
        var sale = Sell(3, 10m, Shop, null); // 10 sold from a shop that now holds only 6

        var fine = StockEngine.Replay([buy, out1, in1], Dinar);
        Assert.Null(fine.Shortfall);
        Assert.Equal(-400m, ValueOf(fine, out1)); // 4 of the 10 bought for 1,000: out of the shop at the average...
        Assert.Equal(400m, ValueOf(fine, in1));   // ...and into the store at the same, so the total is unchanged

        var short1 = StockEngine.Replay([buy, out1, in1, sale], Dinar);
        Assert.NotNull(short1.Shortfall);
        Assert.Equal(Shop, short1.Shortfall!.WarehouseId);
    }

    [Fact]
    public void Stock_taken_back_comes_in_at_what_it_cost_when_it_was_sold_whatever_the_average_is_now()
    {
        var buy = Buy(1, 10, 1000m);       // 100 each
        var saleOwner = Guid.NewGuid();
        var sale = Sell(2, 4, owner: saleOwner);                  // 400
        var dearBuy = Buy(3, 10, 3000m);   // pushes the average up
        var back = new MovementInput(Guid.NewGuid(), Widget, Shop, Day(4), new DateTime(2026, 1, 1).AddSeconds(++_tick), Scaled.ToScaled(2m), CostMode.InAtSourceCost, 0, Guid.NewGuid(), saleOwner);

        var result = StockEngine.Replay([buy, sale, dearBuy, back], Dinar);

        Assert.Equal(-400m, ValueOf(result, sale));
        Assert.Equal(200m, ValueOf(result, back)); // 2 of the 4 sold, at 100 each
    }

    [Fact]
    public void A_return_to_a_supplier_takes_out_the_value_it_says_and_a_count_that_finds_stock_comes_in_at_the_average()
    {
        var buy = Buy(1, 10, 1000m);
        var giveBack = new MovementInput(Guid.NewGuid(), Widget, Shop, Day(2), new DateTime(2026, 1, 1).AddSeconds(++_tick), -Scaled.ToScaled(2m), CostMode.Given, -Scaled.ToScaled(180m), Guid.NewGuid());
        var found = new MovementInput(Guid.NewGuid(), Widget, Shop, Day(3), new DateTime(2026, 1, 1).AddSeconds(++_tick), Scaled.ToScaled(2m), CostMode.InAtAverage, 0, Guid.NewGuid());

        var result = StockEngine.Replay([buy, giveBack, found], Dinar);

        Assert.Equal(-180m, ValueOf(result, giveBack));
        // 8 left worth 820: average 102.5, so 2 more are worth 205
        Assert.Equal(205m, ValueOf(result, found));
    }

    [Fact]
    public void Replaying_twice_gives_the_same_answer()
    {
        var movements = new[] { Buy(1, 7, 100m), Sell(2, 3), Buy(3, 5, 91.234m), Sell(4, 6), Sell(5, 3) };

        var a = StockEngine.Replay(movements, Dinar);
        var b = StockEngine.Replay(movements.Reverse(), Dinar);

        Assert.Equal(a.Values.OrderBy(v => v.Key), b.Values.OrderBy(v => v.Key));
    }
}
