using System.Globalization;

namespace Baba.Application.Printing;

/// <summary>Number formatting for printouts: thousands separators, the currency's decimals, Western or Arabic-Indic digits.</summary>
public static class NumberText
{
    private const string ArabicIndicDigits = "٠١٢٣٤٥٦٧٨٩";

    public static string Amount(decimal value, int minorUnits, bool arabicIndic)
    {
        var text = Math.Abs(value).ToString("N" + minorUnits, CultureInfo.InvariantCulture);
        if (value < 0)
            text = "-" + text;
        return arabicIndic ? ToArabicIndic(text) : text;
    }

    public static string Integer(int value, bool arabicIndic) =>
        arabicIndic ? ToArabicIndic(value.ToString(CultureInfo.InvariantCulture)) : value.ToString(CultureInfo.InvariantCulture);

    public static string ToArabicIndic(string text) =>
        string.Concat(text.Select(c => c switch
        {
            >= '0' and <= '9' => ArabicIndicDigits[c - '0'],
            '.' => '٫',
            ',' => '٬',
            _ => c,
        }));
}
