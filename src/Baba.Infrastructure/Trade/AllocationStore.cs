using Baba.Application.Trade;
using Baba.Domain.Trade;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Trade;

public sealed class AllocationStore(ICompanyDbContextFactory contexts) : IAllocationStore
{
    public async Task<IReadOnlyList<Allocation>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.Allocations.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Allocation>> ForPaymentAsync(Guid paymentVoucherId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.Allocations.AsNoTracking().Where(a => a.PaymentVoucherId == paymentVoucherId).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(IReadOnlyList<Allocation> allocations, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Allocations.AddRange(allocations);
        await context.SaveChangesAsync(cancellationToken);
    }
}
