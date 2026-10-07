using Baba.Application.Budgets;
using Baba.Domain.Budgets;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Budgets;

public sealed class BudgetStore(ICompanyDbContextFactory contexts) : IBudgetStore
{
    public async Task<IReadOnlyList<BudgetEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.BudgetEntries.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task ReplaceAsync(int fiscalYear, Guid? costCenterId, IReadOnlyList<BudgetEntry> entries, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var old = await context.BudgetEntries.Where(e => e.FiscalYear == fiscalYear && e.CostCenterId == costCenterId).ToListAsync(cancellationToken);
        context.BudgetEntries.RemoveRange(old);
        await context.SaveChangesAsync(cancellationToken);
        context.BudgetEntries.AddRange(entries);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
