using System.Net;
using System.Net.Http.Json;
using Baba.Api.Security;

namespace Baba.Api.Tests;

/// <summary>The desktop API must only answer the app on this computer (see BabaApiOptions.AccessToken).</summary>
public class SecurityTests : ApiFixture
{
    private const string SecretToken = "launch-secret-123";

    protected override string? Token => SecretToken;

    private HttpRequestMessage Get(string path, Action<HttpRequestMessage>? change = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        change?.Invoke(request);
        return request;
    }

    [Fact]
    public async Task Api_requests_without_the_token_are_refused()
    {
        var response = await Client.SendAsync(Get("/api/host"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_token_is_refused()
    {
        var response = await Client.SendAsync(Get("/api/host", r => r.Headers.Add(AccessTokenMiddleware.HeaderName, "nope")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_token_in_a_header_or_in_the_cookie_is_accepted()
    {
        var byHeader = await Client.SendAsync(Get("/api/host", r => r.Headers.Add(AccessTokenMiddleware.HeaderName, SecretToken)));
        var byCookie = await Client.SendAsync(Get("/api/host", r => r.Headers.Add("Cookie", $"{AccessTokenMiddleware.CookieName}={SecretToken}")));

        Assert.Equal(HttpStatusCode.OK, byHeader.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byCookie.StatusCode);
    }

    [Fact]
    public async Task The_company_cannot_be_opened_or_read_without_the_token()
    {
        var create = await Client.PostAsJsonAsync("/api/company/create", new { path = NewPath(), password = Password });
        var read = await Client.SendAsync(Get("/api/company"));

        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.False(File.Exists(NewPath()));
    }

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("192.168.1.20")]
    public async Task Requests_addressed_to_another_host_name_are_refused_even_with_the_token(string host)
    {
        var request = Get("/api/host", r =>
        {
            r.Headers.Host = host;
            r.Headers.Add(AccessTokenMiddleware.HeaderName, SecretToken);
        });

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("localhost:5173")]
    public async Task Local_host_names_are_accepted(string host)
    {
        var request = Get("/api/host", r =>
        {
            r.Headers.Host = host;
            r.Headers.Add(AccessTokenMiddleware.HeaderName, SecretToken);
        });

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Changes_from_another_web_origin_are_refused_even_with_the_token()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/close");
        request.Headers.Add(AccessTokenMiddleware.HeaderName, SecretToken);
        request.Headers.Add("Origin", "https://evil.example.com");

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:5173")]
    public async Task Changes_from_the_app_itself_or_the_dev_server_are_accepted(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/close");
        request.Headers.Add(AccessTokenMiddleware.HeaderName, SecretToken);
        request.Headers.Add("Origin", origin);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
