using Baba.Application.Security;
using Baba.Domain.Security;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Security;

public sealed class SecurityStore(ICompanyDbContextFactory contexts) : ISecurityStore
{
    public async Task<IReadOnlyList<AppUser>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.AppUsers.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<AppUser?> FindUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<AppUser?> FindUserByNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == userName, cancellationToken); // NOCASE column
    }

    public async Task AddUserAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.AppUsers.Add(user);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateUserAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.AppUsers.SingleAsync(u => u.Id == user.Id, cancellationToken);
        context.Entry(stored).CurrentValues.SetValues(user);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.AppUsers.Remove(await context.AppUsers.SingleAsync(u => u.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.Roles.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task AddRoleAsync(Role role, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Roles.Add(role);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateRoleAsync(Role role, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.Roles.SingleAsync(r => r.Id == role.Id, cancellationToken);
        context.Entry(stored).CurrentValues.SetValues(role);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Roles.Remove(await context.Roles.SingleAsync(r => r.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }
}
