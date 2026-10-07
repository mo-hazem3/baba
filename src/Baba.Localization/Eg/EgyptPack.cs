using Baba.Domain;

namespace Baba.Localization.Eg;

/// <summary>Egypt. Rates and formats here are a starting point: verify against ETA before relying on them.</summary>
public sealed class EgyptPack : CountryPackBase
{
    public override CountryIdentity Identity { get; } = new("EG", "Egypt", "مصر", ["ar", "en"]);

    public override CurrencyRules Currency { get; } = new(
        new Currency("EGP", minorUnits: 2),
        MajorNameEn: "Egyptian pound",
        MajorNameAr: "جنيه مصري",
        MinorNameEn: "Piastre",
        MinorNameAr: "قرش");

    /// <summary>Defaults to verify with the National Organization for Social Insurance: employees pay 11% and employers 18.75%. The insurable wage limits change every year, so they are left empty here: enter the current ones in the payroll settings. Egyptian law has no end-of-service gratuity.</summary>
    public override IPayrollRules? Payroll { get; } = new PayrollRules(
        InsuranceForNationals: new SocialInsuranceScheme("Social insurance", "التأمينات الاجتماعية", 11m, 18.75m, null, null),
        InsuranceForForeigners: null,
        EndOfService: null);

    public override CalendarRules Calendar { get; } = new(
        Calendars: [CalendarKind.Gregorian],
        DefaultWeekend: [DayOfWeek.Friday, DayOfWeek.Saturday],
        DefaultFiscalYearStartMonth: 1);

    public override IReadOnlyList<TaxCodeDefinition> TaxCodes { get; } =
    [
        new("EG-VAT-STD", "VAT 14%", "ضريبة القيمة المضافة ١٤٪", 14m, TaxCategory.Standard, new DateOnly(2017, 7, 1)),
        new("EG-VAT-ZERO", "VAT zero-rated", "ضريبة القيمة المضافة بنسبة صفر", 0m, TaxCategory.Zero),
        new("EG-VAT-EXEMPT", "VAT exempt", "معفى من ضريبة القيمة المضافة", 0m, TaxCategory.Exempt),
    ];

    public override ITaxReturnDefinition? TaxReturn { get; } = new TaxReturnDefinition("VAT return", "إقرار ضريبة القيمة المضافة", TaxReturnFrequency.Monthly);

    public override IReadOnlyList<TaxRegistrationRule> TaxRegistration { get; } =
    [
        new("tax-registration-number", "Tax registration number", "رقم التسجيل الضريبي", @"^\d{9}$", RequiredForCompany: true),
    ];
}
