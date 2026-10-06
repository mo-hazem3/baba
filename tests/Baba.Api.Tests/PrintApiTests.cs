using System.Net;
using System.Net.Http.Json;
using Baba.Application.Printing;

namespace Baba.Api.Tests;

public class PrintWithoutRendererApiTests : ApiFixture
{
    [Fact]
    public async Task A_host_that_cannot_make_pdfs_answers_not_implemented()
    {
        var response = await Client.PostAsJsonAsync("/api/print/test-page", new { layout = "Arabic" });

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }
}

public class PrintApiTests : ApiFixture
{
    private readonly FakeRenderer _renderer = new();

    protected override IPdfRenderer? PdfRenderer => _renderer;

    [Theory]
    [InlineData("Arabic", "dir=\"rtl\"")]
    [InlineData("English", "dir=\"ltr\"")]
    [InlineData("Both", "class=\"ar\"")]
    public async Task The_test_page_is_returned_as_a_pdf_for_each_layout(string layout, string expectedInHtml)
    {
        var response = await Client.PostAsJsonAsync("/api/print/test-page", new { layout });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF-fake", System.Text.Encoding.ASCII.GetString(await response.Content.ReadAsByteArrayAsync()));
        Assert.Contains(expectedInHtml, _renderer.Html);
    }

    [Fact]
    public async Task The_page_is_made_for_the_open_company()
    {
        await CreateCompanyAsync();

        await Client.PostAsJsonAsync("/api/print/test-page", new { layout = "Both" });

        Assert.Contains("Al Noor", _renderer.Html);
        Assert.Contains("شركة النور", _renderer.Html);
        Assert.Contains("1,249.500", _renderer.Html); // the company's currency has three decimals
    }

    [Fact]
    public async Task An_unknown_layout_is_a_bad_request_and_makes_nothing()
    {
        var response = await Client.PostAsJsonAsync("/api/print/test-page", new { layout = "Klingon" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_renderer.Html);
    }

    private sealed class FakeRenderer : IPdfRenderer
    {
        public string? Html { get; private set; }

        public Task<byte[]> RenderAsync(string html, PdfOptions options, CancellationToken cancellationToken = default)
        {
            Html = html;
            return Task.FromResult(System.Text.Encoding.ASCII.GetBytes("%PDF-fake"));
        }
    }
}
