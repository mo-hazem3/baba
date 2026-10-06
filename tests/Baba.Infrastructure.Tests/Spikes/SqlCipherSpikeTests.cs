using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Tests.Spikes;

/// <summary>
/// Phase 0 spike (a), see docs/adr/0002-company-file-format.md. These tests keep the answers honest:
/// SQLCipher works with EF Core, a wrong password is detected, scaled-integer money sums exactly in SQL,
/// and closing a company really releases the file.
/// </summary>
public sealed class SqlCipherSpikeTests : IDisposable
{
    private const decimal Scale = 10_000m;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "baba-spike-" + Guid.NewGuid().ToString("N"));

    static SqlCipherSpikeTests() => SQLitePCL.Batteries_V2.Init();

    public SqlCipherSpikeTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private string NewPath() => Path.Combine(_folder, "Company.baba");

    private static string ConnectionString(string path, string password) =>
        new SqliteConnectionStringBuilder { DataSource = path, Password = password }.ToString();

    private static SpikeContext Open(string path, string password) => new(ConnectionString(path, password));

    /// <summary>Creates a new encrypted file. By default switches it to a single-file journal (see the journal tests).</summary>
    private static SpikeContext Create(string path, string password, bool singleFile = true)
    {
        var context = Open(path, password);
        if (singleFile)
            context.Database.ExecuteSqlRaw("PRAGMA journal_mode = DELETE;");
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public void The_file_is_encrypted_not_plain_sqlite()
    {
        var path = NewPath();
        using (var context = Create(path, "secret-1"))
        {
            context.Lines.Add(new SpikeLine { Id = Guid.CreateVersion7(), Amount = 1.5m, Memo = "visible-text" });
            context.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        var bytes = File.ReadAllBytes(path);
        var header = System.Text.Encoding.ASCII.GetString(bytes, 0, 15);

        Assert.NotEqual("SQLite format 3", header);
        Assert.DoesNotContain("visible-text", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Reopening_with_the_right_password_returns_the_data()
    {
        var path = NewPath();
        using (var context = Create(path, "secret-1"))
        {
            context.Lines.Add(new SpikeLine { Id = Guid.CreateVersion7(), Amount = 12.5m, Memo = "a" });
            context.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        using var reopened = Open(path, "secret-1");

        Assert.Equal(12.5m, reopened.Lines.Single().Amount);
    }

    [Fact]
    public void A_wrong_password_fails_on_the_first_read_with_not_a_database()
    {
        var path = NewPath();
        using (var context = Create(path, "secret-1"))
        {
            context.Lines.Add(new SpikeLine { Id = Guid.CreateVersion7(), Amount = 1m, Memo = "a" });
            context.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        using var wrong = Open(path, "not-the-password");
        var error = Assert.Throws<SqliteException>(() => wrong.Lines.ToList());

        // SQLITE_NOTADB (26): same error as for a file that is not a Baba file at all.
        Assert.Equal(26, error.SqliteErrorCode);
    }

    [Fact]
    public async Task Scaled_integer_money_sums_exactly_in_sql()
    {
        var path = NewPath();
        using var context = Create(path, "secret-1");
        // Classic floating point trap: 0.1 + 0.2 != 0.3. Also 3-decimal (fils) amounts.
        context.Lines.AddRange(
            new SpikeLine { Id = Guid.CreateVersion7(), Amount = 0.1m, Memo = "a" },
            new SpikeLine { Id = Guid.CreateVersion7(), Amount = 0.2m, Memo = "a" },
            new SpikeLine { Id = Guid.CreateVersion7(), Amount = 0.001m, Memo = "b" },
            new SpikeLine { Id = Guid.CreateVersion7(), Amount = 1234567.891m, Memo = "b" });
        await context.SaveChangesAsync();

        // SUM over the stored integer column (decimal property mapped through a converter).
        var total = await context.Lines.SumAsync(l => l.AmountScaled);
        var byMemo = await context.Lines
            .GroupBy(l => l.Memo)
            .Select(g => new { g.Key, Sum = g.Sum(l => l.AmountScaled) })
            .OrderBy(x => x.Key)
            .ToListAsync();

        Assert.Equal(0.3m + 0.001m + 1234567.891m, total / Scale);
        Assert.Equal(0.3m, byMemo[0].Sum / Scale);
        Assert.Equal(1234567.892m, byMemo[1].Sum / Scale);
    }

    [Fact]
    public async Task Summing_a_plain_decimal_column_is_translated_but_goes_through_managed_code()
    {
        // EF Core 10 translates SUM over a default decimal column (stored as text) by calling a managed
        // function per row. It works, but the scaled integer column is plain SQL: faster, and the same
        // in raw-SQL reports and on PostgreSQL. See ADR 0002.
        var path = NewPath();
        using var context = Create(path, "secret-1");
        context.PlainLines.AddRange(
            new PlainLine { Id = Guid.CreateVersion7(), Amount = 0.1m },
            new PlainLine { Id = Guid.CreateVersion7(), Amount = 0.2m });
        await context.SaveChangesAsync();

        var total = await context.PlainLines.SumAsync(l => l.Amount);

        Assert.Equal(0.3m, total);
        Assert.Contains("ef_sum", context.PlainLines.Select(l => l.Amount).GroupBy(_ => 1).Select(g => g.Sum()).ToQueryString());
    }

    [Fact]
    public void A_new_file_defaults_to_wal_which_is_why_we_switch_it_off()
    {
        // Microsoft.Data.Sqlite creates new files in WAL mode. A WAL file can leave -wal/-shm files next to it
        // and a plain copy of just the .baba file can miss recent changes. Baba uses the rollback journal.
        var path = NewPath();
        using var context = Create(path, "secret-1", singleFile: false);

        Assert.Equal("wal", JournalMode(context));
    }

    [Fact]
    public void Switching_to_the_rollback_journal_leaves_a_single_self_contained_file()
    {
        var path = NewPath();
        using (var context = Create(path, "secret-1"))
        {
            context.Lines.Add(new SpikeLine { Id = Guid.CreateVersion7(), Amount = 9m, Memo = "a" });
            context.SaveChanges();

            Assert.Equal("delete", JournalMode(context));
        }
        SqliteConnection.ClearAllPools();

        Assert.Equal([path], Directory.GetFiles(_folder));

        // A plain file copy (what a user or accountant does) opens fine and has the data.
        var copy = Path.Combine(_folder, "Copy.baba");
        File.Copy(path, copy);
        using var copied = Open(copy, "secret-1");
        Assert.Equal(9m, copied.Lines.Single().Amount);
    }

    [Fact]
    public void Closing_the_company_releases_the_file_so_it_can_be_moved_or_deleted()
    {
        var path = NewPath();
        using (var context = Create(path, "secret-1"))
        {
            context.Lines.Add(new SpikeLine { Id = Guid.CreateVersion7(), Amount = 1m, Memo = "a" });
            context.SaveChanges();
        }

        SqliteConnection.ClearAllPools();
        File.Delete(path);

        Assert.False(File.Exists(path));
    }

    private static string? JournalMode(DbContext context)
    {
        context.Database.OpenConnection();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        return (string?)command.ExecuteScalar();
    }

    private sealed class SpikeLine
    {
        public Guid Id { get; set; }
        public string Memo { get; set; } = "";

        /// <summary>The stored integer (amount x 10,000). This is the only mapped column, and what SQL sums.</summary>
        public long AmountScaled { get; set; }

        /// <summary>The decimal view of <see cref="AmountScaled"/>. Not mapped: use it in memory, never in a query.</summary>
        public decimal Amount
        {
            get => AmountScaled / Scale;
            set => AmountScaled = (long)Math.Round(value * Scale, 0, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>A decimal column stored the default way, to show what the SQLite provider does with it.</summary>
    private sealed class PlainLine
    {
        public Guid Id { get; set; }
        public decimal Amount { get; set; }
    }

    private sealed class SpikeContext(string connectionString) : DbContext
    {
        public DbSet<SpikeLine> Lines => Set<SpikeLine>();
        public DbSet<PlainLine> PlainLines => Set<PlainLine>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connectionString);

        protected override void OnModelCreating(ModelBuilder model)
        {
            var line = model.Entity<SpikeLine>();
            line.HasKey(l => l.Id);
            line.Property(l => l.AmountScaled).HasColumnName("Amount");
            line.Ignore(l => l.Amount);

            model.Entity<PlainLine>().HasKey(l => l.Id);
        }
    }
}
