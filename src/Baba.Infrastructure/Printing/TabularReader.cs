using System.Globalization;
using System.Text;
using Baba.Application.Importing;
using ClosedXML.Excel;

namespace Baba.Infrastructure.Printing;

/// <summary>Reads CSV and Excel (.xlsx) files into plain rows of text, for importing statements, accounts and customers.</summary>
public sealed class TabularReader : ITabularReader
{
    public IReadOnlyList<IReadOnlyList<string>> Read(string fileName, byte[] content)
    {
        if (content.Length == 0)
            throw new InvalidDataException("The file is empty.");

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        // Excel files are zip files; recognise them by their first bytes too, in case the name is wrong.
        var looksLikeZip = content.Length > 3 && content[0] == (byte)'P' && content[1] == (byte)'K';
        return extension == ".xlsx" || looksLikeZip ? ReadWorkbook(content) : ReadCsv(content);
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadWorkbook(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new InvalidDataException("The workbook has no sheets.");
            var used = sheet.RangeUsed();
            if (used is null)
                return [];

            var rows = new List<IReadOnlyList<string>>();
            var firstColumn = used.FirstColumn().ColumnNumber();
            var lastColumn = used.LastColumn().ColumnNumber();
            for (var r = used.FirstRow().RowNumber(); r <= used.LastRow().RowNumber(); r++)
            {
                var cells = new List<string>();
                for (var c = firstColumn; c <= lastColumn; c++)
                    cells.Add(Text(sheet.Cell(r, c)));
                rows.Add(cells);
            }

            return rows;
        }
        catch (Exception e) when (e is not InvalidDataException)
        {
            throw new InvalidDataException("The file is not a readable Excel workbook.", e);
        }
    }

    private static string Text(IXLCell cell) => cell.DataType switch
    {
        XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        XLDataType.Number => ((decimal)cell.GetDouble()).ToString(CultureInfo.InvariantCulture),
        _ => cell.GetString(),
    };

    /// <summary>CSV with quotes, commas, semicolons or tabs, and any line ending. A byte order mark is ignored.</summary>
    internal static IReadOnlyList<IReadOnlyList<string>> ReadCsv(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content).TrimStart('﻿');
        var firstLine = text.Split('\n', 2)[0];
        var delimiter = new[] { ',', ';', '\t' }.MaxBy(d => firstLine.Count(ch => ch == d));
        if (firstLine.Count(ch => ch == delimiter) == 0)
            delimiter = ',';

        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        void EndCell()
        {
            row.Add(cell.ToString());
            cell.Clear();
        }

        void EndRow()
        {
            EndCell();
            rows.Add(row);
            row = [];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(ch);
                }
            }
            else if (ch == '"' && cell.Length == 0)
            {
                quoted = true;
            }
            else if (ch == delimiter)
            {
                EndCell();
            }
            else if (ch == '\r')
            {
                // The \n that follows ends the row.
            }
            else if (ch == '\n')
            {
                EndRow();
            }
            else
            {
                cell.Append(ch);
            }
        }

        if (cell.Length > 0 || row.Count > 0)
            EndRow();
        return rows;
    }
}
