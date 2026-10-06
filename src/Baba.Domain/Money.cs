namespace Baba.Domain;

/// <summary>
/// An amount in one currency, always rounded to that currency's minor units.
/// Uses <see cref="decimal"/> only; never double/float.
/// </summary>
public sealed record Money
{
    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }
    public Currency Currency { get; }

    public bool IsZero => Amount == 0m;

    public static Money Of(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(Round(amount, currency), currency);
    }

    public static Money Zero(Currency currency) => Of(0m, currency);

    /// <summary>Rounds half away from zero, the usual accounting convention.</summary>
    public static decimal Round(decimal amount, Currency currency) =>
        Math.Round(amount, currency.MinorUnits, MidpointRounding.AwayFromZero);

    public Money Negate() => Of(-Amount, Currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return Of(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return Of(left.Amount - right.Amount, left.Currency);
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException(
                $"Cannot combine {left.Currency.Code} and {right.Currency.Code} amounts without an exchange rate.");
    }
}
