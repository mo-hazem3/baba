namespace Baba.Domain;

/// <summary>A currency and how many decimal places its minor unit uses (2 for most, 3 for dinar-type currencies).</summary>
public sealed record Currency
{
    public Currency(string code, int minorUnits)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 3)
            throw new ArgumentException("Currency code must be a 3-letter ISO code.", nameof(code));
        if (minorUnits is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(minorUnits), "Minor units must be between 0 and 4.");

        Code = code.ToUpperInvariant();
        MinorUnits = minorUnits;
    }

    public string Code { get; }
    public int MinorUnits { get; }
}
