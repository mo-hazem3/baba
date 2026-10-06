using Baba.Localization;

namespace Baba.Application.Companies;

/// <summary>
/// The company file the app currently has open (one at a time). Implemented by Infrastructure
/// (an encrypted SQLite file on desktop). Failures throw <see cref="CompanyFileException"/>.
/// </summary>
public interface ICompanyFiles
{
    CompanyInfo? Current { get; }

    /// <summary>Creates a new file, applies the schema, and seeds the company row and chart of accounts. The new company is left open.</summary>
    Task<CompanyInfo> CreateAsync(string path, string password, NewCompanyData company, CancellationToken cancellationToken = default);

    Task<CompanyInfo> OpenAsync(string path, string password, CancellationToken cancellationToken = default);

    /// <summary>Releases the file so it can be copied, moved or opened elsewhere. Does nothing if nothing is open.</summary>
    void Close();

    /// <summary>Writes a complete, consistent copy of the open company to <paramref name="destinationPath"/> with the same password.</summary>
    Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default);
}

/// <summary>A validated company plus the accounts to seed it with. Built by <see cref="CompanyService"/>.</summary>
public sealed record NewCompanyData(NewCompanyRequest Request, IReadOnlyList<AccountSeed> Accounts);
