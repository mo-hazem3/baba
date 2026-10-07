using Baba.Application.Companies;
using Baba.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>A company made by an older version of Baba must still open, after being backed up first.</summary>
public class MigrationTests : AccountingFixture
{
    private const string FirstMigration = "20261006170318_InitialCreate";

    /// <summary>Builds a company file the way Phase 0 made them: only the first migration applied, no accounting tables.</summary>
    private async Task<string> MakePhaseZeroFileAsync()
    {
        var path = NewPath("old");
        SQLitePCL.Batteries_V2.Init();
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Password = Password, Pooling = false }.ToString();
        var scope = new Persistence.CompanyScope();
        var options = new DbContextOptionsBuilder<Persistence.CompanyDbContext>().UseSqlite(connectionString).Options;

        await using (var context = new Persistence.CompanyDbContext(options, new FakeUser("old"), Clock, scope))
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = DELETE;");
            await context.GetService<IMigrator>().MigrateAsync(FirstMigration);
            await context.Database.ExecuteSqlRawAsync("PRAGMA application_id = 1111573057;"); // "BABA", see SqliteCompanyFiles

            var companyId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            var noTaxNumbers = "{}"; // JSON for an empty dictionary
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO Companies (Id, NameAr, NameEn, CountryCode, BaseCurrencyCode, FiscalYearStartMonth, FirstFiscalYear, TaxNumbers, EnabledModules, CreatedAt, CreatedBy)
                VALUES ({companyId}, 'شركة قديمة', 'Old Co', 'XX', 'KWD', 1, 2026, {noTaxNumbers}, '[]', '2026-01-01 00:00:00', 'old')
                """);
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO Accounts (Id, CompanyId, Code, NameAr, NameEn, Type, IsPosting, IsActive, CreatedAt, CreatedBy)
                VALUES ({accountId}, {companyId}, '111', 'الصندوق', 'Cash', 'Asset', 1, 1, '2026-01-01 00:00:00', 'old')
                """);
        }

        return path;
    }

    [Fact]
    public async Task An_older_company_file_is_backed_up_then_updated_and_works_with_the_new_features()
    {
        var path = await MakePhaseZeroFileAsync();
        var before = File.ReadAllBytes(path);

        var files = NewManager();
        var info = await files.OpenAsync(path, Password);

        Assert.Equal("Old Co", info.NameEn);

        // A complete, openable backup of the OLD file was made next to it before anything changed.
        var backup = Assert.Single(Directory.GetFiles(Folder, "old.before-update-*.baba"));
        Assert.Equal(before.Length, new FileInfo(backup).Length);
        files.Close();

        // The backup is the OLD version, untouched: one migration applied, no accounting tables, the old company inside.
        // (Looked at directly: opening it in Baba would upgrade that copy as well.)
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Password = Password, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            async Task<long> Scalar(string sql)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                return (long)(await command.ExecuteScalarAsync())!;
            }

            Assert.Equal(1, await Scalar("SELECT count(*) FROM __EFMigrationsHistory"));
            Assert.Equal(0, await Scalar("SELECT count(*) FROM sqlite_master WHERE name = 'Vouchers'"));
            Assert.Equal(1, await Scalar("SELECT count(*) FROM Companies WHERE NameEn = 'Old Co'"));
        }

        // The updated file has the new tables and the old account is readable, with no special role.
        var again = NewManager("again");
        await again.OpenAsync(path, Password);
        using (var context = again.Create())
        {
            Assert.Equal(context.Database.GetMigrations().Count(), (await context.Database.GetAppliedMigrationsAsync()).Count()); // every migration, however many there are by now
            var cash = await context.Accounts.SingleAsync();
            Assert.Equal((AccountRole.None, "Cash"), (cash.Role, cash.NameEn));
            Assert.Empty(context.Vouchers);
        }
    }

    [Fact]
    public async Task A_file_that_is_already_up_to_date_is_not_backed_up_on_every_open()
    {
        var env = await NewEnvAsync();
        env.Files.Close();

        await NewManager().OpenAsync(Path.Combine(Folder, "Company.baba"), Password);

        Assert.Empty(Directory.GetFiles(Folder, "*.before-update-*"));
    }
}
