using System.Text.RegularExpressions;
using Baba.Localization.Eg;

namespace Baba.Localization.Tests.Eg;

public class EgyptPackTests
{
    private readonly EgyptPack _pack = new();

    [Fact]
    public void Uses_the_pound_with_two_decimals() =>
        Assert.Equal(("EGP", 2), (_pack.Currency.Currency.Code, _pack.Currency.Currency.MinorUnits));

    [Fact]
    public void Standard_vat_is_fourteen_percent() =>
        Assert.Equal(14m, _pack.TaxCodes.Single(t => t.Category == TaxCategory.Standard).Rate);

    [Fact]
    public void Tax_registration_number_is_nine_digits()
    {
        var rule = _pack.TaxRegistration.Single();

        Assert.Matches(rule.Pattern!, "123456789");
        Assert.DoesNotMatch(rule.Pattern!, "12345678");
    }

    [Fact]
    public void Weekend_is_friday_and_saturday() =>
        Assert.Equal([DayOfWeek.Friday, DayOfWeek.Saturday], _pack.Calendar.DefaultWeekend);
}
