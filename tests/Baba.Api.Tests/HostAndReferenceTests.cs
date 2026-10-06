using System.Net;
using System.Net.Http.Json;
using Baba.Api.Contracts;
using Baba.Application.Abstractions;
using Baba.Localization;

namespace Baba.Api.Tests;

public class HostAndReferenceTests : ApiFixture
{
    [Fact]
    public async Task Countries_come_from_the_installed_packs_with_registration_rules_and_enums_as_text()
    {
        var countries = await ReadAsync<List<CountryDto>>(await Client.GetAsync("/api/countries"));

        Assert.Equal(CountryPackRegistry.Discover().All.Count, countries.Count);
        var kuwait = countries.Single(c => c.Code == "KW");
        Assert.Equal(3, kuwait.Currency.MinorUnits);
        Assert.False(kuwait.Capabilities.HasTaxCodes);
        Assert.Empty(kuwait.TaxRegistration);

        var saudi = countries.Single(c => c.Code == "SA");
        Assert.Contains(CalendarKind.HijriUmmAlQura, saudi.Calendars);
        Assert.Contains(DayOfWeek.Friday, saudi.DefaultWeekend);
        Assert.Contains(saudi.TaxRegistration, r => r.Key == "vat-number" && r.RequiredForCompany);
        Assert.DoesNotContain(countries, c => c.Code == "XX"); // the template pack is never offered

        var raw = await Client.GetStringAsync("/api/countries");
        Assert.Contains("\"Friday\"", raw);
        Assert.Contains("\"HijriUmmAlQura\"", raw);
    }

    [Fact]
    public async Task Currencies_and_modules_are_listed()
    {
        var currencies = await ReadAsync<List<CurrencyDto>>(await Client.GetAsync("/api/currencies"));
        var modules = await ReadAsync<List<ModuleDto>>(await Client.GetAsync("/api/modules"));

        Assert.Equal(3, currencies.Single(c => c.Code == "KWD").MinorUnits);
        Assert.Equal(0, currencies.Single(c => c.Code == "JPY").MinorUnits);
        Assert.Contains(modules, m => m.Key == "inventory");
    }

    [Fact]
    public async Task Without_a_desktop_host_there_are_no_file_dialogs()
    {
        var host = await ReadAsync<HostInfo>(await Client.GetAsync("/api/host"));
        var dialog = await Client.PostAsync("/api/dialogs/open-company-file", null);

        Assert.False(host.FileDialogs);
        Assert.Equal(HttpStatusCode.NotImplemented, dialog.StatusCode);
    }

    [Fact]
    public async Task A_startup_file_is_handed_over_once()
    {
        ((StartupRequest)App.Services.GetService(typeof(StartupRequest))!).OpenPath = @"C:\Books\Noor.baba";

        var first = await ReadAsync<StartupInfo>(await Client.GetAsync("/api/startup"));
        var second = await ReadAsync<StartupInfo>(await Client.GetAsync("/api/startup"));

        Assert.Equal(@"C:\Books\Noor.baba", first.OpenPath);
        Assert.Null(second.OpenPath);
    }

    [Fact]
    public async Task The_openapi_document_describes_the_api()
    {
        var document = await Client.GetStringAsync("/api/openapi/v1.json");

        Assert.Contains("\"/api/company/create\"", document);
        Assert.Contains("\"CreateCompany\"", document);
    }
}

public class DesktopHostTests : ApiFixture
{
    private readonly FakeDialogs _dialogs = new();

    protected override IFileDialogs? FileDialogs => _dialogs;

    [Fact]
    public async Task With_a_desktop_host_the_dialogs_are_offered_and_return_the_chosen_path()
    {
        _dialogs.OpenResult = @"C:\Books\Noor.baba";
        _dialogs.SaveResult = null;

        var host = await ReadAsync<HostInfo>(await Client.GetAsync("/api/host"));
        var open = await ReadAsync<PathChoice>(await Client.PostAsync("/api/dialogs/open-company-file", null));
        var save = await ReadAsync<PathChoice>(await Client.PostAsJsonAsync("/api/dialogs/save-company-file", new SaveDialogRequest("Noor.baba")));

        Assert.True(host.FileDialogs);
        Assert.Equal(@"C:\Books\Noor.baba", open.Path);
        Assert.Null(save.Path); // the user cancelled
        Assert.Equal("Noor.baba", _dialogs.SuggestedName);
    }

    private sealed class FakeDialogs : IFileDialogs
    {
        public string? OpenResult { get; set; }
        public string? SaveResult { get; set; }
        public string? SuggestedName { get; private set; }

        public Task<string?> PickCompanyFileToOpenAsync(CancellationToken cancellationToken = default) => Task.FromResult(OpenResult);

        public Task<string?> PickCompanyFileToSaveAsync(string suggestedFileName, CancellationToken cancellationToken = default)
        {
            SuggestedName = suggestedFileName;
            return Task.FromResult(SaveResult);
        }
    }
}
