using System.Text.RegularExpressions;
using Baba.Domain;
using Baba.Localization;

namespace Baba.Application.Companies;

/// <summary>Use cases for creating, opening and backing up a company file.</summary>
public sealed class CompanyService(ICompanyFiles files, CountryPackRegistry countryPacks)
{
    public const int MinimumPasswordLength = 6;

    public CompanyInfo? Current => files.Current;

    public Task<CompanyInfo> CreateAsync(string path, string password, NewCompanyRequest request, CancellationToken cancellationToken = default)
    {
        var (normalised, accounts) = Validate(path, password, request);
        return files.CreateAsync(WithBabaExtension(path), password, new NewCompanyData(normalised, accounts), cancellationToken);
    }

    public Task<CompanyInfo> OpenAsync(string path, string password, CancellationToken cancellationToken = default) =>
        files.OpenAsync(path, password, cancellationToken);

    public void Close() => files.Close();

    /// <summary>Turns optional modules on or off (brief section 10). Core accounting is always on; nothing is deleted when a module is switched off.</summary>
    public Task<CompanyInfo> SetEnabledModulesAsync(IReadOnlyList<string> modules, CancellationToken cancellationToken = default)
    {
        var distinct = (modules ?? []).Distinct().ToList();
        if (distinct.Any(m => !ModuleKeys.All.Contains(m)))
            throw new ValidationException([new ValidationIssue("modules", "module.unknown")]);
        return files.SetEnabledModulesAsync(ModuleKeys.All.Where(distinct.Contains).ToList(), cancellationToken);
    }

    public Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default) =>
        files.BackupAsync(destinationPath, cancellationToken);

    /// <summary>Company files always end in .baba, so double-clicking one opens Baba.</summary>
    private static string WithBabaExtension(string path) =>
        path.EndsWith(".baba", StringComparison.OrdinalIgnoreCase) ? path : path + ".baba";

    private (NewCompanyRequest Request, IReadOnlyList<AccountSeed> Accounts) Validate(string path, string password, NewCompanyRequest request)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(path))
            issues.Add(new("path", "path.required"));
        else if (!Path.IsPathFullyQualified(path))
            issues.Add(new("path", "path.not-absolute")); // a relative path would land in the app's own folder
        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
            issues.Add(new("password", "password.too-short"));

        var nameAr = request.NameAr?.Trim() ?? "";
        var nameEn = request.NameEn?.Trim() ?? "";
        if (nameAr.Length == 0 && nameEn.Length == 0)
            issues.Add(new("name", "name.required"));

        var pack = countryPacks.Find(request.CountryCode ?? "");
        if (pack is null)
            issues.Add(new("country", "country.unknown"));

        // A company may use any catalog currency, and always the default currency of its country.
        var currencyCode = request.BaseCurrencyCode?.Trim().ToUpperInvariant() ?? "";
        var currency = CurrencyCatalog.Find(currencyCode);
        if (currency is null && pack is not null && string.Equals(pack.Currency.Currency.Code, currencyCode, StringComparison.Ordinal))
            currency = new CurrencyInfo(pack.Currency.Currency, pack.Currency.MajorNameEn, pack.Currency.MajorNameAr);
        if (currency is null)
            issues.Add(new("currency", "currency.unknown"));

        if (request.FiscalYearStartMonth is < 1 or > 12)
            issues.Add(new("fiscalYearStartMonth", "fiscal-year.month-invalid"));
        if (request.FirstFiscalYear is < 1900 or > 2200)
            issues.Add(new("firstFiscalYear", "fiscal-year.year-invalid"));

        foreach (var module in request.EnabledModules ?? [])
        {
            if (!ModuleKeys.All.Contains(module))
                issues.Add(new("modules", "module.unknown"));
        }

        IReadOnlyList<AccountSeed> accounts = [];
        if (pack is not null)
        {
            ValidateTaxNumbers(pack, request, issues);

            if (request.ChartTemplateKey is not null)
            {
                var chart = pack.ChartsOfAccounts.FirstOrDefault(c => c.Key == request.ChartTemplateKey);
                if (chart is null)
                    issues.Add(new("chartTemplateKey", "chart.unknown"));
                else
                    accounts = chart.Accounts;
            }
        }

        if (issues.Count > 0)
            throw new ValidationException(issues);

        var normalised = request with
        {
            // The wizard asks for both names; if only one is given it is used for both so lists and search always work.
            NameAr = nameAr.Length > 0 ? nameAr : nameEn,
            NameEn = nameEn.Length > 0 ? nameEn : nameAr,
            CountryCode = pack!.Identity.Code,
            BaseCurrencyCode = currency!.Code,
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            TaxNumbers = (request.TaxNumbers ?? new Dictionary<string, string>())
                .Where(t => !string.IsNullOrWhiteSpace(t.Value))
                .ToDictionary(t => t.Key, t => t.Value.Trim()),
            EnabledModules = (request.EnabledModules ?? []).Distinct().ToList(),
        };
        return (normalised, accounts);
    }

    private static void ValidateTaxNumbers(ICountryPack pack, NewCompanyRequest request, List<ValidationIssue> issues)
    {
        var given = request.TaxNumbers ?? new Dictionary<string, string>();

        foreach (var key in given.Keys.Where(k => pack.TaxRegistration.All(r => r.Key != k)))
            issues.Add(new($"taxNumbers.{key}", "tax.unknown"));

        foreach (var rule in pack.TaxRegistration)
        {
            var value = given.GetValueOrDefault(rule.Key)?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                if (rule.RequiredForCompany)
                    issues.Add(new($"taxNumbers.{rule.Key}", "tax.required"));
            }
            else if (rule.Pattern is not null && !Regex.IsMatch(value, rule.Pattern))
            {
                issues.Add(new($"taxNumbers.{rule.Key}", "tax.invalid"));
            }
        }
    }
}
