using Baba.Application.Trade;
using Baba.Domain.Trade;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Trade;

public sealed class TaxCodeStore(ICompanyDbContextFactory contexts) : ITaxCodeStore
{
    public async Task<IReadOnlyList<TaxCode>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.TaxCodes.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task AddAsync(IReadOnlyList<TaxCode> codes, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.TaxCodes.AddRange(codes);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(IReadOnlyList<TaxCode> codes, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var ids = codes.Select(c => c.Id).ToList();
        var stored = await context.TaxCodes.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        foreach (var code in codes)
            context.Entry(stored[code.Id]).CurrentValues.SetValues(code); // only what really changed reaches the audit log
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.TaxCodes.Remove(await context.TaxCodes.SingleAsync(c => c.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> InUseAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var used = await context.DocumentLines.Where(l => l.TaxCodeId != null).Select(l => l.TaxCodeId!.Value).Distinct().ToListAsync(cancellationToken);
        used.AddRange(await context.Products.Where(p => p.TaxCodeId != null).Select(p => p.TaxCodeId!.Value).Distinct().ToListAsync(cancellationToken));
        return used.ToHashSet();
    }
}
