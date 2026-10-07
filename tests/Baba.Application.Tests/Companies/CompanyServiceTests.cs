using Baba.Application.Companies;
using Baba.Localization;

namespace Baba.Application.Tests.Companies;

public class CompanyServiceTests
{
    private readonly FakeCompanyFiles _files = new();
    private readonly CountryPackRegistry _packs = new([new TestPack()]);
    private readonly CompanyService _service;

    public CompanyServiceTests() => _service = new CompanyService(_files, _packs);

    /// <summary>A valid request for the test country, whose registration number is exactly five digits.</summary>
    private static NewCompanyRequest ValidRequest(Func<NewCompanyRequest, NewCompanyRequest>? change = null)
    {
        var request = new NewCompanyRequest(
            NameAr: "شركة النور",
            NameEn: "Al Noor",
            CountryCode: "XX",
            BaseCurrencyCode: "TST",
            FiscalYearStartMonth: 1,
            FirstFiscalYear: 2026,
            TaxNumbers: new Dictionary<string, string> { ["tax-id"] = "12345" },
            Address: null,
            ChartTemplateKey: "default",
            EnabledModules: []);
        return change?.Invoke(request) ?? request;
    }

    private sealed class TestPack : CountryPackBase
    {
        public override CountryIdentity Identity { get; } = new("XX", "Testland", "أرض الاختبار", ["en"]);

        public override CurrencyRules Currency { get; } = new(
            new Baba.Domain.Currency("TST", 2), "Test unit", "وحدة", "Cent", "سنت");

        public override CalendarRules Calendar { get; } = new([CalendarKind.Gregorian], [DayOfWeek.Sunday], 1);

        public override IReadOnlyList<TaxRegistrationRule> TaxRegistration { get; } =
            [new("tax-id", "Tax ID", "الرقم الضريبي", @"^\d{5}$", RequiredForCompany: true)];
    }

    private static ValidationException Invalid(Func<Task> action) =>
        Assert.ThrowsAsync<ValidationException>(action).GetAwaiter().GetResult();

    [Fact]
    public async Task A_valid_request_creates_the_company_with_the_chart_from_the_pack()
    {
        var request = ValidRequest();
        var pack = _packs.Get(request.CountryCode);

        var info = await _service.CreateAsync("C:/x/Company.baba", "123456", request);

        Assert.Equal(request.NameEn, info.NameEn);
        Assert.Equal("C:/x/Company.baba", _files.CreatedPath);
        Assert.Equal(pack.ChartsOfAccounts[0].Accounts.Count, _files.CreatedWith!.Accounts.Count);
    }

    [Fact]
    public async Task A_blank_chart_creates_no_accounts()
    {
        await _service.CreateAsync("C:/x/Company.baba", "123456", ValidRequest(r => r with { ChartTemplateKey = null }));

        Assert.Empty(_files.CreatedWith!.Accounts);
    }

    [Fact]
    public void A_short_password_is_refused_with_a_field_and_code()
    {
        var error = Invalid(() => _service.CreateAsync("C:/x/Company.baba", "12345", ValidRequest()));

        Assert.Contains(new ValidationIssue("password", "password.too-short"), error.Issues);
        Assert.Null(_files.CreatedPath);
    }

    [Fact]
    public void A_missing_path_is_refused()
    {
        var error = Invalid(() => _service.CreateAsync(" ", "123456", ValidRequest()));

        Assert.Contains(new ValidationIssue("path", "path.required"), error.Issues);
    }

    [Fact]
    public void A_relative_path_is_refused_because_it_would_land_in_the_apps_own_folder()
    {
        var error = Invalid(() => _service.CreateAsync("Company.baba", "123456", ValidRequest()));

        Assert.Contains(new ValidationIssue("path", "path.not-absolute"), error.Issues);
        Assert.Null(_files.CreatedPath);
    }

    [Theory]
    [InlineData("C:/Books/Noor", "C:/Books/Noor.baba")]
    [InlineData("C:/Books/Noor.baba", "C:/Books/Noor.baba")]
    [InlineData("C:/Books/Noor.BABA", "C:/Books/Noor.BABA")]
    public async Task Company_files_always_end_in_baba(string typed, string expected)
    {
        await _service.CreateAsync(typed, "123456", ValidRequest());

        Assert.Equal(expected, _files.CreatedPath);
    }

    [Fact]
    public void At_least_one_name_is_required()
    {
        var error = Invalid(() => _service.CreateAsync("C:/x/c.baba", "123456", ValidRequest(r => r with { NameAr = " ", NameEn = "" })));

        Assert.Contains(new ValidationIssue("name", "name.required"), error.Issues);
    }

    [Fact]
    public async Task A_single_name_is_used_for_both_languages()
    {
        await _service.CreateAsync("C:/x/c.baba", "123456", ValidRequest(r => r with { NameAr = "", NameEn = " Only English " }));

        Assert.Equal("Only English", _files.CreatedWith!.Request.NameAr);
        Assert.Equal("Only English", _files.CreatedWith.Request.NameEn);
    }

    [Fact]
    public void An_unknown_country_is_refused()
    {
        var error = Invalid(() => _service.CreateAsync("C:/x/c.baba", "123456", ValidRequest(r => r with { CountryCode = "ZZ" })));

        Assert.Contains(new ValidationIssue("country", "country.unknown"), error.Issues);
    }

    [Fact]
    public async Task Any_catalog_currency_is_allowed_and_codes_are_normalised()
    {
        await _service.CreateAsync("C:/x/c.baba", "123456", ValidRequest(r => r with { BaseCurrencyCode = "usd" }));

        Assert.Equal("USD", _files.CreatedWith!.Request.BaseCurrencyCode);
    }

