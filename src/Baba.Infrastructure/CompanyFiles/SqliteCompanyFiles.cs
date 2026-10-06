using Baba.Application.Abstractions;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.CompanyFiles;

/// <summary>
/// The open company file: an SQLite database encrypted with SQLCipher (see docs/adr/0002-company-file-format.md).
/// One file is open at a time. It is locked against a second window, uses the single-file rollback journal,
/// is backed up before a schema update, and is fully released on <see cref="Close"/>.
/// </summary>
public sealed class SqliteCompanyFiles(ICurrentUser currentUser, TimeProvider clock)
    : ICompanyFiles, ICompanyDbContextFactory, IDisposable
{
    /// <summary>"BABA" as a number. Stored in the file header so we can tell a Baba file from any other database.</summary>
    internal const int ApplicationId = 0x42414241;

    private const int SqliteNotADatabase = 26;

    private readonly CompanyScope _scope = new();
    private readonly object _gate = new();
    private string? _path;
    private string? _password;
    private FileStream? _lock;
    private CompanyInfo? _current;

    public CompanyInfo? Current => _current;

    public async Task<CompanyInfo> CreateAsync(string path, string password, NewCompanyData company, CancellationToken cancellationToken = default)
    {
        EnsureNothingOpen();
        path = Path.GetFullPath(path);
        if (File.Exists(path))
            throw new CompanyFileException(CompanyFileProblem.FileAlreadyExists, $"'{path}' already exists. Choose a new file name.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var fileLock = AcquireLock(path);
        try
        {
            await using var context = NewContext(path, password, createIfMissing: true, _scope);
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = DELETE;", cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync($"PRAGMA application_id = {ApplicationId};", cancellationToken);

            var seeded = BuildCompany(company.Request);
            _scope.CompanyId = seeded.Id;
            context.Companies.Add(seeded);
            context.Accounts.AddRange(BuildAccounts(company.Accounts));
            await context.SaveChangesAsync(cancellationToken);

            return Opened(path, password, fileLock, seeded);
        }
        catch
        {
            fileLock.Dispose();
            SqliteConnection.ClearAllPools();
            _scope.CompanyId = Guid.Empty;
            TryDelete(path); // never leave a half-made company file behind
            throw;
        }
    }

    public async Task<CompanyInfo> OpenAsync(string path, string password, CancellationToken cancellationToken = default)
    {
        EnsureNothingOpen();
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
            throw new CompanyFileException(CompanyFileProblem.FileNotFound, $"'{path}' was not found.");

        var fileLock = AcquireLock(path);
        try
        {
            await using var context = NewContext(path, password, createIfMissing: false, _scope);
            var connection = context.Database.GetDbConnection();

            // Opening or first reading fails with "not a database" for a wrong password (and for a non-Baba file).
            try
            {
                await context.Database.OpenConnectionAsync(cancellationToken);
                await ScalarAsync(connection, "SELECT count(*) FROM sqlite_master;", cancellationToken);
            }
            catch (SqliteException e) when (e.SqliteErrorCode == SqliteNotADatabase)
            {
                throw new CompanyFileException(CompanyFileProblem.WrongPasswordOrNotABabaFile,
                    "The password is wrong, or this is not a Baba file.", e);
            }

            if (Convert.ToInt32(await ScalarAsync(connection, "PRAGMA application_id;", cancellationToken)) != ApplicationId)
                throw new CompanyFileException(CompanyFileProblem.NotABabaFile, "This file was not made by Baba.");

            await ScalarAsync(connection, "PRAGMA journal_mode = DELETE;", cancellationToken);

            var known = context.Database.GetMigrations().ToHashSet();
            var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            if (applied.Any(m => !known.Contains(m)))
                throw new CompanyFileException(CompanyFileProblem.CreatedByNewerVersion,
                    "This company was saved by a newer version of Baba. Update Baba to open it.");

            if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                await BackupFileAsync(path, password, BeforeUpdateBackupPath(path), cancellationToken);
                await context.Database.MigrateAsync(cancellationToken);
            }

            var company = await context.Companies.AsNoTracking().SingleAsync(cancellationToken);
            _scope.CompanyId = company.Id;
            return Opened(path, password, fileLock, company);
        }
        catch
        {
            fileLock.Dispose();
            SqliteConnection.ClearAllPools();
            throw;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _lock?.Dispose();
            _lock = null;
            _current = null;
            _path = null;
            _password = null;
            _scope.CompanyId = Guid.Empty;
        }

        // Pooling keeps the file open after contexts are disposed; release it so it can be moved or deleted.
        SqliteConnection.ClearAllPools();
    }

    public Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        string path, password;
        lock (_gate)
        {
            (path, password) = (_path, _password) is ({ } p, { } w)
                ? (p, w)
                : throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        }

        return BackupFileAsync(path, password, Path.GetFullPath(destinationPath), cancellationToken);
    }

    public CompanyDbContext Create()
    {
        lock (_gate)
        {
            if (_path is null || _password is null)
                throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
            return NewContext(_path, _password, createIfMissing: false, _scope);
        }
    }

    public void Dispose() => Close();

    private CompanyDbContext NewContext(string path, string password, bool createIfMissing, CompanyScope scope)
    {
        SqliteBootstrap.Ensure();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Password = password,
            Mode = createIfMissing ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
        }.ToString();

        var options = new DbContextOptionsBuilder<CompanyDbContext>().UseSqlite(connectionString).Options;
        return new CompanyDbContext(options, currentUser, clock, scope);
    }

    private CompanyInfo Opened(string path, string password, FileStream fileLock, Company company)
    {
        lock (_gate)
        {
            _path = path;
            _password = password;
            _lock = fileLock;
            _current = new CompanyInfo(
                company.Id, company.NameAr, company.NameEn, company.CountryCode, company.BaseCurrencyCode,
                company.FiscalYearStartMonth, company.FirstFiscalYear, company.EnabledModules.ToList(), path);
            return _current;
        }
    }

    private void EnsureNothingOpen()
    {
        if (_current is not null)
            throw new InvalidOperationException("A company is already open. Close it first.");
    }

    /// <summary>
    /// Locks the file against a second window or process. The operating system drops the lock if Baba crashes,
    /// so a leftover lock file never blocks the next start.
    /// </summary>
    private static FileStream AcquireLock(string path)
    {
        try
        {
            var stream = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 1, FileOptions.DeleteOnClose);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write($"pid {Environment.ProcessId}");
            return stream;
        }
        catch (IOException e)
        {
            throw new CompanyFileException(CompanyFileProblem.OpenElsewhere,
                "This company is already open in another window or another copy of Baba.", e);
        }
    }

    private Company BuildCompany(NewCompanyRequest request) => new()
    {
        NameAr = request.NameAr,
        NameEn = request.NameEn,
        CountryCode = request.CountryCode,
        BaseCurrencyCode = request.BaseCurrencyCode,
        FiscalYearStartMonth = request.FiscalYearStartMonth,
        FirstFiscalYear = request.FirstFiscalYear,
        TaxNumbers = request.TaxNumbers.ToDictionary(t => t.Key, t => t.Value),
        Address = request.Address,
        EnabledModules = request.EnabledModules.ToList(),
    };

    private static List<Account> BuildAccounts(IReadOnlyList<Baba.Localization.AccountSeed> seeds)
    {
        var byCode = seeds.ToDictionary(s => s.Code, s => new Account
        {
            Code = s.Code,
            NameAr = s.NameAr,
            NameEn = s.NameEn,
            Type = s.Type,
            IsPosting = s.IsPosting,
            Role = s.Role,
        });

        foreach (var seed in seeds.Where(s => s.ParentCode is not null))
            byCode[seed.Code].ParentId = byCode[seed.ParentCode!].Id;

        return byCode.Values.ToList();
    }

    private static async Task<object?> ScalarAsync(System.Data.Common.DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    /// <summary>Writes a consistent copy using SQLite's online backup, so it is safe even if a save is in progress.</summary>
    private static async Task BackupFileAsync(string sourcePath, string password, string destinationPath, CancellationToken cancellationToken)
    {
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            throw new CompanyFileException(CompanyFileProblem.InvalidRequest, "Choose a different file for the backup.");
        if (File.Exists(destinationPath))
            throw new CompanyFileException(CompanyFileProblem.FileAlreadyExists, $"'{destinationPath}' already exists.");

        SqliteBootstrap.Ensure();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        string ConnectionString(string path, SqliteOpenMode mode) =>
            new SqliteConnectionStringBuilder { DataSource = path, Password = password, Mode = mode, Pooling = false }.ToString();

        await using var source = new SqliteConnection(ConnectionString(sourcePath, SqliteOpenMode.ReadOnly));
        await using var destination = new SqliteConnection(ConnectionString(destinationPath, SqliteOpenMode.ReadWriteCreate));
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private static string BeforeUpdateBackupPath(string path)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(
            Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}.before-update-{stamp}{Path.GetExtension(path)}");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* best effort */ }
    }
}
