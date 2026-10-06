using Baba.Application.Accounting;
using Baba.Domain;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Accounting;

public sealed class AccountStore(ICompanyDbContextFactory contexts) : IAccountStore
{
    public async Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.Accounts.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> AccountIdsWithEntriesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var used = new HashSet<Guid>();
        used.UnionWith(await context.VoucherLines.Select(l => l.AccountId).Distinct().ToListAsync(cancellationToken));
        used.UnionWith(await context.LedgerEntries.Select(e => e.AccountId).Distinct().ToListAsync(cancellationToken));
        used.UnionWith(await context.Vouchers.Where(v => v.CashAccountId != null).Select(v => v.CashAccountId!.Value).Distinct().ToListAsync(cancellationToken));
        return used;
    }

    public async Task AddAsync(Account account, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Accounts.Add(account);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Account account, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        // Load the stored row and copy the new values onto it, so the audit log records only what really changed.
        var stored = await context.Accounts.SingleAsync(a => a.Id == account.Id, cancellationToken);
        context.Entry(stored).CurrentValues.SetValues(account);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Accounts.Remove(await context.Accounts.SingleAsync(a => a.Id == accountId, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> MoveEntriesAsync(Guid fromAccountId, Guid toAccountId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Voucher lines and bank accounts are tracked, so the audit log shows exactly which vouchers were changed.
        var lines = await context.VoucherLines.Where(l => l.AccountId == fromAccountId).ToListAsync(cancellationToken);
        foreach (var line in lines)
            line.AccountId = toAccountId;

        var vouchers = await context.Vouchers.Where(v => v.CashAccountId == fromAccountId).ToListAsync(cancellationToken);
        foreach (var voucher in vouchers)
            voucher.CashAccountId = toAccountId;

        await context.SaveChangesAsync(cancellationToken);

        // The ledger entries follow their voucher lines (they are derived data, so they are not audited row by row).
        await context.LedgerEntries.Where(e => e.AccountId == fromAccountId)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.AccountId, toAccountId), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return lines.Select(l => l.VoucherId).Concat(vouchers.Select(v => v.Id)).Distinct().Count();
    }
}
