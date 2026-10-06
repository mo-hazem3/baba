using Baba.Localization.Ae;

namespace Baba.Localization.Tests.Ae;

public class UnitedArabEmiratesPackTests
{
    private readonly UnitedArabEmiratesPack _pack = new();

    [Fact]
    public void Uses_the_dirham_with_two_decimals() =>
        Assert.Equal(("AED", 2), (_pack.Currency.Currency.Code, _pack.Currency.Currency.MinorUnits));

    [Fact]
    public void Standard_vat_is_five_percent() =>
        Assert.Equal(5m, _pack.TaxCodes.Single(t => t.Category == TaxCategory.Standard).Rate);

    [Fact]
    public void Trn_is_fifteen_digits()
    {
        var pattern = _pack.TaxRegistration.Single().Pattern!;

        Assert.Matches(pattern, "100123456700003");
        Assert.DoesNotMatch(pattern, "10012345670000");
    }

    [Fact]
    public void Weekend_is_saturday_and_sunday() =>
        Assert.Equal([DayOfWeek.Saturday, DayOfWeek.Sunday], _pack.Calendar.DefaultWeekend);
}
