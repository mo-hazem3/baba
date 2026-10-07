using System.Globalization;
using System.Text.RegularExpressions;

namespace Baba.Application.Importing;

/// <summary>Reads a spreadsheet or CSV file into rows of text, whatever its format. Implemented by Infrastructure.</summary>
public interface ITabularReader
{
    /// <summary>The first sheet of an Excel file (.xlsx) or the rows of a CSV file, one list of cell texts per row. Dates come back as yyyy-MM-dd.</summary>
    IReadOnlyList<IReadOnlyList<string>> Read(string fileName, byte[] content);
}

/// <summary>Makes the Excel file to fill in for an import (the key says which import). Implemented by Infrastructure.</summary>
public interface IImportTemplates
{
    Printing.ExportFile? Build(string key);
}

/// <summary>One thing wrong in an imported file. <see cref="Row"/> counts rows as the person sees them in the file (the first row is 1).</summary>
public sealed record ImportIssue(int Row, string Code);

/// <summary>What an import did. When there are issues, nothing was imported (an import is all or nothing).</summary>
public sealed record ImportResult(int Imported, int Skipped, IReadOnlyList<ImportIssue> Issues);

/// <summary>Reading messy human-made files (brief section 10.2): column names in English or Arabic, any common date style, any number style.</summary>
public static partial class ImportParsing
{
    /// <summary>The index of the first header that is one of the names, or -1. Names are compared without case, spaces or diacritics.</summary>
    public static int FindColumn(IReadOnlyList<string> header, params string[] names)
    {
        var wanted = names.Select(Clean).ToHashSet();
        for (var i = 0; i < header.Count; i++)
        {
            if (wanted.Contains(Clean(header[i])))
                return i;
        }

        return -1;
    }

    public static string Cell(IReadOnlyList<string> row, int column) => column >= 0 && column < row.Count ? row[column].Trim() : "";

    private static string Clean(string text) =>
        ArabicText.Normalize(text).Replace(" ", "").Replace("_", "").Replace("-", "").Replace(".", "");

    /// <summary>A date written as 2026-10-06, 06/10/2026, 6-10-2026 or 06.10.2026 (day first when it could be either).</summary>
    public static DateOnly? ParseDate(string text)
    {
        text = ArabicText.Digits(text.Trim());
        if (text.Length == 0)
            return null;

        string[] formats =
        [
            "yyyy-MM-dd", "yyyy/MM/dd", "yyyy-M-d", "yyyy/M/d", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy",
            "dd/MM/yy", "d/M/yy",
        ];
        return DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    /// <summary>
    /// A money amount as people write it: 1,234.50 or 1.234,50 or 1 234,50, with a currency sign, Arabic digits, a minus in front or
    /// behind, or brackets for a negative. Returns null when it is not a number.
    /// </summary>
    public static decimal? ParseAmount(string text)
    {
        text = ArabicText.Digits(text.Trim()).Replace("٫", ".").Replace("٬", "").Replace(" ", " ");
        if (text.Length == 0)
            return null;

        var negative = text.StartsWith('-') || text.EndsWith('-') || (text.StartsWith('(') && text.EndsWith(')'));
        text = NotNumber().Replace(text, "");
        if (text.Length == 0)
            return null;

        var comma = text.LastIndexOf(',');
        var dot = text.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
        {
            // Both present: the one that comes last is the decimal sign, the other groups thousands.
            text = comma > dot ? text.Replace(".", "").Replace(',', '.') : text.Replace(",", "");
        }
        else if (comma >= 0)
        {
            // Only commas: 1,234 and 1,234,567 group thousands; 12,5 and 1234,50 are decimals.
            var groups = text.Split(',');
            text = groups.Length > 2 || groups[^1].Length == 3 ? text.Replace(",", "") : text.Replace(',', '.');
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return null;
        return negative ? -value : value;
    }

    [GeneratedRegex(@"[^0-9.,]")]
    private static partial Regex NotNumber();
}
