using Baba.Domain;

namespace Baba.Localization;

public sealed record CurrencyInfo(Currency Currency, string NameEn, string NameAr)
{
    public string Code => Currency.Code;
}

/// <summary>
/// Currencies a company can choose as its base or transaction currency, with their ISO 4217 minor units.
/// This is generic reference data (a company in any country can use any of them), not a country rule.
/// </summary>
public static class CurrencyCatalog
{
    public static IReadOnlyList<CurrencyInfo> All { get; } =
    [
        // Arab region
        New("EGP", 2, "Egyptian pound", "جنيه مصري"),
        New("SAR", 2, "Saudi riyal", "ريال سعودي"),
        New("AED", 2, "UAE dirham", "درهم إماراتي"),
        New("KWD", 3, "Kuwaiti dinar", "دينار كويتي"),
        New("BHD", 3, "Bahraini dinar", "دينار بحريني"),
        New("OMR", 3, "Omani rial", "ريال عماني"),
        New("QAR", 2, "Qatari riyal", "ريال قطري"),
        New("JOD", 3, "Jordanian dinar", "دينار أردني"),
        New("LBP", 2, "Lebanese pound", "ليرة لبنانية"),
        New("IQD", 3, "Iraqi dinar", "دينار عراقي"),
        New("TND", 3, "Tunisian dinar", "دينار تونسي"),
        New("LYD", 3, "Libyan dinar", "دينار ليبي"),
        New("MAD", 2, "Moroccan dirham", "درهم مغربي"),
        New("DZD", 2, "Algerian dinar", "دينار جزائري"),
        New("SDG", 2, "Sudanese pound", "جنيه سوداني"),
        New("YER", 2, "Yemeni rial", "ريال يمني"),

        // Major world currencies
        New("USD", 2, "US dollar", "دولار أمريكي"),
        New("EUR", 2, "Euro", "يورو"),
        New("GBP", 2, "Pound sterling", "جنيه إسترليني"),
        New("CHF", 2, "Swiss franc", "فرنك سويسري"),
        New("TRY", 2, "Turkish lira", "ليرة تركية"),
        New("CNY", 2, "Chinese yuan", "يوان صيني"),
        New("INR", 2, "Indian rupee", "روبية هندية"),
        New("JPY", 0, "Japanese yen", "ين ياباني"),
    ];

    public static CurrencyInfo? Find(string code) =>
        All.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    private static CurrencyInfo New(string code, int minorUnits, string nameEn, string nameAr) =>
        new(new Currency(code, minorUnits), nameEn, nameAr);
}
