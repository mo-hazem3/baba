namespace Baba.Domain.Tests;

public class MoneyTests
{
    private static readonly Currency TwoDecimals = new("TWO", 2);
    private static readonly Currency ThreeDecimals = new("TRI", 3);

    [Theory]
    [InlineData(10.005, 10.01)]
    [InlineData(10.004, 10.00)]
    [InlineData(-10.005, -10.01)]
    [InlineData(0.995, 1.00)]
    public void Rounds_half_away_from_zero_to_two_decimals(double amount, double expected)
    {
        var money = Money.Of((decimal)amount, TwoDecimals);

        Assert.Equal((decimal)expected, money.Amount);
    }

    [Theory]
    [InlineData(1.2345, 1.235)]
    [InlineData(1.2344, 1.234)]
    [InlineData(-1.2345, -1.235)]
    public void Rounds_to_three_decimals_for_three_decimal_currencies(double amount, double expected)
    {
        var money = Money.Of((decimal)amount, ThreeDecimals);

        Assert.Equal((decimal)expected, money.Amount);
    }

    [Fact]
    public void Adding_keeps_the_currency_and_rounds()
    {
        var total = Money.Of(0.10m, TwoDecimals) + Money.Of(0.20m, TwoDecimals);

        Assert.Equal(0.30m, total.Amount);
        Assert.Equal(TwoDecimals, total.Currency);
    }

    [Fact]
    public void Subtracting_can_go_negative()
    {
        var result = Money.Of(1m, TwoDecimals) - Money.Of(3m, TwoDecimals);

        Assert.Equal(-2m, result.Amount);
    }

    [Fact]
    public void Mixing_currencies_without_an_exchange_rate_is_refused()
    {
        var two = Money.Of(1m, TwoDecimals);
        var three = Money.Of(1m, ThreeDecimals);

        Assert.Throws<InvalidOperationException>(() => two + three);
    }

    [Fact]
    public void Negate_flips_the_sign_and_zero_is_zero()
    {
        Assert.Equal(-5m, Money.Of(5m, TwoDecimals).Negate().Amount);
        Assert.True(Money.Zero(TwoDecimals).IsZero);
    }

    [Fact]
    public void Equal_amounts_in_the_same_currency_are_equal()
    {
        Assert.Equal(Money.Of(1.5m, TwoDecimals), Money.Of(1.50m, TwoDecimals));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("ABCD")]
    public void Currency_code_must_be_three_letters(string code)
    {
        Assert.Throws<ArgumentException>(() => new Currency(code, 2));
    }
}
