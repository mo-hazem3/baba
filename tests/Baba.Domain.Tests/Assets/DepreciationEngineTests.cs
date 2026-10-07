using Baba.Domain.Assets;

namespace Baba.Domain.Tests.Assets;

/// <summary>Monthly depreciation, straight line and declining balance (brief section 10.4).</summary>
public class DepreciationEngineTests
{
    private static readonly Currency Dinar = new("KWD", 3);

    private static decimal Total(DepreciationMethod method, decimal cost, decimal salvage, int life, decimal rate, int months)
    {
        decimal accumulated = 0;
        for (var month = 1; month <= months; month++)
            accumulated += DepreciationEngine.Next(method, cost, salvage, life, rate, accumulated, month, Dinar);
        return accumulated;
    }

    [Fact]
    public void Straight_line_is_the_same_every_month_and_adds_up_to_cost_less_salvage()
    {
        Assert.Equal(100m, DepreciationEngine.Next(DepreciationMethod.StraightLine, 1200m, 0m, 12, 0m, 0m, 1, Dinar));
        Assert.Equal(1200m, Total(DepreciationMethod.StraightLine, 1200m, 0m, 12, 0m, 12));
        Assert.Equal(1000m, Total(DepreciationMethod.StraightLine, 1200m, 200m, 12, 0m, 12)); // salvage stays
    }

    [Fact]
    public void The_last_straight_line_month_takes_the_rounding_so_the_total_is_exact()
    {
        Assert.Equal(333.333m, DepreciationEngine.Next(DepreciationMethod.StraightLine, 1000m, 0m, 3, 0m, 0m, 1, Dinar));
        Assert.Equal(333.334m, DepreciationEngine.Next(DepreciationMethod.StraightLine, 1000m, 0m, 3, 0m, 666.666m, 3, Dinar));
        Assert.Equal(1000m, Total(DepreciationMethod.StraightLine, 1000m, 0m, 3, 0m, 3));
    }

    [Fact]
    public void Nothing_is_depreciated_after_the_life_is_over_or_once_the_asset_is_down_to_its_salvage()
    {
        Assert.Equal(0m, DepreciationEngine.Next(DepreciationMethod.StraightLine, 1200m, 0m, 12, 0m, 1200m, 13, Dinar));
        Assert.Equal(0m, DepreciationEngine.Next(DepreciationMethod.DecliningBalance, 1000m, 100m, 0, 30m, 900m, 40, Dinar));
    }

    [Fact]
    public void A_missed_month_after_the_life_catches_up_with_whatever_is_left()
    {
        Assert.Equal(300m, DepreciationEngine.Next(DepreciationMethod.StraightLine, 1200m, 0m, 12, 0m, 900m, 20, Dinar));
    }

    [Fact]
    public void Declining_balance_is_a_yearly_percentage_of_what_is_left_and_shrinks()
    {
        var first = DepreciationEngine.Next(DepreciationMethod.DecliningBalance, 1000m, 0m, 0, 24m, 0m, 1, Dinar);
        var second = DepreciationEngine.Next(DepreciationMethod.DecliningBalance, 1000m, 0m, 0, 24m, first, 2, Dinar);

        Assert.Equal(20m, first);      // 1,000 x 24% / 12
        Assert.Equal(19.6m, second);   // 980 x 2%
    }

    [Fact]
    public void Declining_balance_never_goes_below_the_salvage_value()
    {
        var total = Total(DepreciationMethod.DecliningBalance, 1000m, 900m, 0, 60m, 60);

        Assert.True(total <= 100m);
        Assert.True(total > 99m);
    }

    [Fact]
    public void The_month_number_counts_the_month_of_acquisition_as_the_first()
    {
        var acquired = new DateOnly(2026, 3, 20);

        Assert.Equal(1, DepreciationEngine.MonthNumber(acquired, new DateOnly(2026, 3, 1)));
        Assert.Equal(2, DepreciationEngine.MonthNumber(acquired, new DateOnly(2026, 4, 1)));
        Assert.Equal(11, DepreciationEngine.MonthNumber(acquired, new DateOnly(2027, 1, 1)));
    }
}
