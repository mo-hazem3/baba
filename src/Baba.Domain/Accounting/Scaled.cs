namespace Baba.Domain.Accounting;

/// <summary>
/// Money is stored as a whole number of 1/10,000 units (ADR 0002), so SQL sums stay exact for every currency
/// (2 or 3 decimals). Entities keep the stored <c>long</c> and expose a decimal view next to it.
/// </summary>
public static class Scaled
{
    public const decimal Scale = 10_000m;

    public static long ToScaled(decimal amount) => (long)Math.Round(amount * Scale, 0, MidpointRounding.AwayFromZero);

    public static decimal ToDecimal(long scaled) => scaled / Scale;
}

/// <summary>Exchange rates are stored as a whole number of millionths (1.000000 = 1,000,000), because they need more decimals than money.</summary>
public static class FxRate
{
    public const long One = 1_000_000;
    public const decimal Scale = 1_000_000m;

    public static long ToScaled(decimal rate) => (long)Math.Round(rate * Scale, 0, MidpointRounding.AwayFromZero);

    public static decimal ToDecimal(long scaled) => scaled / Scale;
}
