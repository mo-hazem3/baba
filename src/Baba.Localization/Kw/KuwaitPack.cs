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

    public override CalendarRules Calendar { get; } = new(
        Calendars: [CalendarKind.Gregorian, CalendarKind.HijriUmmAlQura],
        DefaultWeekend: [DayOfWeek.Friday, DayOfWeek.Saturday],
        DefaultFiscalYearStartMonth: 1);
}
