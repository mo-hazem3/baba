using Baba.Application.Printing;
using Baba.Localization;

namespace Baba.Application.Tests.Printing;

public class AmountInWordsTests
{
    private static CurrencyWords Words(string code) => CurrencyWordsCatalog.Find(code)!;

    // ---------------------------------------------------------------- English

    [Theory]
    [InlineData("KWD", 0, "Zero Kuwaiti dinars only")]
    [InlineData("KWD", 1, "One Kuwaiti dinar only")]
    [InlineData("KWD", 2, "Two Kuwaiti dinars only")]
    [InlineData("KWD", 1249.5, "One thousand two hundred forty-nine Kuwaiti dinars and five hundred fils only")]
    [InlineData("KWD", 0.001, "One fils only")]
    [InlineData("KWD", 0.5, "Five hundred fils only")]
    [InlineData("KWD", 1.001, "One Kuwaiti dinar and one fils only")]
    [InlineData("SAR", 21.05, "Twenty-one Saudi riyals and five halalas only")]
    [InlineData("AED", 100, "One hundred UAE dirhams only")]
    [InlineData("AED", 1.01, "One UAE dirham and one fils only")]
    [InlineData("EGP", 1234567.89, "One million two hundred thirty-four thousand five hundred sixty-seven Egyptian pounds and eighty-nine piastres only")]
    [InlineData("EGP", 1_000_000_000, "One billion Egyptian pounds only")]
    [InlineData("USD", 1_000_000, "One million US dollars only")]
    [InlineData("USD", 19, "Nineteen US dollars only")]
    [InlineData("USD", 90, "Ninety US dollars only")]
    [InlineData("GBP", 0.01, "One penny only")]
    [InlineData("GBP", 0.5, "Fifty pence only")]
    [InlineData("OMR", 0.005, "Five baisa only")]
    public void English_amounts_read_naturally_with_the_right_singular_and_plural(string code, double amount, string expected) =>
        Assert.Equal(expected, AmountInWords.English((decimal)amount, Words(code)));

    [Fact]
    public void English_without_the_closing_word_for_use_inside_a_sentence() =>
        Assert.Equal("Five US dollars", AmountInWords.English(5m, Words("USD"), framing: false));

    // ---------------------------------------------------------------- Arabic

    [Theory]
    // The example from the brief, and the forms of the noun: singular, dual, plural, accusative.
    [InlineData("KWD", 1249.5, "فقط ألف ومائتان وتسعة وأربعون ديناراً كويتياً وخمسمائة فلس لا غير")]
    [InlineData("EGP", 0, "فقط صفر جنيه مصري لا غير")]
    [InlineData("EGP", 1, "فقط جنيه مصري واحد لا غير")]
    [InlineData("EGP", 2, "فقط جنيهان مصريان لا غير")]
    [InlineData("EGP", 3, "فقط ثلاثة جنيهات مصرية لا غير")]
    [InlineData("EGP", 10, "فقط عشرة جنيهات مصرية لا غير")]
    [InlineData("EGP", 11, "فقط أحد عشر جنيهاً مصرياً لا غير")]
    [InlineData("EGP", 12, "فقط اثنا عشر جنيهاً مصرياً لا غير")]
    [InlineData("EGP", 19, "فقط تسعة عشر جنيهاً مصرياً لا غير")]
    [InlineData("EGP", 20, "فقط عشرون جنيهاً مصرياً لا غير")]
    [InlineData("EGP", 25, "فقط خمسة وعشرون جنيهاً مصرياً لا غير")]
    [InlineData("EGP", 99, "فقط تسعة وتسعون جنيهاً مصرياً لا غير")]
    [InlineData("EGP", 100, "فقط مائة جنيه مصري لا غير")]
    [InlineData("EGP", 101, "فقط مائة وواحد جنيه مصري لا غير")]
    [InlineData("EGP", 103, "فقط مائة وثلاثة جنيهات مصرية لا غير")]
    [InlineData("EGP", 111, "فقط مائة وأحد عشر جنيهاً مصرياً لا غير")]
    [InlineData("EGP", 200, "فقط مائتا جنيه مصري لا غير")]
    [InlineData("EGP", 300, "فقط ثلاثمائة جنيه مصري لا غير")]
    [InlineData("EGP", 999, "فقط تسعمائة وتسعة وتسعون جنيهاً مصرياً لا غير")]
    // Thousands, millions, billions.
    [InlineData("EGP", 1000, "فقط ألف جنيه مصري لا غير")]
    [InlineData("EGP", 2000, "فقط ألفا جنيه مصري لا غير")]
    [InlineData("EGP", 2500, "فقط ألفان وخمسمائة جنيه مصري لا غير")]
    [InlineData("EGP", 3000, "فقط ثلاثة آلاف جنيه مصري لا غير")]
    [InlineData("EGP", 10000, "فقط عشرة آلاف جنيه مصري لا غير")]
    [InlineData("EGP", 11000, "فقط أحد عشر ألفاً جنيه مصري لا غير")]
    [InlineData("EGP", 100000, "فقط مائة ألف جنيه مصري لا غير")]
    [InlineData("EGP", 200000, "فقط مائتا ألف جنيه مصري لا غير")]
    [InlineData("EGP", 1000000, "فقط مليون جنيه مصري لا غير")]
    [InlineData("EGP", 2000000, "فقط مليونا جنيه مصري لا غير")]
    [InlineData("EGP", 3000000, "فقط ثلاثة ملايين جنيه مصري لا غير")]
    [InlineData("EGP", 1000000000, "فقط مليار جنيه مصري لا غير")]
    [InlineData("EGP", 2000000000, "فقط مليارا جنيه مصري لا غير")]
    [InlineData("KWD", 123456.789, "فقط مائة وثلاثة وعشرون ألفاً وأربعمائة وستة وخمسون ديناراً كويتياً وسبعمائة وتسعة وثمانون فلساً لا غير")]
    public void Arabic_amounts_use_the_right_number_and_noun_forms(string code, double amount, string expected) =>
        Assert.Equal(expected, AmountInWords.Arabic((decimal)amount, Words(code)));

