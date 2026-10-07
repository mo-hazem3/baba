using System.Text.RegularExpressions;

namespace Baba.Application.Importing;

/// <summary>Arabic text helpers for matching names typed by different people (the same rules as the search in the web app).</summary>
public static partial class ArabicText
{
    /// <summary>Lower case, without diacritics or tatweel, with letter variants made equal (أ إ آ become ا, ى becomes ي, ة becomes ه) and digits made Western.</summary>
    public static string Normalize(string text)
    {
        var clean = Diacritics().Replace(text.Normalize(System.Text.NormalizationForm.FormKC), "");
        clean = clean.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ٱ', 'ا')
            .Replace('ى', 'ي').Replace('ة', 'ه').Replace('ؤ', 'و').Replace('ئ', 'ي');
        return Digits(clean).ToLowerInvariant();
    }

    /// <summary>Arabic-Indic (٠١٢) and Persian (۰۱۲) digits as Western digits.</summary>
    public static string Digits(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '٠' and <= '٩')
                chars[i] = (char)('0' + (chars[i] - '٠'));
            else if (chars[i] is >= '۰' and <= '۹')
                chars[i] = (char)('0' + (chars[i] - '۰'));
        }

        return new string(chars);
    }

    [GeneratedRegex("[ً-ٰـ]")]
    private static partial Regex Diacritics();
}
