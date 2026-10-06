using Baba.Domain;

namespace Baba.Localization.Sa;

/// <summary>Saudi Arabia. Rates and formats here are a starting point: verify against ZATCA before relying on them.</summary>
public sealed class SaudiArabiaPack : CountryPackBase
{
    public override CountryIdentity Identity { get; } = new("SA", "Saudi Arabia", "المملكة العربية السعودية", ["ar", "en"]);

    public override CurrencyRules Currency { get; } = new(
        new Currency("SAR", minorUnits: 2),
        MajorNameEn: "Saudi riyal",
        MajorNameAr: "ريال سعودي",
        MinorNameEn: "Halala",
        MinorNameAr: "هللة");

    public override CalendarRules Calendar { get; } = new(
        Calendars: [CalendarKind.Gregorian, CalendarKind.HijriUmmAlQura],
        DefaultWeekend: [DayOfWeek.Friday, DayOfWeek.Saturday],
        DefaultFiscalYearStartMonth: 1);

    public override IReadOnlyList<TaxCodeDefinition> TaxCodes { get; } =
    [
        new("SA-VAT-STD", "VAT 15%", "ضريبة القيمة المضافة ١٥٪", 15m, TaxCategory.Standard, new DateOnly(2020, 7, 1)),
        new("SA-VAT-ZERO", "VAT zero-rated", "ضريبة القيمة المضافة بنسبة صفر", 0m, TaxCategory.Zero),
        new("SA-VAT-EXEMPT", "VAT exempt", "معفى من ضريبة القيمة المضافة", 0m, TaxCategory.Exempt),
        new("SA-VAT-OUT", "Out of scope", "خارج نطاق الضريبة", 0m, TaxCategory.OutOfScope),
    ];

    public override IReadOnlyList<TaxRegistrationRule> TaxRegistration { get; } =
    [
        new("vat-number", "VAT registration number", "رقم التسجيل في ضريبة القيمة المضافة", @"^3\d{13}3$", RequiredForCompany: true),
        new("commercial-registration", "Commercial registration (CR) number", "رقم السجل التجاري", @"^\d{10}$", RequiredForCompany: false),
    ];

    public override DocumentRules DocumentRules { get; } = new(
        SubmittedDocumentsAreImmutable: true,
        BilingualPrintRequired: true);
}
