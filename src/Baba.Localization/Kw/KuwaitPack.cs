using Baba.Domain;

namespace Baba.Localization.Kw;

/// <summary>
/// Kuwait. There is no VAT at the moment, so the pack has no tax codes; adding them later is just data here.
/// The dinar uses 3 decimal places.
/// </summary>
public sealed class KuwaitPack : CountryPackBase
{
    public override CountryIdentity Identity { get; } = new("KW", "Kuwait", "الكويت", ["ar", "en"]);

    public override CurrencyRules Currency { get; } = new(
        new Currency("KWD", minorUnits: 3),
        MajorNameEn: "Kuwaiti dinar",
        MajorNameAr: "دينار كويتي",
        MinorNameEn: "Fils",
        MinorNameAr: "فلس");

    /// <summary>Defaults to verify with PIFSS and the labour law: Kuwaitis pay into PIFSS; the gratuity is 15 days' wage a year for five years, then a month a year, up to one and a half years' wage.</summary>
    public override IPayrollRules? Payroll { get; } = new PayrollRules(
        InsuranceForNationals: new SocialInsuranceScheme("PIFSS social security", "التأمينات الاجتماعية (المؤسسة العامة للتأمينات)", 10.5m, 11.5m, null, 2750m),
        InsuranceForForeigners: null,
        EndOfService: new DaysPerYearGratuity("End-of-service indemnity", "مكافأة نهاية الخدمة", 15m, 26m, 26m, 18m));

    public override CalendarRules Calendar { get; } = new(
        Calendars: [CalendarKind.Gregorian, CalendarKind.HijriUmmAlQura],
        DefaultWeekend: [DayOfWeek.Friday, DayOfWeek.Saturday],
        DefaultFiscalYearStartMonth: 1);
}
