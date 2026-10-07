using Baba.Localization;

namespace Baba.Application.Printing;

/// <summary>
/// An amount written out in words, for the "amount in words" line of cheques and vouchers (تفقيط), in English or Arabic and per
/// currency: "One thousand two hundred forty-nine dinars and five hundred fils only".
/// </summary>
public static class AmountInWords
{
    private const long MaxMajor = 999_999_999_999;

    /// <summary>Splits an amount into the main unit and the fraction as a whole number of fraction units (0.500 with 3 decimals is 500).</summary>
    public static (long Major, long Minor) Split(decimal amount, int minorUnits)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Only positive amounts can be written in words.");

        var scale = (long)Math.Pow(10, minorUnits);
        var total = (long)Math.Round(amount * scale, 0, MidpointRounding.AwayFromZero);
        if (total / scale > MaxMajor)
            throw new ArgumentOutOfRangeException(nameof(amount), "The amount is too large to write in words.");
        return (total / scale, total % scale);
    }

    // ------------------------------------------------------------------ English

    public static string English(decimal amount, CurrencyWords words, bool framing = true)
    {
        var (major, minor) = Split(amount, words.MinorUnits);

        var parts = new List<string>();
        if (major > 0 || minor == 0)
            parts.Add($"{EnglishNumber(major)} {(major == 1 ? words.MajorEnSingular : words.MajorEnPlural)}");
        if (minor > 0)
            parts.Add($"{EnglishNumber(minor)} {(minor == 1 ? words.MinorEnSingular : words.MinorEnPlural)}");

        var text = string.Join(" and ", parts);
        text = char.ToUpperInvariant(text[0]) + text[1..];
        return framing ? text + " only" : text;
    }

    private static readonly string[] EnglishOnes =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
        "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    ];

    private static readonly string[] EnglishTens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    private static string EnglishNumber(long n)
    {
        if (n < 20)
            return EnglishOnes[n];
        if (n < 100)
            return EnglishTens[n / 10] + (n % 10 == 0 ? "" : "-" + EnglishOnes[n % 10]);
        if (n < 1000)
            return EnglishOnes[n / 100] + " hundred" + (n % 100 == 0 ? "" : " " + EnglishNumber(n % 100));

        foreach (var (size, name) in new[] { (1_000_000_000L, "billion"), (1_000_000L, "million"), (1_000L, "thousand") })
        {
            if (n >= size)
                return EnglishNumber(n / size) + " " + name + (n % size == 0 ? "" : " " + EnglishNumber(n % size));
        }

        return "";
    }

    // ------------------------------------------------------------------ Arabic

    /// <summary>The Arabic text, for example "فقط ألف ومائتان وتسعة وأربعون ديناراً كويتياً وخمسمائة فلس لا غير".</summary>
    public static string Arabic(decimal amount, CurrencyWords words, bool framing = true)
    {
        var (major, minor) = Split(amount, words.MinorUnits);

        var parts = new List<string>();
        if (major > 0 || minor == 0)
            parts.Add(ArabicCounted(major, words.MajorAr));
        if (minor > 0)
            parts.Add(ArabicCounted(minor, words.MinorAr));

        var text = string.Join(" و", parts);
        return framing ? $"فقط {text} لا غير" : text;
    }

    private static readonly string[] OnesMasculineNoun = ["", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة", "عشرة"];
    private static readonly string[] OnesFeminineNoun = ["", "واحدة", "اثنتان", "ثلاث", "أربع", "خمس", "ست", "سبع", "ثمان", "تسع", "عشر"];
    private static readonly string[] TeensMasculineNoun = ["", "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر", "ستة عشر", "سبعة عشر", "ثمانية عشر", "تسعة عشر"];
    private static readonly string[] TeensFeminineNoun = ["", "إحدى عشرة", "اثنتا عشرة", "ثلاث عشرة", "أربع عشرة", "خمس عشرة", "ست عشرة", "سبع عشرة", "ثماني عشرة", "تسع عشرة"];
    private static readonly string[] ArabicTens = ["", "", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون"];
    private static readonly string[] ArabicHundreds = ["", "مائة", "مائتان", "ثلاثمائة", "أربعمائة", "خمسمائة", "ستمائة", "سبعمائة", "ثمانمائة", "تسعمائة"];

    private sealed record Scale(long Size, string Singular, string Dual, string Construct, string Plural, string Accusative);

    private static readonly Scale[] Scales =
    [
        new(1_000_000_000, "مليار", "ملياران", "مليارا", "مليارات", "ملياراً"),
        new(1_000_000, "مليون", "مليونان", "مليونا", "ملايين", "مليوناً"),
        new(1_000, "ألف", "ألفان", "ألفا", "آلاف", "ألفاً"),
    ];

    /// <summary>A number followed by the unit noun in the form Arabic grammar requires: "ثلاثة دنانير كويتية", "أحد عشر ديناراً كويتياً".</summary>
    private static string ArabicCounted(long n, ArabicUnit unit)
    {
        if (n == 0)
            return $"صفر {unit.Singular}";
        if (n == 1)
            return $"{unit.Singular} {(unit.Feminine ? OnesFeminineNoun[1] : OnesMasculineNoun[1])}"; // "دينار كويتي واحد"
        if (n == 2)
            return unit.Dual;

        var parts = ArabicNumberParts(n, unit.Feminine, out var lastIsDual);
        var remainder = n % 100;
        var noun = remainder switch
        {
            >= 3 and <= 10 => unit.Plural,
            >= 11 => unit.Accusative,
            _ => unit.Singular,
        };

        // "ألفان جنيه" is written "ألفا جنيه" when the noun follows directly (construct state), and likewise "مائتا جنيه".
        if (lastIsDual && remainder == 0)
            parts[^1] = ConstructForm(parts[^1]);

        return string.Join(" و", parts) + " " + noun;
    }

    /// <summary>The number as the parts joined by "و": for example 1,249 is ["ألف", "مائتان", "تسعة وأربعون"].</summary>
    private static List<string> ArabicNumberParts(long n, bool nounFeminine, out bool lastIsDual)
    {
        var parts = new List<string>();
        lastIsDual = false;

        foreach (var scale in Scales)
        {
            var count = n / scale.Size;
            n %= scale.Size;
            if (count == 0)
                continue;

            parts.Add(ArabicScaled(count, scale));
            lastIsDual = count == 2;
        }

        if (n > 0)
        {
            parts.AddRange(ArabicBelowThousand((int)n, nounFeminine));
            lastIsDual = n == 200;
        }

        return parts;
    }

    /// <summary>"ثلاثة آلاف", "أحد عشر ألفاً", "مائة ألف", "ألفان": a count of thousands, millions or billions with its scale word.</summary>
    private static string ArabicScaled(long count, Scale scale)
    {
        if (count == 1)
            return scale.Singular;
        if (count == 2)
            return scale.Dual;

        // The count itself is read with masculine agreement (ألف, مليون and مليار are masculine nouns).
        var words = string.Join(" و", ArabicBelowThousand((int)count, nounFeminine: false));
        var remainder = count % 100;
        var noun = remainder switch
        {
            >= 3 and <= 10 => scale.Plural,
            >= 11 => scale.Accusative,
            _ => scale.Singular,
        };

        return count == 200 ? $"{ConstructForm(words)} {noun}" : $"{words} {noun}";
    }

    /// <summary>1 to 999 as parts joined by "و" in reading order: hundreds, then ones, then tens ("تسعة وأربعون").</summary>
    private static IEnumerable<string> ArabicBelowThousand(int n, bool nounFeminine)
    {
        var hundreds = n / 100;
        var rest = n % 100;
        if (hundreds > 0)
            yield return ArabicHundreds[hundreds];

        if (rest == 0)
            yield break;

        if (rest <= 10)
        {
            yield return (nounFeminine ? OnesFeminineNoun : OnesMasculineNoun)[rest];
        }
        else if (rest < 20)
        {
            yield return (nounFeminine ? TeensFeminineNoun : TeensMasculineNoun)[rest - 10];
        }
        else
        {
            var ones = rest % 10;
            if (ones == 1 && nounFeminine)
                yield return "إحدى"; // 21, 31 ... with a feminine noun: "إحدى وعشرون هللةً" (not "واحدة وعشرون")
            else if (ones > 0)
                yield return (nounFeminine ? OnesFeminineNoun : OnesMasculineNoun)[ones];
            yield return ArabicTens[rest / 10];
        }
    }

    /// <summary>The dual in the construct state: the final "ان" is dropped and an alef added ("ألفان" becomes "ألفا", "مائتان" becomes "مائتا").</summary>
    private static string ConstructForm(string dual) =>
        dual.EndsWith("ان", StringComparison.Ordinal) ? dual[..^2] + "ا" : dual;
}
