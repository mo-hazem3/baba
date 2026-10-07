using Baba.Application.Companies;
using Baba.Localization;

namespace Baba.Api.Contracts;

// Request and response shapes of the HTTP API. The TypeScript client is generated from these via OpenAPI.

public sealed record CreateCompanyRequest(string Path, string Password, NewCompanyRequest Company);

public sealed record OpenCompanyRequest(string Path, string Password);

public sealed record BackupRequest(string DestinationPath);

public sealed record SetModulesRequest(IReadOnlyList<string> Modules);

public sealed record SaveDialogRequest(string SuggestedFileName);

public sealed record PathChoice(string? Path);

public sealed record RemoveRecentFileRequest(string Path);

public sealed record RecentFileDto(string Path, string Name, DateTime LastOpenedAt, bool Exists);

/// <summary>What this host can do. The UI hides features the host does not offer (for example native file dialogs).</summary>
public sealed record HostInfo(bool FileDialogs, bool PdfPrinting, string Version);

/// <summary>A company file the app was asked to open on start (double-click on a .baba file).</summary>
public sealed record StartupInfo(string? OpenPath);

public sealed record ModuleDto(string Key);

public sealed record CurrencyDto(string Code, int MinorUnits, string NameEn, string NameAr);

public sealed record CurrencyUnitsDto(string Code, int MinorUnits, string MajorNameEn, string MajorNameAr, string MinorNameEn, string MinorNameAr);

public sealed record TaxRegistrationRuleDto(string Key, string NameEn, string NameAr, string? Pattern, bool RequiredForCompany);

public sealed record ChartTemplateDto(string Key, string NameEn, string NameAr, int AccountCount);

public sealed record CapabilitiesDto(bool HasTaxCodes, bool HasTaxReturn, bool HasEInvoicing, bool HasWithholding, bool HasPayroll, bool HasStatutoryReports);

/// <summary>
/// Everything the UI needs to know about a country, as data. The UI shows country-dependent fields from
/// <see cref="TaxRegistration"/> and <see cref="Capabilities"/>, never from the country code.
/// </summary>
public sealed record CountryDto(
    string Code,
    string NameEn,
    string NameAr,
    IReadOnlyList<string> Languages,
    CurrencyUnitsDto Currency,
    IReadOnlyList<CalendarKind> Calendars,
    IReadOnlyList<DayOfWeek> DefaultWeekend,
    int DefaultFiscalYearStartMonth,
    IReadOnlyList<TaxRegistrationRuleDto> TaxRegistration,
    IReadOnlyList<ChartTemplateDto> ChartsOfAccounts,
    bool BilingualPrintRequired,
    CapabilitiesDto Capabilities)
{
    public static CountryDto From(ICountryPack pack) => new(
        pack.Identity.Code,
        pack.Identity.NameEn,
        pack.Identity.NameAr,
        pack.Identity.Languages,
        new CurrencyUnitsDto(
            pack.Currency.Currency.Code,
            pack.Currency.Currency.MinorUnits,
            pack.Currency.MajorNameEn,
            pack.Currency.MajorNameAr,
            pack.Currency.MinorNameEn,
            pack.Currency.MinorNameAr),
        pack.Calendar.Calendars,
        pack.Calendar.DefaultWeekend,
        pack.Calendar.DefaultFiscalYearStartMonth,
        pack.TaxRegistration.Select(r => new TaxRegistrationRuleDto(r.Key, r.NameEn, r.NameAr, r.Pattern, r.RequiredForCompany)).ToList(),
        pack.ChartsOfAccounts.Select(c => new ChartTemplateDto(c.Key, c.NameEn, c.NameAr, c.Accounts.Count)).ToList(),
        pack.DocumentRules.BilingualPrintRequired,
        new CapabilitiesDto(
            pack.Capabilities.HasTaxCodes,
            pack.Capabilities.HasTaxReturn,
            pack.Capabilities.HasEInvoicing,
            pack.Capabilities.HasWithholding,
            pack.Capabilities.HasPayroll,
            pack.Capabilities.HasStatutoryReports));
}
