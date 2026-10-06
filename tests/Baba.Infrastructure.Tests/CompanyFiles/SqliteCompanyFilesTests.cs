using Baba.Application.Companies;
using Baba.Domain;
using Baba.Localization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Tests.CompanyFiles;

public class SqliteCompanyFilesTests : CompanyFilesFixture
{
    [Fact]
    public async Task Creating_a_company_makes_an_encrypted_file_with_the_company_and_its_chart()
    {
        var files = NewManager();
        var path = NewPath();

        var info = await files.CreateAsync(path, Password, NewCompany());

        Assert.Equal(path, info.FilePath);
        Assert.Equal("Al Noor Trading", info.NameEn);
        Assert.Equal("KWD", info.BaseCurrencyCode);
        Assert.Equal(["bank-cash", "sales"], info.EnabledModules);
        Assert.Same(info, files.Current);

        // The file is open (pooled connection), so read it with shared access.
        var firstBytes = new byte[15];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            stream.ReadExactly(firstBytes);
        Assert.NotEqual("SQLite format 3", System.Text.Encoding.ASCII.GetString(firstBytes));

        using var context = files.Create();
        Assert.Equal(DefaultChartOfAccounts.Template.Accounts.Count, await context.Accounts.CountAsync());
        var current = await context.Accounts.SingleAsync(a => a.Code == "11");
        var parent = await context.Accounts.SingleAsync(a => a.Id == current.ParentId);
        Assert.Equal("1", parent.Code);
    }

    [Fact]
    public async Task A_closed_company_reopens_with_the_password_and_keeps_arabic_text()
    {
        var path = NewPath();
        var first = NewManager();
        var created = await first.CreateAsync(path, Password, NewCompany());
        first.Close();

        var second = NewManager();
        var reopened = await second.OpenAsync(path, Password);

        Assert.Equal(created.Id, reopened.Id);
        Assert.Equal("شركة النور للتجارة", reopened.NameAr);
        using var context = second.Create();
        var company = await context.Companies.SingleAsync();
        Assert.Equal("12345", company.TaxNumbers["tax-id"]);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_leaves_the_file_unlocked()
    {
        var path = NewPath();
        var files = NewManager();
        await files.CreateAsync(path, Password, NewCompany());
        files.Close();

        var other = NewManager();
        var error = await Assert.ThrowsAsync<CompanyFileException>(() => other.OpenAsync(path, "wrong-password"));

        Assert.Equal(CompanyFileProblem.WrongPasswordOrNotABabaFile, error.Problem);
        Assert.Null(other.Current);
        Assert.False(File.Exists(path + ".lock"));

        // And the right password still works afterwards.
        Assert.NotNull(await other.OpenAsync(path, Password));
    }

    [Fact]
    public async Task A_file_that_is_not_a_database_at_all_is_refused()
    {
        var path = NewPath("notes");
        await File.WriteAllTextAsync(path, "just some text, not a database");

        var error = await Assert.ThrowsAsync<CompanyFileException>(() => NewManager().OpenAsync(path, Password));

        Assert.Equal(CompanyFileProblem.WrongPasswordOrNotABabaFile, error.Problem);
    }

    [Fact]
    public async Task A_valid_database_that_baba_did_not_make_is_refused()
    {
        var path = NewPath("other-app");
        SQLitePCL.Batteries_V2.Init();
        await using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Password = Password }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Notes (Id INTEGER);";
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        var error = await Assert.ThrowsAsync<CompanyFileException>(() => NewManager().OpenAsync(path, Password));

        Assert.Equal(CompanyFileProblem.NotABabaFile, error.Problem);
    }

    [Fact]
    public async Task A_second_window_cannot_open_a_company_that_is_already_open()
    {
        var path = NewPath();
        var firstWindow = NewManager();
        await firstWindow.CreateAsync(path, Password, NewCompany());

        var secondWindow = NewManager();
        var error = await Assert.ThrowsAsync<CompanyFileException>(() => secondWindow.OpenAsync(path, Password));

        Assert.Equal(CompanyFileProblem.OpenElsewhere, error.Problem);

        firstWindow.Close();
        Assert.NotNull(await secondWindow.OpenAsync(path, Password));
    }

