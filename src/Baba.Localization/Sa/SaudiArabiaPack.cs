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

    public override ITaxReturnDefinition? TaxReturn { get; } = new TaxReturnDefinition("VAT return", "الإقرار الضريبي لضريبة القيمة المضافة", TaxReturnFrequency.Quarterly);

    public override IReadOnlyList<TaxRegistrationRule> TaxRegistration { get; } =
    [
        new("vat-number", "VAT registration number", "رقم التسجيل في ضريبة القيمة المضافة", @"^3\d{13}3$", RequiredForCompany: true),
        new("commercial-registration", "Commercial registration (CR) number", "رقم السجل التجاري", @"^\d{10}$", RequiredForCompany: false),
    ];

    /// <summary>Defaults to verify with GOSI and the labour law (articles 84 and 85): Saudis pay annuities and SANED, foreigners only the employer's occupational hazards share; the gratuity is half a month's wage a year for five years, then a month a year.</summary>
    public override IPayrollRules? Payroll { get; } = new PayrollRules(
        InsuranceForNationals: new SocialInsuranceScheme("GOSI social insurance", "التأمينات الاجتماعية (GOSI)", 9.75m, 11.75m, 1500m, 45000m),
        InsuranceForForeigners: new SocialInsuranceScheme("GOSI occupational hazards", "التأمينات الاجتماعية: أخطار العمل (GOSI)", 0m, 2m, 1500m, 45000m),
        EndOfService: new DaysPerYearGratuity("End-of-service award", "مكافأة نهاية الخدمة", 15m, 30m, 30m, null));

    public override IEInvoicingProvider? EInvoicing { get; } = new ZatcaEInvoicing();

    public override DocumentRules DocumentRules { get; } = new(
        SubmittedDocumentsAreImmutable: true,
        BilingualPrintRequired: true);
}
