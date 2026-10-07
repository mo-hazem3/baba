using Baba.Application.Abstractions;
using Baba.Application.Companies;
using Baba.Infrastructure.CompanyFiles;
using Baba.Localization;
using Microsoft.Data.Sqlite;

namespace Baba.Infrastructure.Tests.CompanyFiles;

/// <summary>A temp folder per test, with helpers to build a manager and a valid new-company request.</summary>
public abstract class CompanyFilesFixture : IDisposable
{
    protected const string Password = "correct-horse";

    protected readonly string Folder = Path.Combine(Path.GetTempPath(), "baba-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteCompanyFiles> _managers = [];

    protected FixedClock Clock { get; } = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    protected CompanyFilesFixture() => Directory.CreateDirectory(Folder);

    public void Dispose()
    {
        foreach (var manager in _managers)
            manager.Dispose();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { /* best effort */ }
    }

    protected string NewPath(string name = "Company") => Path.Combine(Folder, name + ".baba");

    /// <summary>A manager, like one running app window. Make two to simulate two windows on the same file.</summary>
    protected SqliteCompanyFiles NewManager(string userId = "tester")
    {
        var manager = new SqliteCompanyFiles(new FakeUser(userId), Clock);
        _managers.Add(manager);
        return manager;
    }

    protected static NewCompanyData NewCompany(
        string nameEn = "Al Noor Trading",
        string nameAr = "شركة النور للتجارة",
        IReadOnlyList<AccountSeed>? accounts = null) =>
        new(new NewCompanyRequest(
                NameAr: nameAr,
                NameEn: nameEn,
                CountryCode: "XX",
                BaseCurrencyCode: "KWD",
                FiscalYearStartMonth: 1,
                FirstFiscalYear: 2026,
                TaxNumbers: new Dictionary<string, string> { ["tax-id"] = "12345" },
                Address: "Block 5",
                ChartTemplateKey: "default",
                EnabledModules: ["bank-cash", "sales"]),
            accounts ?? DefaultChartOfAccounts.Template.Accounts);

    protected sealed class FakeUser(string userId) : ICurrentUser
    {
        public string UserId { get; } = userId;
    }

    protected sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
