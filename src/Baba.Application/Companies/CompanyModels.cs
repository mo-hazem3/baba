namespace Baba.Application.Companies;

/// <summary>What the new-company wizard collects (brief section 9).</summary>
public sealed record NewCompanyRequest(
    string NameAr,
    string NameEn,
    string CountryCode,
    string BaseCurrencyCode,
    int FiscalYearStartMonth,
    int FirstFiscalYear,
    IReadOnlyDictionary<string, string> TaxNumbers,
    string? Address,
    /// <summary>The key of one of the country pack's chart templates, or null for a blank chart.</summary>
    string? ChartTemplateKey,
    IReadOnlyList<string> EnabledModules);

public sealed record CompanyInfo(
    Guid Id,
    string NameAr,
    string NameEn,
    string CountryCode,
    string BaseCurrencyCode,
    int FiscalYearStartMonth,
    int FirstFiscalYear,
    IReadOnlyList<string> EnabledModules,
    string FilePath);

/// <summary>Why a company file could not be created or opened. The UI turns each into a plain-language message.</summary>
public enum CompanyFileProblem
{
    FileAlreadyExists,
    FileNotFound,

    /// <summary>Wrong password, or not a Baba file. SQLCipher cannot tell these apart.</summary>
    WrongPasswordOrNotABabaFile,

    /// <summary>A valid database that was not made by Baba.</summary>
    NotABabaFile,

    /// <summary>The file is open in another window or another copy of Baba.</summary>
    OpenElsewhere,

    /// <summary>The file was last saved by a newer Baba. Update the app to open it.</summary>
    CreatedByNewerVersion,
    NoCompanyOpen,
    InvalidRequest,
}

public sealed class CompanyFileException(CompanyFileProblem problem, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public CompanyFileProblem Problem { get; } = problem;
}
