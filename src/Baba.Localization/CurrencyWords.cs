namespace Baba.Localization;

/// <summary>
/// The Arabic forms of a currency unit that amount-in-words needs. Arabic changes the noun with the number:
/// one dinar (singular), two (dual), three to ten (plural), eleven to ninety-nine (accusative singular, ending in tanween).
/// </summary>
public sealed record ArabicUnit(string Singular, string Dual, string Plural, string Accusative, bool Feminine = false);

/// <summary>The words for a currency's main unit and its fraction, in English and Arabic (brief section 5).</summary>
public sealed record CurrencyWords(
    string Code,
    int MinorUnits,
    string MajorEnSingular,
    string MajorEnPlural,
    string MinorEnSingular,
    string MinorEnPlural,
    ArabicUnit MajorAr,
    ArabicUnit MinorAr)
{
    /// <summary>For a currency without its own entry: the code stands in as the unit name.</summary>
    public static CurrencyWords Generic(string code, int minorUnits) => new(
        code, minorUnits, code, code, "subunit", "subunits",
        new ArabicUnit(code, code, code, code), new ArabicUnit("جزء", "جزءان", "أجزاء", "جزءاً"));
}

/// <summary>
/// Unit names for the currencies a company is likely to use. Egyptian pound, Saudi riyal, UAE dirham and Kuwaiti dinar come
/// first (brief section 5), then the other Gulf and Arab currencies and the major world ones.
/// </summary>
public static class CurrencyWordsCatalog
{
    private static readonly Dictionary<string, CurrencyWords> Entries = new CurrencyWords[]
    {
        new("EGP", 2, "Egyptian pound", "Egyptian pounds", "piastre", "piastres",
            new("جنيه مصري", "جنيهان مصريان", "جنيهات مصرية", "جنيهاً مصرياً"), new("قرش", "قرشان", "قروش", "قرشاً")),
        new("SAR", 2, "Saudi riyal", "Saudi riyals", "halala", "halalas",
            new("ريال سعودي", "ريالان سعوديان", "ريالات سعودية", "ريالاً سعودياً"), new("هللة", "هللتان", "هللات", "هللةً", Feminine: true)),
        new("AED", 2, "UAE dirham", "UAE dirhams", "fils", "fils",
            new("درهم إماراتي", "درهمان إماراتيان", "دراهم إماراتية", "درهماً إماراتياً"), new("فلس", "فلسان", "فلوس", "فلساً")),
        new("KWD", 3, "Kuwaiti dinar", "Kuwaiti dinars", "fils", "fils",
            new("دينار كويتي", "ديناران كويتيان", "دنانير كويتية", "ديناراً كويتياً"), new("فلس", "فلسان", "فلوس", "فلساً")),
        new("BHD", 3, "Bahraini dinar", "Bahraini dinars", "fils", "fils",
            new("دينار بحريني", "ديناران بحرينيان", "دنانير بحرينية", "ديناراً بحرينياً"), new("فلس", "فلسان", "فلوس", "فلساً")),
        new("OMR", 3, "Omani rial", "Omani rials", "baisa", "baisa",
            new("ريال عماني", "ريالان عمانيان", "ريالات عمانية", "ريالاً عمانياً"), new("بيسة", "بيستان", "بيسات", "بيسةً", Feminine: true)),
        new("QAR", 2, "Qatari riyal", "Qatari riyals", "dirham", "dirhams",
            new("ريال قطري", "ريالان قطريان", "ريالات قطرية", "ريالاً قطرياً"), new("درهم", "درهمان", "دراهم", "درهماً")),
        new("JOD", 3, "Jordanian dinar", "Jordanian dinars", "fils", "fils",
            new("دينار أردني", "ديناران أردنيان", "دنانير أردنية", "ديناراً أردنياً"), new("فلس", "فلسان", "فلوس", "فلساً")),
        new("USD", 2, "US dollar", "US dollars", "cent", "cents",
            new("دولار أمريكي", "دولاران أمريكيان", "دولارات أمريكية", "دولاراً أمريكياً"), new("سنت", "سنتان", "سنتات", "سنتاً")),
        new("EUR", 2, "euro", "euros", "cent", "cents",
            new("يورو", "يورو", "يورو", "يورو"), new("سنت", "سنتان", "سنتات", "سنتاً")),
        new("GBP", 2, "pound sterling", "pounds sterling", "penny", "pence",
            new("جنيه إسترليني", "جنيهان إسترلينيان", "جنيهات إسترلينية", "جنيهاً إسترلينياً"), new("بنس", "بنسان", "بنسات", "بنساً")),
    }.ToDictionary(w => w.Code);

    public static CurrencyWords? Find(string code) => Entries.GetValueOrDefault(code.ToUpperInvariant());

    public static IReadOnlyCollection<CurrencyWords> All => Entries.Values;
}