    [Fact]
    public void An_unknown_currency_is_refused()
    {
        var error = Invalid(() => _service.CreateAsync("C:/x/c.baba", "123456", ValidRequest(r => r with { BaseCurrencyCode = "ZZZ" })));

        Assert.Contains(new ValidationIssue("currency", "currency.unknown"), error.Issues);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void The_fiscal_year_month_must_be_1_to_12(int month)
    {
        var error = Invalid(() => _service.CreateAsync("C:/x/c.baba", "123456", ValidRequest(r => r with { FiscalYearStartMonth = month })));

        Assert.Contains(new ValidationIssue("fiscalYearStartMonth", "fiscal-year.month-invalid"), error.Issues);
    }

    [Fact]
    public void An_unknown_module_or_chart_is_refused()
    {
        var error = Invalid(() => _service.CreateAsync("C:/x/c.baba", "123456",
            ValidRequest(r => r with { EnabledModules = ["flying-carpets"], ChartTemplateKey = "nope" })));

        Assert.Contains(new ValidationIssue("modules", "module.unknown"), error.Issues);
        Assert.Contains(new ValidationIssue("chartTemplateKey", "chart.unknown"), error.Issues);
    }

    [Fact]
    public void A_required_registration_number_must_be_given_and_match_the_pack_format()
    {
        var request = ValidRequest();
        var pack = _packs.Get(request.CountryCode);
        var rule = pack.TaxRegistration.First(r => r.RequiredForCompany);

        var missing = Invalid(() => _service.CreateAsync("C:/x/c.baba", "123456", request with { TaxNumbers = new Dictionary<string, string>() }));
        Assert.Contains(new ValidationIssue($"taxNumbers.{rule.Key}", "tax.required"), missing.Issues);

        var wrong = Invalid(() => _service.CreateAsync("C:/x/c.baba", "123456",
            request with { TaxNumbers = new Dictionary<string, string> { [rule.Key] = "not-a-number" } }));
        Assert.Contains(new ValidationIssue($"taxNumbers.{rule.Key}", "tax.invalid"), wrong.Issues);

        var unknown = Invalid(() => _service.CreateAsync("C:/x/c.baba", "123456",
            request with { TaxNumbers = new Dictionary<string, string>(request.TaxNumbers) { ["made-up"] = "1" } }));
        Assert.Contains(new ValidationIssue("taxNumbers.made-up", "tax.unknown"), unknown.Issues);
    }

    [Fact]
    public void All_problems_are_reported_together()
    {
        var error = Invalid(() => _service.CreateAsync("", "1", ValidRequest(r => r with { NameAr = "", NameEn = "", FiscalYearStartMonth = 99 })));

        Assert.True(error.Issues.Count >= 4);
    }

    // ---- Restoring a backup ----

    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "baba-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void A_backup_is_restored_by_copying_it_to_a_new_company_file_and_the_backup_is_left_alone()
    {
        var folder = TempFolder();
        try
        {
            var backup = Path.Combine(folder, "Al Noor.before-close-2026.baba");
            File.WriteAllBytes(backup, [1, 2, 3, 4]);

            var restored = _service.Restore(backup, Path.Combine(folder, "Al Noor restored")); // the .baba ending is added

            Assert.Equal(Path.Combine(folder, "Al Noor restored.baba"), restored);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(restored));
            Assert.True(File.Exists(backup));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Restoring_never_overwrites_and_explains_what_is_wrong()
    {
        var folder = TempFolder();
        try
        {
            var backup = Path.Combine(folder, "backup.baba");
            var taken = Path.Combine(folder, "taken.baba");
            File.WriteAllBytes(backup, [1]);
            File.WriteAllBytes(taken, [9]);

            Assert.Contains(Assert.Throws<ValidationException>(() => _service.Restore(Path.Combine(folder, "missing.baba"), Path.Combine(folder, "new.baba"))).Issues, i => i.Code == "restore.backup-missing");
            Assert.Contains(Assert.Throws<ValidationException>(() => _service.Restore(backup, "relative.baba")).Issues, i => i.Code == "path.not-absolute");
            Assert.Contains(Assert.Throws<ValidationException>(() => _service.Restore(backup, taken)).Issues, i => i.Code == "restore.destination-exists");
            Assert.Contains(Assert.Throws<ValidationException>(() => _service.Restore(backup, backup)).Issues, i => i.Code == "restore.same-file");
            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(taken)); // untouched
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class FakeCompanyFiles : ICompanyFiles
    {
        public string? CreatedPath { get; private set; }
        public NewCompanyData? CreatedWith { get; private set; }
        public CompanyInfo? Current { get; private set; }

        public Task<CompanyInfo> CreateAsync(string path, string password, NewCompanyData company, CancellationToken cancellationToken = default)
        {
            CreatedPath = path;
            CreatedWith = company;
            var r = company.Request;
            Current = new CompanyInfo(Guid.NewGuid(), r.NameAr, r.NameEn, r.CountryCode, r.BaseCurrencyCode,
                r.FiscalYearStartMonth, r.FirstFiscalYear, r.EnabledModules, path);
            return Task.FromResult(Current);
        }

        public Task<CompanyInfo> OpenAsync(string path, string password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Close() => Current = null;

        public Task<CompanyInfo> SetEnabledModulesAsync(IReadOnlyList<string> modules, CancellationToken cancellationToken = default)
        {
            Current = Current! with { EnabledModules = modules };
            return Task.FromResult(Current);
        }

        public Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
