using System.Text.Json;
using Baba.Application.Abstractions;
using Baba.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Baba.Infrastructure.Persistence;

/// <summary>
/// The data of one company. Every query is limited to the open company, timestamps and user ids are filled in
/// automatically, and every change is written to the audit log in the same save.
/// </summary>
public sealed class CompanyDbContext(
    DbContextOptions<CompanyDbContext> options,
    ICurrentUser currentUser,
    TimeProvider clock,
    CompanyScope scope) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    // Read by the query filters below (EF turns it into a parameter per context instance).
    private Guid CurrentCompanyId => scope.CompanyId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        configuration.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configuration.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        configuration.Properties<AccountType>().HaveConversion<string>();
        configuration.Properties<AuditAction>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Company>(company =>
        {
            company.Property(c => c.TaxNumbers).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new(),
                new ValueComparer<Dictionary<string, string>>(
                    (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
                    v => v.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
                    v => new Dictionary<string, string>(v)));

            company.Property(c => c.EnabledModules).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new(),
                new ValueComparer<List<string>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s)),
                    v => v.ToList()));
        });

        model.Entity<Account>(account =>
        {
            account.HasIndex(a => new { a.CompanyId, a.Code }).IsUnique();
            account.HasOne<Account>().WithMany().HasForeignKey(a => a.ParentId).OnDelete(DeleteBehavior.Restrict);
            account.HasQueryFilter(a => a.CompanyId == CurrentCompanyId);
        });

        model.Entity<StoredFile>().HasQueryFilter(f => f.CompanyId == CurrentCompanyId);

        model.Entity<AuditLogEntry>(log =>
        {
            log.HasIndex(l => new { l.EntityName, l.EntityId });
            log.HasIndex(l => l.At);
            log.HasQueryFilter(l => l.CompanyId == CurrentCompanyId);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Stamps company id, timestamps and user ids, and adds the audit log rows for this save.</summary>
    private void PrepareChanges()
    {
        ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow().UtcDateTime;
        var user = currentUser.UserId;
        var auditRows = new List<AuditLogEntry>();

        var changed = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not AuditLogEntry)
            .ToList();

        foreach (var entry in changed)
        {
            if (entry is { State: EntityState.Added, Entity: ICompanyScoped scoped } && scoped.CompanyId == Guid.Empty)
                scoped.CompanyId = CurrentCompanyId;

            if (entry.Entity is IAuditable auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedAt = now;
                    auditable.CreatedBy = user;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = user;
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                }
            }

            auditRows.Add(CreateAuditRow(entry, now, user));
        }

        AuditLog.AddRange(auditRows);
    }

    private AuditLogEntry CreateAuditRow(EntityEntry entry, DateTime now, string user)
    {
        var id = (Guid)entry.Property(nameof(Entity.Id)).CurrentValue!;
        var companyId = entry.Entity is Company ? id : CurrentCompanyId;

        // Bookkeeping columns are not interesting in the log.
        static bool IsAuditColumn(string name) =>
            name is nameof(IAuditable.CreatedAt) or nameof(IAuditable.CreatedBy)
                or nameof(IAuditable.UpdatedAt) or nameof(IAuditable.UpdatedBy);

        var properties = entry.Properties.Where(p => !IsAuditColumn(p.Metadata.Name)).ToList();

        object? Describe(object? value) => value is byte[] bytes ? $"[{bytes.Length} bytes]" : value;

        Dictionary<string, object?> Snapshot(IEnumerable<PropertyEntry> source, bool original) =>
            source.ToDictionary(p => p.Metadata.Name, p => Describe(original ? p.OriginalValue : p.CurrentValue));

        string? before = null, after = null;
        var action = AuditAction.Created;
        switch (entry.State)
        {
            case EntityState.Added:
                after = JsonSerializer.Serialize(Snapshot(properties, original: false));
                break;
            case EntityState.Deleted:
                action = AuditAction.Deleted;
                before = JsonSerializer.Serialize(Snapshot(properties, original: true));
                break;
            default:
                action = AuditAction.Updated;
                var modified = properties.Where(p => p.IsModified).ToList();
                before = JsonSerializer.Serialize(Snapshot(modified, original: true));
                after = JsonSerializer.Serialize(Snapshot(modified, original: false));
                break;
        }

        return new AuditLogEntry
        {
            CompanyId = companyId,
            At = now,
            UserId = user,
            Action = action,
            EntityName = entry.Metadata.ClrType.Name,
            EntityId = id,
            BeforeJson = before,
            AfterJson = after,
        };
    }
}
