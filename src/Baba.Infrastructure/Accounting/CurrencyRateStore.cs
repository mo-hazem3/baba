using Baba.Application.Accounting;
using Baba.Domain.Accounting;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Accounting;

public sealed class CurrencyRateStore(ICompanyDbContextFactory contexts) : ICurrencyRateStore
{
    public async Task<IReadOnlyList<CurrencyRate>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.CurrencyRates.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(CurrencyRate rate, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.CurrencyRates.SingleOrDefaultAsync(r => r.Id == rate.Id, cancellationToken);
        if (stored is null)
            context.CurrencyRates.Add(rate);
        else
            context.Entry(stored).CurrentValues.SetValues(rate);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.CurrencyRates.Remove(await context.CurrencyRates.SingleAsync(r => r.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }
}
