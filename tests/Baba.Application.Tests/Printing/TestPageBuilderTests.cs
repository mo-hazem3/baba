using System.Text.RegularExpressions;
using Baba.Application.Printing;
using Baba.Domain;

namespace Baba.Application.Tests.Printing;

public class TestPageBuilderTests
{
    private static readonly Currency TwoDecimals = new("TWO", 2);
    private static readonly Currency ThreeDecimals = new("TRI", 3);

    private static string Build(PrintLayout layout, Currency? currency = null, string nameEn = "Al Noor", string nameAr = "النور") =>
        TestPageBuilder.Build(layout, "/* fonts */", "Test Font", nameAr, nameEn, currency ?? TwoDecimals, new DateOnly(2026, 10, 6));

    private static bool HasArabic(string text) => Regex.IsMatch(text, "[؀-ۿ]");

    [Fact]
    public void The_arabic_page_is_right_to_left_arabic_with_arabic_digits()
    {
        var html = Build(PrintLayout.Arabic);

        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", html);
        Assert.Contains("فاتورة مبيعات", html);
        Assert.Contains("النور", html);
        Assert.Contains("٨٥٠٫٠٠", html); // 850.00 in Arabic-Indic digits
        Assert.DoesNotContain("Sales invoice", html);
        Assert.DoesNotContain("Al Noor", html);
    }

    [Fact]
    public void The_english_page_is_left_to_right_english_with_western_digits()
    {
        var html = Build(PrintLayout.English);

        Assert.Contains("<html lang=\"en\" dir=\"ltr\">", html);
        Assert.Contains("Sales invoice", html);
        Assert.Contains("Al Noor", html);
        Assert.Contains("850.00", html);
        Assert.False(HasArabic(html), "The English page should contain no Arabic text.");
    }

    [Fact]
    public void The_bilingual_page_has_both_languages_with_the_arabic_marked_right_to_left()
    {
        var html = Build(PrintLayout.Both);

        Assert.Contains("Sales invoice", html);
        Assert.Contains("فاتورة مبيعات", html);
        Assert.Contains("<span class=\"ar\" lang=\"ar\" dir=\"rtl\">فاتورة مبيعات</span>", html);
        Assert.Contains("Al Noor", html);
        Assert.Contains("النور", html);
    }

    [Fact]
    public void Amounts_follow_the_currency_decimals_and_the_total_adds_up()
    {
        // 850 + 10 x 35.5 - 35.5 + 80 = 1,249.50
        var two = Build(PrintLayout.English, TwoDecimals);
        var three = Build(PrintLayout.English, ThreeDecimals);

        Assert.Contains("1,249.50", two);
        Assert.Contains("TWO", two);
        Assert.Contains("1,249.500", three);
        Assert.Contains("850.000", three);
    }

    [Fact]
    public void Negative_amounts_get_a_minus_sign_and_the_red_style()
    {
        var html = Build(PrintLayout.English);

        Assert.Contains("class=\"num neg\"><bdi dir=\"ltr\">-35.50</bdi>", html);
    }

    [Fact]
    public void Company_names_cannot_inject_html()
    {
        var html = Build(PrintLayout.English, nameEn: "<script>alert(1)</script> & Co");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; Co", html);
    }

    [Fact]
    public void The_page_uses_the_given_fonts_and_the_invoice_date()
    {
        var html = Build(PrintLayout.English);

        Assert.Contains("/* fonts */", html);
        Assert.Contains("font-family: Test Font", html);
        Assert.Contains("06/10/2026", html);
    }
}
