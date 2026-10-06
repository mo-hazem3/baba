using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baba.Api.Contracts;
using Baba.Application.Abstractions;
using Baba.Application.Companies;
using Baba.Application.Printing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Baba.Api.Tests;

/// <summary>Runs the real API on an in-memory server with a temp folder, so tests never open a port or touch real data.</summary>
public abstract class ApiFixture : IAsyncLifetime
{
    protected const string Password = "correct-horse";

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected readonly string Folder = Path.Combine(Path.GetTempPath(), "baba-api-tests-" + Guid.NewGuid().ToString("N"));
    private WebApplication? _app;

    protected HttpClient Client { get; private set; } = null!;
    protected WebApplication App => _app!;

    /// <summary>Set to null in a test class to switch the token check off.</summary>
    protected virtual string? Token => null;
    protected virtual IFileDialogs? FileDialogs => null;
    protected virtual IPdfRenderer? PdfRenderer => null;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Folder);
        var options = new BabaApiOptions
        {
            AccessToken = Token,
            RecentFilesPath = Path.Combine(Folder, "recent.json"),
            AdditionalAllowedOrigins = ["http://localhost:5173"],
        };

        _app = BabaApi.Create(options, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<ICurrentUser>(new TestUser());
            if (FileDialogs is { } dialogs)
                builder.Services.AddSingleton(dialogs);
            if (PdfRenderer is { } renderer)
                builder.Services.AddSingleton(renderer);
        });
        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _app!.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { /* best effort */ }
    }

    protected string NewPath(string name = "Company") => Path.Combine(Folder, name + ".baba");

    protected static NewCompanyRequest ValidCompany(string country = "KW", string currency = "KWD") => new(
        NameAr: "شركة النور",
        NameEn: "Al Noor",
        CountryCode: country,
        BaseCurrencyCode: currency,
        FiscalYearStartMonth: 1,
        FirstFiscalYear: 2026,
        TaxNumbers: new Dictionary<string, string>(),
        Address: null,
        ChartTemplateKey: "default",
        EnabledModules: ["bank-cash"]);

    protected Task<HttpResponseMessage> CreateCompanyAsync(string? path = null, NewCompanyRequest? company = null, string password = Password) =>
        Client.PostAsJsonAsync("/api/company/create", new CreateCompanyRequest(path ?? NewPath(), password, company ?? ValidCompany()));

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    private sealed class TestUser : ICurrentUser
    {
        public string UserId => "api-tester";
    }
}
