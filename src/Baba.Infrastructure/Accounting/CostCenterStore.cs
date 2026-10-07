using Baba.Application.Accounting;
using Baba.Domain.Accounting;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Accounting;

public sealed class CostCenterStore(ICompanyDbContextFactory contexts) : ICostCenterStore
{
    public async Task<IReadOnlyList<CostCenter>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.CostCenters.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> CostCenterIdsInUseAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var used = await context.VoucherLines.Where(l => l.CostCenterId != null).Select(l => l.CostCenterId!.Value).Distinct().ToListAsync(cancellationToken);
        return used.ToHashSet();
    }

    public async Task AddAsync(CostCenter costCenter, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.CostCenters.Add(costCenter);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(CostCenter costCenter, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.CostCenters.SingleAsync(c => c.Id == costCenter.Id, cancellationToken);
        context.Entry(stored).CurrentValues.SetValues(costCenter);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid costCenterId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.CostCenters.Remove(await context.CostCenters.SingleAsync(c => c.Id == costCenterId, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }
}
