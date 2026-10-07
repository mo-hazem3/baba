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
        used.UnionWith(await context.DocumentLines.Where(l => l.AccountId != null).Select(l => l.AccountId!.Value).Distinct().ToListAsync(cancellationToken));
        used.UnionWith(await context.TaxCodes.Where(t => t.OutputAccountId != null).Select(t => t.OutputAccountId!.Value).Distinct().ToListAsync(cancellationToken));
        used.UnionWith(await context.TaxCodes.Where(t => t.InputAccountId != null).Select(t => t.InputAccountId!.Value).Distinct().ToListAsync(cancellationToken));
        used.UnionWith(await context.Products.Where(p => p.InventoryAccountId != null).Select(p => p.InventoryAccountId!.Value).Distinct().ToListAsync(cancellationToken));
        used.UnionWith(await context.Products.Where(p => p.CostOfSalesAccountId != null).Select(p => p.CostOfSalesAccountId!.Value).Distinct().ToListAsync(cancellationToken));
        used.UnionWith(await context.SalaryComponents.Where(c => c.AccountId != null).Select(c => c.AccountId!.Value).Distinct().ToListAsync(cancellationToken));
        used.UnionWith(await context.PayslipItems.Where(i => i.AccountId != null).Select(i => i.AccountId!.Value).Distinct().ToListAsync(cancellationToken));
        foreach (var settings in await context.PayrollSettings.ToListAsync(cancellationToken))
        {
            foreach (var id in new[] { settings.SalaryExpenseAccountId, settings.SalariesPayableAccountId, settings.InsuranceExpenseAccountId, settings.InsurancePayableAccountId, settings.EndOfServiceExpenseAccountId, settings.EndOfServiceProvisionAccountId })
            {
                if (id is { } value)
                    used.Add(value);
            }
        }

        foreach (var asset in await context.FixedAssets.Select(a => new { a.AssetAccountId, a.AccumulatedAccountId, a.ExpenseAccountId }).ToListAsync(cancellationToken))
            used.UnionWith([asset.AssetAccountId, asset.AccumulatedAccountId, asset.ExpenseAccountId]);
        used.UnionWith(await context.StockDocuments.Where(d => d.CounterAccountId != null).Select(d => d.CounterAccountId!.Value).Distinct().ToListAsync(cancellationToken));
        used.UnionWith(await context.Products.Where(p => p.SalesAccountId != null).Select(p => p.SalesAccountId!.Value).Distinct().ToListAsync(cancellationToken));
        used.UnionWith(await context.Products.Where(p => p.PurchaseAccountId != null).Select(p => p.PurchaseAccountId!.Value).Distinct().ToListAsync(cancellationToken));
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

    public async Task<bool> HasReconciledEntriesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await (
            from r in context.ReconciledEntries.AsNoTracking()
            join e in context.LedgerEntries.AsNoTracking() on r.LedgerEntryId equals e.Id
            where e.AccountId == accountId
            select r.Id).AnyAsync(cancellationToken);
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

        foreach (var line in await context.DocumentLines.Where(l => l.AccountId == fromAccountId).ToListAsync(cancellationToken))
            line.AccountId = toAccountId;
        foreach (var product in await context.Products.Where(p => p.SalesAccountId == fromAccountId || p.PurchaseAccountId == fromAccountId).ToListAsync(cancellationToken))
        {
            if (product.SalesAccountId == fromAccountId) product.SalesAccountId = toAccountId;
            if (product.PurchaseAccountId == fromAccountId) product.PurchaseAccountId = toAccountId;
        }

        await context.SaveChangesAsync(cancellationToken);

        // The ledger entries follow their voucher lines (they are derived data, so they are not audited row by row).
        await context.LedgerEntries.Where(e => e.AccountId == fromAccountId)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.AccountId, toAccountId), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return lines.Select(l => l.VoucherId).Concat(vouchers.Select(v => v.Id)).Distinct().Count();
    }
}