    [Theory]
    // A feminine fraction unit (halala, baisa) takes the opposite-gender number forms.
    [InlineData("SAR", 5.5, "فقط خمسة ريالات سعودية وخمسون هللةً لا غير")]
    [InlineData("SAR", 0.03, "فقط ثلاث هللات لا غير")]
    [InlineData("SAR", 0.02, "فقط هللتان لا غير")]
    [InlineData("SAR", 1.01, "فقط ريال سعودي واحد وهللة واحدة لا غير")]
    [InlineData("SAR", 13, "فقط ثلاثة عشر ريالاً سعودياً لا غير")]
    [InlineData("SAR", 0.13, "فقط ثلاث عشرة هللةً لا غير")]
    [InlineData("SAR", 0.12, "فقط اثنتا عشرة هللةً لا غير")]
    [InlineData("SAR", 0.11, "فقط إحدى عشرة هللةً لا غير")]
    [InlineData("SAR", 0.21, "فقط إحدى وعشرون هللةً لا غير")]
    [InlineData("OMR", 0.005, "فقط خمس بيسات لا غير")]
    // Masculine fraction units.
    [InlineData("AED", 12.34, "فقط اثنا عشر درهماً إماراتياً وأربعة وثلاثون فلساً لا غير")]
    [InlineData("KWD", 0.001, "فقط فلس واحد لا غير")]
    [InlineData("KWD", 0.5, "فقط خمسمائة فلس لا غير")]
    [InlineData("KWD", 3.25, "فقط ثلاثة دنانير كويتية ومائتان وخمسون فلساً لا غير")]
    [InlineData("EUR", 5, "فقط خمسة يورو لا غير")]
    public void Arabic_fractions_follow_the_gender_of_their_unit(string code, double amount, string expected) =>
        Assert.Equal(expected, AmountInWords.Arabic((decimal)amount, Words(code)));

    [Fact]
    public void Arabic_without_the_framing_words_for_use_inside_a_sentence() =>
        Assert.Equal("ثلاثة دنانير كويتية", AmountInWords.Arabic(3m, Words("KWD"), framing: false));

    // ---------------------------------------------------------------- Shared rules

    [Theory]
    [InlineData(1249.5, 3, 1249, 500)]
    [InlineData(0.001, 3, 0, 1)]
    [InlineData(21.05, 2, 21, 5)]
    [InlineData(1.0005, 3, 1, 1)]  // rounds half away from zero, as everywhere in Baba
    [InlineData(5, 0, 5, 0)]
    public void An_amount_splits_into_a_main_part_and_a_whole_number_of_fraction_units(double amount, int decimals, long major, long minor) =>
        Assert.Equal((major, minor), AmountInWords.Split((decimal)amount, decimals));

    [Fact]
    public void Negative_and_absurdly_large_amounts_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AmountInWords.English(-1m, Words("USD")));
        Assert.Throws<ArgumentOutOfRangeException>(() => AmountInWords.Arabic(1_000_000_000_000m, Words("USD")));
        AmountInWords.English(999_999_999_999.99m, Words("USD")); // the largest amount that fits
    }

    [Fact]
    public void A_currency_without_its_own_words_still_reads_sensibly()
    {
        var generic = CurrencyWords.Generic("XYZ", 2);

        Assert.Equal("Five XYZ and twenty-five subunits only", AmountInWords.English(5.25m, generic));
        Assert.Contains("خمسة XYZ", AmountInWords.Arabic(5.25m, generic));
    }

    [Fact]
    public void Every_catalog_currency_has_complete_forms_and_matching_decimals()
    {
        foreach (var words in CurrencyWordsCatalog.All)
        {
            Assert.Equal(CurrencyCatalog.Find(words.Code)!.Currency.MinorUnits, words.MinorUnits);
            foreach (var unit in new[] { words.MajorAr, words.MinorAr })
                Assert.All([unit.Singular, unit.Dual, unit.Plural, unit.Accusative], form => Assert.False(string.IsNullOrWhiteSpace(form), words.Code));
            Assert.All([words.MajorEnSingular, words.MajorEnPlural, words.MinorEnSingular, words.MinorEnPlural], form => Assert.False(string.IsNullOrWhiteSpace(form), words.Code));
        }

        // The four currencies the brief names are all there.
        Assert.All(new[] { "EGP", "SAR", "AED", "KWD" }, code => Assert.NotNull(CurrencyWordsCatalog.Find(code)));
    }

    [Fact]
    public void Every_amount_from_0_to_1999_has_words_in_both_languages_without_gaps()
    {
        var words = Words("KWD");
        for (var n = 0; n < 2000; n++)
        {
            var english = AmountInWords.English(n, words);
            var arabic = AmountInWords.Arabic(n, words);
            Assert.False(string.IsNullOrWhiteSpace(english), n.ToString());
            Assert.DoesNotContain("  ", english); // never a double space
            Assert.DoesNotContain("  ", arabic);
            Assert.DoesNotContain(" و ", arabic); // the "و" is always attached to the next word
        }
    }
}
