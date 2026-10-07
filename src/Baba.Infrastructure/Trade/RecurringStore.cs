using Baba.Application.Trade;
using Baba.Domain.Trade;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Trade;

public sealed class RecurringStore(ICompanyDbContextFactory contexts) : IRecurringStore
{
    public async Task<IReadOnlyList<RecurringSchedule>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.RecurringSchedules.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<RecurringSchedule?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.RecurringSchedules.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task AddAsync(RecurringSchedule schedule, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.RecurringSchedules.Add(schedule);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(RecurringSchedule schedule, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.RecurringSchedules.SingleAsync(s => s.Id == schedule.Id, cancellationToken);
        context.Entry(stored).CurrentValues.SetValues(schedule);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.RecurringSchedules.Remove(await context.RecurringSchedules.SingleAsync(s => s.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }
}
