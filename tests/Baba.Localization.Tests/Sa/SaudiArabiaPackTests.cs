using Baba.Localization.Sa;

namespace Baba.Localization.Tests.Sa;

public class SaudiArabiaPackTests
{
    private readonly SaudiArabiaPack _pack = new();

    [Fact]
    public void Uses_the_riyal_with_two_decimals() =>
        Assert.Equal(("SAR", 2), (_pack.Currency.Currency.Code, _pack.Currency.Currency.MinorUnits));

    [Fact]
    public void Standard_vat_is_fifteen_percent() =>
        Assert.Equal(15m, _pack.TaxCodes.Single(t => t.Category == TaxCategory.Standard).Rate);

    [Fact]
    public void Vat_number_is_fifteen_digits_starting_and_ending_with_three()
    {
        var pattern = _pack.TaxRegistration.Single(r => r.Key == "vat-number").Pattern!;

        Assert.Matches(pattern, "300123456789003");
        Assert.DoesNotMatch(pattern, "200123456789003");
        Assert.DoesNotMatch(pattern, "30012345678900");
    }

    [Fact]
    public void Offers_hijri_dates_and_requires_bilingual_immutable_documents()
    {
        Assert.Contains(CalendarKind.HijriUmmAlQura, _pack.Calendar.Calendars);
        Assert.True(_pack.DocumentRules.SubmittedDocumentsAreImmutable);
        Assert.True(_pack.DocumentRules.BilingualPrintRequired);
    }
}
