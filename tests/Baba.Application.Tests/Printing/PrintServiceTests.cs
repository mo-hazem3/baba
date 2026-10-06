using Baba.Application.Companies;
using Baba.Application.Printing;

namespace Baba.Application.Tests.Printing;

public class PrintServiceTests
{
    private sealed class FakeRenderer : IPdfRenderer
    {
        public string? Html { get; private set; }
        public PdfOptions? Options { get; private set; }

        public Task<byte[]> RenderAsync(string html, PdfOptions options, CancellationToken cancellationToken = default)
        {
            (Html, Options) = (html, options);
            return Task.FromResult<byte[]>([37, 80, 68, 70]); // "%PDF"
        }
    }

    private sealed class FakeFonts : IPrintFonts
    {
        public string FontFamily => "Test Font";
        public string CssFontFaces() => "/* test fonts */";
    }

    private sealed class FakeFiles(CompanyInfo? current) : ICompanyFiles
    {
        public CompanyInfo? Current { get; } = current;
        public Task<CompanyInfo> CreateAsync(string path, string password, NewCompanyData company, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CompanyInfo> OpenAsync(string path, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Close() { }
        public Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static CompanyInfo Company(string currency) =>
        new(Guid.NewGuid(), "شركة الأمل", "Al Amal", "XX", currency, 1, 2026, [], "C:/x.baba");

    [Fact]
    public async Task The_test_page_uses_the_open_company_and_its_currency_decimals()
    {
        var renderer = new FakeRenderer();
        var service = new PrintService(renderer, new FakeFonts(), new FakeFiles(Company("KWD")), new FixedClock());

        var pdf = await service.RenderTestPageAsync(PrintLayout.Both);

        Assert.Equal([37, 80, 68, 70], pdf);
        Assert.Contains("Al Amal", renderer.Html);
        Assert.Contains("شركة الأمل", renderer.Html);
        Assert.Contains("1,249.500", renderer.Html); // dinars have three decimals
        Assert.Contains("/* test fonts */", renderer.Html);
        Assert.Contains("06/10/2026", renderer.Html);
        Assert.NotNull(renderer.Options);
    }

    [Fact]
    public async Task Without_a_company_a_sample_company_is_printed()
    {
        var renderer = new FakeRenderer();
        var service = new PrintService(renderer, new FakeFonts(), new FakeFiles(null), new FixedClock());

        await service.RenderTestPageAsync(PrintLayout.English);

        Assert.Contains("Sample Company", renderer.Html);
        Assert.Contains("1,249.50", renderer.Html);
    }

    [Fact]
    public async Task A_currency_missing_from_the_catalog_falls_back_to_a_sample_currency()
    {
        var renderer = new FakeRenderer();
        var service = new PrintService(renderer, new FakeFonts(), new FakeFiles(Company("ZZZ")), new FixedClock());

        await service.RenderTestPageAsync(PrintLayout.English);

        Assert.Contains("1,249.50", renderer.Html);
    }

    [Fact]
    public async Task A_host_without_a_pdf_renderer_says_so()
    {
        var service = new PrintService(null, new FakeFonts(), new FakeFiles(null), new FixedClock());

        Assert.False(service.IsAvailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenderTestPageAsync(PrintLayout.Arabic));
    }
}

public class NumberTextTests
{
    [Theory]
    [InlineData(1234.5, 2, false, "1,234.50")]
    [InlineData(1234.5, 3, false, "1,234.500")]
    [InlineData(-35.5, 2, false, "-35.50")]
    [InlineData(1234.5, 3, true, "١٬٢٣٤٫٥٠٠")]
    [InlineData(-35.5, 2, true, "-٣٥٫٥٠")]
    [InlineData(0, 0, false, "0")]
    public void Amounts_use_the_currency_decimals_and_digit_style(double value, int decimals, bool arabicIndic, string expected) =>
        Assert.Equal(expected, NumberText.Amount((decimal)value, decimals, arabicIndic));

    [Fact]
    public void Integers_and_text_convert_digits_only()
    {
        Assert.Equal("١٠", NumberText.Integer(10, arabicIndic: true));
        Assert.Equal("10", NumberText.Integer(10, arabicIndic: false));
        Assert.Equal("TEST-٠٠٠١", NumberText.ToArabicIndic("TEST-0001"));
    }
}
