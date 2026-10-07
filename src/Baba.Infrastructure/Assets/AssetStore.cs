using Baba.Application.Assets;
using Baba.Domain.Assets;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Assets;

public sealed class AssetStore(ICompanyDbContextFactory contexts) : IAssetStore
{
    public async Task<IReadOnlyList<FixedAsset>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.FixedAssets.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<FixedAsset?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.FixedAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task AddAsync(FixedAsset asset, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.FixedAssets.Add(asset);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(FixedAsset asset, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.FixedAssets.SingleAsync(a => a.Id == asset.Id, cancellationToken);
        context.Entry(stored).CurrentValues.SetValues(asset);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.FixedAssets.Remove(await context.FixedAssets.SingleAsync(a => a.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AssetDepreciation>> ListDepreciationsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.AssetDepreciations.AsNoTracking().OrderBy(r => r.Month).ToListAsync(cancellationToken);
    }

    public async Task AddDepreciationsAsync(IReadOnlyList<AssetDepreciation> rows, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.AssetDepreciations.AddRange(rows);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteDepreciationsOfVoucherAsync(Guid voucherId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.AssetDepreciations.RemoveRange(await context.AssetDepreciations.Where(r => r.VoucherId == voucherId).ToListAsync(cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }
}
