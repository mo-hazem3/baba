using Baba.Application.Accounting;
using Baba.Domain.Accounting;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Accounting;

public sealed class PeriodStore(ICompanyDbContextFactory contexts) : IPeriodStore
{
    public async Task<IReadOnlyList<Period>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.Periods.AsNoTracking().OrderBy(p => p.Start).ToListAsync(cancellationToken);
    }

    public async Task SetLockedAsync(DateOnly monthStart, bool locked, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var period = await context.Periods.SingleOrDefaultAsync(p => p.Start == monthStart, cancellationToken);
        if (period is null)
        {
            if (!locked)
                return; // a month that was never locked is already open

            var (start, end) = Period.MonthOf(monthStart);
            context.Periods.Add(new Period { Start = start, End = end, IsLocked = true });
        }
        else
        {
            period.IsLocked = locked;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
