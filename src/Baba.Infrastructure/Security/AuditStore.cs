using Baba.Application.Security;
using Baba.Domain;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Security;

public sealed class AuditStore(ICompanyDbContextFactory contexts) : IAuditStore
{
    public async Task<IReadOnlyList<AuditLogEntry>> SearchAsync(AuditSearch search, int limit, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var query = context.AuditLog.AsNoTracking();

        // The log keeps universal time; a day means the computer's day.
        if (search.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
            query = query.Where(l => l.At >= start);
        }

        if (search.To is { } to)
        {
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
            query = query.Where(l => l.At < end);
        }

        if (!string.IsNullOrWhiteSpace(search.User))
        {
            var user = search.User.Trim().ToLower();
            query = query.Where(l => l.UserId.ToLower() == user);
        }

        if (!string.IsNullOrWhiteSpace(search.Entity))
        {
            var entity = search.Entity.Trim();
            query = query.Where(l => l.EntityName == entity);
        }

        return await query.OrderByDescending(l => l.At).ThenByDescending(l => l.Id).Take(limit).ToListAsync(cancellationToken);
    }
}
