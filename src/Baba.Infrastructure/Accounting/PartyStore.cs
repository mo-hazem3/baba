using Baba.Application.Accounting;
using Baba.Domain.Accounting;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Accounting;

public sealed class PartyStore(ICompanyDbContextFactory contexts) : IPartyStore
{
    public async Task<IReadOnlyList<Party>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.Parties.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> PartyIdsInUseAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var used = await context.VoucherLines.Where(l => l.PartyId != null).Select(l => l.PartyId!.Value).Distinct().ToListAsync(cancellationToken);
        used.AddRange(await context.Documents.Select(d => d.PartyId).Distinct().ToListAsync(cancellationToken));
        return used.ToHashSet();
    }

    public async Task AddAsync(Party party, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Parties.Add(party);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Party party, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        // Copy the new values onto the stored row, so the audit log records only what really changed.
        var stored = await context.Parties.SingleAsync(p => p.Id == party.Id, cancellationToken);
        context.Entry(stored).CurrentValues.SetValues(party);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid partyId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Parties.Remove(await context.Parties.SingleAsync(p => p.Id == partyId, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }
}