    [Fact]
    public async Task Opening_a_missing_file_and_creating_over_an_existing_one_are_refused()
    {
        var files = NewManager();
        var path = NewPath();

        var missing = await Assert.ThrowsAsync<CompanyFileException>(() => files.OpenAsync(path, Password));
        Assert.Equal(CompanyFileProblem.FileNotFound, missing.Problem);
        Assert.False(File.Exists(path), "Opening must never create a file.");

        await files.CreateAsync(path, Password, NewCompany());
        files.Close();
        var exists = await Assert.ThrowsAsync<CompanyFileException>(() => files.CreateAsync(path, Password, NewCompany()));
        Assert.Equal(CompanyFileProblem.FileAlreadyExists, exists.Problem);
    }

    [Fact]
    public async Task Only_one_company_can_be_open_at_a_time()
    {
        var files = NewManager();
        await files.CreateAsync(NewPath("one"), Password, NewCompany());

        await Assert.ThrowsAsync<InvalidOperationException>(() => files.CreateAsync(NewPath("two"), Password, NewCompany()));
    }

    [Fact]
    public async Task Closing_releases_the_file_so_it_can_be_moved_or_deleted()
    {
        var path = NewPath();
        var files = NewManager();
        await files.CreateAsync(path, Password, NewCompany());
        Assert.True(File.Exists(path + ".lock"));

        files.Close();

        Assert.False(File.Exists(path + ".lock"));
        Assert.Null(files.Current);
        File.Move(path, NewPath("moved"));
        Assert.True(File.Exists(NewPath("moved")));
        Assert.Throws<CompanyFileException>(() => files.Create());
    }

    [Fact]
    public async Task A_closed_company_is_a_single_file_with_no_wal_files()
    {
        var path = NewPath();
        var files = NewManager();
        await files.CreateAsync(path, Password, NewCompany());
        using (var context = files.Create())
        {
            context.Accounts.Add(new Account { Code = "99", NameEn = "Extra", NameAr = "إضافي", Type = AccountType.Asset, IsPosting = true });
            await context.SaveChangesAsync();
        }
        files.Close();

        Assert.Equal([path], Directory.GetFiles(Folder));
    }

    [Fact]
    public async Task A_backup_is_a_complete_encrypted_copy_that_opens_with_the_same_password()
    {
        var path = NewPath();
        var backup = Path.Combine(Folder, "backups", "Company-backup.baba");
        var files = NewManager();
        var created = await files.CreateAsync(path, Password, NewCompany());

        await files.BackupAsync(backup);
        files.Close();

        Assert.True(File.Exists(backup));
        var restored = await NewManager().OpenAsync(backup, Password);
        Assert.Equal(created.Id, restored.Id);
        Assert.Equal(created.NameAr, restored.NameAr);
    }

