using System.Net;
using System.Net.Http.Json;
using Baba.Api.Contracts;
using Baba.Api.Errors;
using Baba.Application.Companies;

namespace Baba.Api.Tests;

public class CompanyApiTests : ApiFixture
{
    [Fact]
    public async Task Creating_a_company_returns_it_and_makes_it_the_open_company()
    {
        var response = await CreateCompanyAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await ReadAsync<CompanyInfo>(response);
        Assert.Equal("Al Noor", created.NameEn);
        Assert.Equal("KW", created.CountryCode);
        Assert.True(File.Exists(NewPath()));

        var current = await ReadAsync<CompanyInfo>(await Client.GetAsync("/api/company"));
        Assert.Equal(created.Id, current.Id);
    }

    [Fact]
    public async Task No_open_company_answers_no_content()
    {
        var response = await Client.GetAsync("/api/company");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task The_full_cycle_create_close_reopen_works_and_remembers_the_file()
    {
        await CreateCompanyAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsync("/api/company/close", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.GetAsync("/api/company")).StatusCode);

        var reopened = await Client.PostAsJsonAsync("/api/company/open", new OpenCompanyRequest(NewPath(), Password));

        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.Equal("شركة النور", (await ReadAsync<CompanyInfo>(reopened)).NameAr);

        var recent = await ReadAsync<List<RecentFileDto>>(await Client.GetAsync("/api/recent-files"));
        var entry = Assert.Single(recent);
        Assert.Equal("Company", entry.Name);
        Assert.True(entry.Exists);
    }

    [Fact]
    public async Task A_wrong_password_is_forbidden_with_a_translatable_problem_code()
    {
        await CreateCompanyAsync();
        await Client.PostAsync("/api/company/close", null);

        var response = await Client.PostAsJsonAsync("/api/company/open", new OpenCompanyRequest(NewPath(), "wrong-password"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(nameof(CompanyFileProblem.WrongPasswordOrNotABabaFile), (await ReadAsync<ApiProblem>(response)).Problem);
    }

    [Fact]
    public async Task A_missing_file_is_not_found_and_a_second_open_company_conflicts()
    {
        var missing = await Client.PostAsJsonAsync("/api/company/open", new OpenCompanyRequest(NewPath("nope"), Password));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False(File.Exists(NewPath("nope")));

        await CreateCompanyAsync();
        var second = await Client.PostAsJsonAsync("/api/company/open", new OpenCompanyRequest(NewPath(), Password));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("CompanyAlreadyOpen", (await ReadAsync<ApiProblem>(second)).Problem);
    }

    [Fact]
    public async Task Creating_over_an_existing_file_conflicts()
    {
        await CreateCompanyAsync();
        await Client.PostAsync("/api/company/close", null);

        var response = await CreateCompanyAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(nameof(CompanyFileProblem.FileAlreadyExists), (await ReadAsync<ApiProblem>(response)).Problem);
    }

    [Fact]
    public async Task Invalid_input_returns_every_problem_with_field_and_code()
    {
        var bad = ValidCompany() with { NameAr = "", NameEn = "", FiscalYearStartMonth = 13, CountryCode = "ZZ" };

        var response = await CreateCompanyAsync(company: bad, password: "123");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync<ApiProblem>(response);
        Assert.Equal("Validation", problem.Problem);
        var codes = problem.Issues!.Select(i => i.Code).ToList();
        Assert.Contains("password.too-short", codes);
        Assert.Contains("name.required", codes);
        Assert.Contains("country.unknown", codes);
        Assert.Contains("fiscal-year.month-invalid", codes);
        Assert.False(File.Exists(NewPath()), "Nothing is created when the input is invalid.");
    }

    [Fact]
    public async Task A_country_that_requires_a_registration_number_is_validated_from_the_pack_data()
    {
        var company = ValidCompany("SA", "SAR");

        var missing = await CreateCompanyAsync(company: company);
        var issues = (await ReadAsync<ApiProblem>(missing)).Issues!;
        Assert.Contains(issues, i => i.Code == "tax.required" && i.Field == "taxNumbers.vat-number");

        var valid = await CreateCompanyAsync(company: company with
        {
            TaxNumbers = new Dictionary<string, string> { ["vat-number"] = "300123456789003" },
        });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Fact]
    public async Task A_backup_is_written_where_asked_and_opens_with_the_same_password()
    {
        await CreateCompanyAsync();
        var backup = Path.Combine(Folder, "backups", "copy.baba");

        var response = await Client.PostAsJsonAsync("/api/company/backup", new BackupRequest(backup));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await Client.PostAsync("/api/company/close", null);
        var opened = await Client.PostAsJsonAsync("/api/company/open", new OpenCompanyRequest(backup, Password));
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task Backup_without_an_open_company_conflicts()
    {
        var response = await Client.PostAsJsonAsync("/api/company/backup", new BackupRequest(NewPath("b")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(nameof(CompanyFileProblem.NoCompanyOpen), (await ReadAsync<ApiProblem>(response)).Problem);
    }

    [Fact]
    public async Task Removing_a_recent_file_forgets_it_without_touching_the_file()
    {
        await CreateCompanyAsync();

        await Client.PostAsJsonAsync("/api/recent-files/remove", new RemoveRecentFileRequest(NewPath()));

        Assert.Empty(await ReadAsync<List<RecentFileDto>>(await Client.GetAsync("/api/recent-files")));
        Assert.True(File.Exists(NewPath()));
    }
}
