using Baba.Application.Importing;

namespace Baba.Application.Tests.Importing;

public class ImportParsingTests
{
    [Theory]
    [InlineData("1234.5", 1234.5)]
    [InlineData("1,234.50", 1234.5)]
    [InlineData("1.234,50", 1234.5)]
    [InlineData("1,234,567", 1234567)]
    [InlineData("12,5", 12.5)]
    [InlineData("1234,50", 1234.5)]
    [InlineData("1 234,50", 1234.5)]
    [InlineData("-1,000.000", -1000)]
    [InlineData("1000-", -1000)]
    [InlineData("(250.00)", -250)]
    [InlineData("KWD 75.250", 75.25)]
    [InlineData("+40", 40)]
    [InlineData("١٢٣٤٫٥", 1234.5)]
    [InlineData("١٠٬٠٠٠", 10000)]
    public void Amounts_are_read_the_way_people_write_them(string text, double expected) =>
        Assert.Equal((decimal)expected, ImportParsing.ParseAmount(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-")]
    public void Text_that_is_not_a_number_is_not_an_amount(string text) => Assert.Null(ImportParsing.ParseAmount(text));

    [Theory]
    [InlineData("2026-10-06")]
    [InlineData("2026/10/06")]
    [InlineData("06/10/2026")]
    [InlineData("6/10/2026")]
    [InlineData("06-10-2026")]
    [InlineData("06.10.2026")]
    [InlineData("٠٦/١٠/٢٠٢٦")]
    [InlineData("06/10/26")]
    public void Dates_are_read_in_the_common_styles_day_first(string text) =>
        Assert.Equal(new DateOnly(2026, 10, 6), ImportParsing.ParseDate(text));

    [Theory]
    [InlineData("")]
    [InlineData("tomorrow")]
    [InlineData("31/02/2026")]
    [InlineData("13/13/2026")]
    public void Anything_else_is_not_a_date(string text) => Assert.Null(ImportParsing.ParseDate(text));

    [Fact]
    public void Column_names_are_found_whatever_the_case_spacing_or_arabic_spelling()
    {
        string[] header = ["  Transaction Date ", "Description", "المبلغ", "إيداع"];

        Assert.Equal(0, ImportParsing.FindColumn(header, "transaction date"));
        Assert.Equal(1, ImportParsing.FindColumn(header, "DESCRIPTION"));
        Assert.Equal(2, ImportParsing.FindColumn(header, "amount", "المبلغ"));
        Assert.Equal(3, ImportParsing.FindColumn(header, "ايداع")); // with or without the hamza
        Assert.Equal(-1, ImportParsing.FindColumn(header, "balance"));
    }

    [Fact]
    public void Arabic_text_is_normalised_for_matching()
    {
        Assert.Equal(ArabicText.Normalize("أحمد"), ArabicText.Normalize("احمد"));
        Assert.Equal(ArabicText.Normalize("مدرسة"), ArabicText.Normalize("مدرسه"));
        Assert.Equal("2026", ArabicText.Digits("٢٠٢٦"));
    }
}
