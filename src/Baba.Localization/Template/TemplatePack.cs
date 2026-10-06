using Baba.Domain;

namespace Baba.Localization.Template;

/// <summary>
/// The empty starting point for a new country. To add a country, copy this folder to
/// <c>Baba.Localization/&lt;Code&gt;/</c>, rename the namespace and class, fill in the data below, remove
/// <see cref="ExcludeFromDiscoveryAttribute"/>, and add tests in <c>tests/Baba.Localization.Tests/&lt;Code&gt;/</c>.
/// Everything else (tax codes, registration numbers, chart, document rules, ...) is optional.
/// Full checklist: docs/countries/README.md.
/// </summary>
[ExcludeFromDiscovery]
public sealed class TemplatePack : CountryPackBase
{
    public override CountryIdentity Identity { get; } = new(
        Code: "XX",
        NameEn: "Template country",
        NameAr: "دولة نموذجية",
        Languages: ["ar", "en"]);

    public override CurrencyRules Currency { get; } = new(
        new Currency("XXX", minorUnits: 2),
        MajorNameEn: "Unit",
        MajorNameAr: "وحدة",
        MinorNameEn: "Cent",
        MinorNameAr: "سنت");

    public override CalendarRules Calendar { get; } = new(
        Calendars: [CalendarKind.Gregorian],
        DefaultWeekend: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        DefaultFiscalYearStartMonth: 1);
}
