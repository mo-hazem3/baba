using Baba.Domain;

namespace Baba.Localization.Ae;

/// <summary>United Arab Emirates. Rates and formats here are a starting point: verify against the FTA before relying on them.</summary>
public sealed class UnitedArabEmiratesPack : CountryPackBase
{
    public override CountryIdentity Identity { get; } = new("AE", "United Arab Emirates", "الإمارات العربية المتحدة", ["ar", "en"]);

    public override CurrencyRules Currency { get; } = new(
        new Currency("AED", minorUnits: 2),
        MajorNameEn: "UAE dirham",
        MajorNameAr: "درهم إماراتي",
        MinorNameEn: "Fils",
        MinorNameAr: "فلس");

    public override CalendarRules Calendar { get; } = new(
        Calendars: [CalendarKind.Gregorian, CalendarKind.HijriUmmAlQura],
        DefaultWeekend: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        DefaultFiscalYearStartMonth: 1);

    public override IReadOnlyList<TaxCodeDefinition> TaxCodes { get; } =
    [
        new("AE-VAT-STD", "VAT 5%", "ضريبة القيمة المضافة ٥٪", 5m, TaxCategory.Standard, new DateOnly(2018, 1, 1)),
        new("AE-VAT-ZERO", "VAT zero-rated", "ضريبة القيمة المضافة بنسبة صفر", 0m, TaxCategory.Zero),
        new("AE-VAT-EXEMPT", "VAT exempt", "معفى من ضريبة القيمة المضافة", 0m, TaxCategory.Exempt),
        new("AE-VAT-OUT", "Out of scope", "خارج نطاق الضريبة", 0m, TaxCategory.OutOfScope),
    ];

    public override IReadOnlyList<TaxRegistrationRule> TaxRegistration { get; } =
    [
        new("trn", "Tax registration number (TRN)", "رقم التسجيل الضريبي", @"^\d{15}$", RequiredForCompany: true),
    ];
}
