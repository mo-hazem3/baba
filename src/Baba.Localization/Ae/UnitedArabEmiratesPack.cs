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

    /// <summary>Defaults to verify with GPSSA and the labour law: UAE nationals pay into GPSSA, others do not; the gratuity is 21 days' basic wage a year for five years, then 30, up to two years' wage.</summary>
    public override IPayrollRules? Payroll { get; } = new PayrollRules(
        InsuranceForNationals: new SocialInsuranceScheme("GPSSA pension contributions", "اشتراكات التقاعد (الهيئة العامة للمعاشات)", 5m, 12.5m, null, 50000m),
        InsuranceForForeigners: null,
        EndOfService: new DaysPerYearGratuity("End-of-service gratuity", "مكافأة نهاية الخدمة", 21m, 30m, 30m, 24m));

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

    public override ITaxReturnDefinition? TaxReturn { get; } = new TaxReturnDefinition("VAT return", "الإقرار الضريبي لضريبة القيمة المضافة", TaxReturnFrequency.Quarterly);

    public override IReadOnlyList<TaxRegistrationRule> TaxRegistration { get; } =
    [
        new("trn", "Tax registration number (TRN)", "رقم التسجيل الضريبي", @"^\d{15}$", RequiredForCompany: true),
    ];
}
