using System.Text;
using Baba.Infrastructure.Printing;

namespace Baba.Infrastructure.Tests.Accounting;

public class TabularReaderTests
{
    private static IReadOnlyList<IReadOnlyList<string>> Csv(string text, bool bom = false) =>
        new TabularReader().Read("file.csv", (bom ? Encoding.UTF8.GetPreamble() : []).Concat(Encoding.UTF8.GetBytes(text)).ToArray());

    [Fact]
    public void Commas_quotes_and_line_breaks_inside_quotes_are_handled()
    {
        var rows = Csv("a,b,c\r\n1,\"two, with comma\",\"say \"\"hi\"\"\"\r\n\"line\nbreak\",x,\r\n");

        Assert.Equal(3, rows.Count);
        Assert.Equal(["1", "two, with comma", "say \"hi\""], rows[1]);
        Assert.Equal(["line\nbreak", "x", ""], rows[2]);
    }

    [Theory]
    [InlineData("a;b;c\n1;2;3")]
    [InlineData("a\tb\tc\n1\t2\t3")]
    [InlineData("a,b,c\n1,2,3")]
    public void The_separator_is_found_from_the_first_line(string text)
    {
        var rows = Csv(text);

        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["1", "2", "3"], rows[1]);
    }

    [Fact]
    public void A_byte_order_mark_and_a_missing_final_line_break_do_no_harm()
    {
        var rows = Csv("Date,Amount\n2026-10-01,5", bom: true);

        Assert.Equal("Date", rows[0][0]);
        Assert.Equal(["2026-10-01", "5"], rows[1]);
    }

    [Fact]
    public void An_empty_file_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => new TabularReader().Read("x.csv", []));
    }

    [Fact]
    public void A_file_that_looks_like_excel_but_is_not_is_refused()
    {
        var notExcel = Encoding.ASCII.GetBytes("PK this is not a zip file at all");

        Assert.Throws<InvalidDataException>(() => new TabularReader().Read("x.xlsx", notExcel));
    }
}
