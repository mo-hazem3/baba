using Baba.Application.Trade;
using Baba.Domain.Trade;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Trade;

public sealed class ProductStore(ICompanyDbContextFactory contexts) : IProductStore
{
    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.Products.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Products.Add(product);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.Products.SingleAsync(p => p.Id == product.Id, cancellationToken);
        context.Entry(stored).CurrentValues.SetValues(product);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Products.Remove(await context.Products.SingleAsync(p => p.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class PriceListStore(ICompanyDbContextFactory contexts) : IPriceListStore
{
    public async Task<IReadOnlyList<PriceList>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.PriceLists.AsNoTracking().Include(l => l.Lines).ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(PriceList priceList, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.PriceLists.Include(l => l.Lines).SingleOrDefaultAsync(l => l.Id == priceList.Id, cancellationToken);
        if (stored is null)
        {
            context.PriceLists.Add(priceList);
        }
        else
        {
            context.Entry(stored).CurrentValues.SetValues(priceList);
            context.PriceListLines.RemoveRange(stored.Lines);
            foreach (var line in priceList.Lines)
            {
                line.PriceListId = stored.Id;
                context.PriceListLines.Add(line);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.PriceLists.Remove(await context.PriceLists.Include(l => l.Lines).SingleAsync(l => l.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> PriceListIdsInUseAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var used = await context.Parties.Where(p => p.PriceListId != null).Select(p => p.PriceListId!.Value).Distinct().ToListAsync(cancellationToken);
        return used.ToHashSet();
    }
}
