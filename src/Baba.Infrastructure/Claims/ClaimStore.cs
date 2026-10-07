using Baba.Application.Claims;
using Baba.Domain.Claims;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Claims;

public sealed class ClaimStore(ICompanyDbContextFactory contexts) : IClaimStore
{
    public async Task<IReadOnlyList<ExpenseClaim>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.ExpenseClaims.AsNoTracking().Include(c => c.Lines).ToListAsync(cancellationToken);
    }

    public async Task<ExpenseClaim?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.ExpenseClaims.AsNoTracking().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task AddAsync(ExpenseClaim claim, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.ExpenseClaims.Add(claim);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ExpenseClaim claim, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var stored = await context.ExpenseClaims.Include(c => c.Lines).SingleAsync(c => c.Id == claim.Id, cancellationToken);
        context.ExpenseClaimLines.RemoveRange(stored.Lines);
        await context.SaveChangesAsync(cancellationToken);

        context.Entry(stored).CurrentValues.SetValues(claim);
        foreach (var line in claim.Lines)
        {
            context.ExpenseClaimLines.Add(new ExpenseClaimLine
            {
                Id = Guid.CreateVersion7(), CompanyId = claim.CompanyId, ClaimId = claim.Id, LineNumber = line.LineNumber, Date = line.Date, Description = line.Description,
                AccountId = line.AccountId, AmountScaled = line.AmountScaled, CostCenterId = line.CostCenterId,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.ExpenseClaims.Remove(await context.ExpenseClaims.SingleAsync(c => c.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }
}