    [Fact]
    public async Task A_backup_never_overwrites_a_file_or_the_company_itself()
    {
        var path = NewPath();
        var files = NewManager();
        await files.CreateAsync(path, Password, NewCompany());

        var sameFile = await Assert.ThrowsAsync<CompanyFileException>(() => files.BackupAsync(path));
        Assert.Equal(CompanyFileProblem.InvalidRequest, sameFile.Problem);

        var existing = NewPath("existing");
        await File.WriteAllTextAsync(existing, "keep me");
        var exists = await Assert.ThrowsAsync<CompanyFileException>(() => files.BackupAsync(existing));
        Assert.Equal(CompanyFileProblem.FileAlreadyExists, exists.Problem);
        Assert.Equal("keep me", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task Backup_needs_an_open_company()
    {
        var error = await Assert.ThrowsAsync<CompanyFileException>(() => NewManager().BackupAsync(NewPath("b")));

        Assert.Equal(CompanyFileProblem.NoCompanyOpen, error.Problem);
    }

    [Fact]
    public async Task A_failed_create_leaves_no_file_and_no_lock_behind()
    {
        var path = NewPath();
        var files = NewManager();
        var duplicate = new AccountSeed("1", "A", "أ", AccountType.Asset, null, true);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            files.CreateAsync(path, Password, NewCompany(accounts: [duplicate, duplicate])));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".lock"));
        Assert.Null(files.Current);
    }

    [Fact]
    public async Task A_file_saved_by_a_newer_version_is_refused_with_a_clear_reason()
    {
        var path = NewPath();
        var files = NewManager();
        await files.CreateAsync(path, Password, NewCompany());
        files.Close();

        await using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Password = Password }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('29990101000000_FromTheFuture', '99.0.0');";
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        var error = await Assert.ThrowsAsync<CompanyFileException>(() => NewManager().OpenAsync(path, Password));

        Assert.Equal(CompanyFileProblem.CreatedByNewerVersion, error.Problem);
    }

    [Fact]
    public async Task Queries_only_see_rows_of_the_open_company()
    {
        var files = NewManager();
        await files.CreateAsync(NewPath(), Password, NewCompany());

        using var context = files.Create();
        context.Files.Add(new StoredFile { CompanyId = Guid.NewGuid(), Name = "someone-else.png", ContentType = "image/png", Content = [1] });
        context.Files.Add(new StoredFile { Name = "mine.png", ContentType = "image/png", Content = [2] });
        await context.SaveChangesAsync();

        Assert.Equal(["mine.png"], await context.Files.Select(f => f.Name).ToListAsync());
        Assert.Equal(2, await context.Files.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Saving_fills_in_company_id_timestamps_and_user()
    {
        var files = NewManager("amal");
        var info = await files.CreateAsync(NewPath(), Password, NewCompany());

        using var context = files.Create();
        var account = await context.Accounts.FirstAsync();

        Assert.Equal(info.Id, account.CompanyId);
        Assert.Equal("amal", account.CreatedBy);
        Assert.Equal(Clock.Now.UtcDateTime, account.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, account.CreatedAt.Kind);
        Assert.Null(account.UpdatedAt);
    }

    [Fact]
    public async Task Changes_are_written_to_the_audit_log_with_before_and_after_values()
    {
        var files = NewManager("amal");
        await files.CreateAsync(NewPath(), Password, NewCompany());

        Clock.Now = Clock.Now.AddHours(1);
        Guid accountId;
        using (var context = files.Create())
        {
            var account = await context.Accounts.SingleAsync(a => a.Code == "31");
            accountId = account.Id;
            account.NameEn = "Share capital";
            await context.SaveChangesAsync();
        }

        using var read = files.Create();
        var changed = await read.Accounts.SingleAsync(a => a.Id == accountId);
        Assert.Equal(Clock.Now.UtcDateTime, changed.UpdatedAt);
        Assert.Equal("amal", changed.UpdatedBy);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero).UtcDateTime, changed.CreatedAt);

        var created = await read.AuditLog.SingleAsync(l => l.EntityId == accountId && l.Action == AuditAction.Created);
        Assert.Contains("\"Code\":\"31\"", created.AfterJson);
        Assert.Null(created.BeforeJson);

        var updated = await read.AuditLog.SingleAsync(l => l.EntityId == accountId && l.Action == AuditAction.Updated);
        Assert.Equal("amal", updated.UserId);
        Assert.Equal("{\"NameEn\":\"Capital\"}", updated.BeforeJson);
        Assert.Equal("{\"NameEn\":\"Share capital\"}", updated.AfterJson);
        Assert.Equal(await read.Companies.Select(c => c.Id).SingleAsync(), updated.CompanyId);
    }

    [Fact]
    public async Task Deleting_is_audited_and_the_company_creation_itself_is_in_the_log()
    {
        var files = NewManager();
        var info = await files.CreateAsync(NewPath(), Password, NewCompany());

        using var context = files.Create();
        Assert.True(await context.AuditLog.AnyAsync(l => l.EntityName == nameof(Company) && l.EntityId == info.Id && l.Action == AuditAction.Created));

        var leaf = await context.Accounts.FirstAsync(a => a.Code == "52");
        context.Accounts.Remove(leaf);
        await context.SaveChangesAsync();

        var deleted = await context.AuditLog.SingleAsync(l => l.EntityId == leaf.Id && l.Action == AuditAction.Deleted);
        Assert.Contains("\"Code\":\"52\"", deleted.BeforeJson);
        Assert.Null(deleted.AfterJson);
    }

    [Fact]
    public async Task Account_codes_are_unique_within_a_company()
    {
        var files = NewManager();
        await files.CreateAsync(NewPath(), Password, NewCompany());

        using var context = files.Create();
        context.Accounts.Add(new Account { Code = "11", NameEn = "Dup", NameAr = "مكرر", Type = AccountType.Asset, IsPosting = true });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
