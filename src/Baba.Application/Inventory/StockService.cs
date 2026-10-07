using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Application.Trade;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Inventory;
using Baba.Domain.Trade;
using Baba.Localization;

namespace Baba.Application.Inventory;

/// <summary>A movement an invoice or stock document wants to make. <c>LineIndex</c> names the line it came from, for error messages.</summary>
public sealed record NewMovement(
    int LineIndex,
    Guid ProductId,
    Guid WarehouseId,
    DateOnly Date,
    StockMovementKind Kind,
    long QuantityScaled,
    CostMode Mode,
    long GivenValueScaled = 0,
    Guid? SourceOwnerId = null);

/// <summary>All the movements of one invoice, note or stock document, which replace what it made before.</summary>
public sealed record StockChange(Guid OwnerId, bool IsDocument, DateTime OwnerCreatedAt, IReadOnlyList<NewMovement> Movements);

/// <summary>How much of a product is in a warehouse and what it is worth, in the company's currency.</summary>
public sealed record StockLevelDto(Guid ProductId, Guid WarehouseId, decimal Quantity, decimal Value);

/// <summary>
/// Stock on hand (brief section 10.4). Stock is the sum of its movements and nothing else is stored. Whenever an invoice or stock document
/// changes its movements, every movement of the products involved is worked out again in date order (<see cref="StockEngine"/>), which makes
/// weighted-average costing right even for a purchase entered late. The change is refused if it would leave any product short in any
/// warehouse at any date, or if it would change a cost already booked in a locked month. The cost of the stock sold on an invoice and the
/// value of an opening or adjustment are ledger vouchers made from the movements, so they follow every recalculation and the value of the
/// stock always equals the stock account in the ledger.
/// </summary>
public sealed class StockService(
    IStockStore store,
    IProductStore products,
    IAccountStore accounts,
    IDocumentStore documents,
    VoucherService vouchers,
    ICompanyFiles files)
{
    // ---------------------------------------------------------------- Reading

    /// <summary>The stock of every product in every warehouse on a date (now, when none is given).</summary>
    public async Task<IReadOnlyList<StockLevelDto>> LevelsAsync(DateOnly? asOf = null, CancellationToken cancellationToken = default) =>
        (await store.ListMovementsAsync(cancellationToken))
            .Where(m => asOf is null || m.Date <= asOf)
            .GroupBy(m => (m.ProductId, m.WarehouseId))
            .Select(g => new StockLevelDto(g.Key.ProductId, g.Key.WarehouseId, Scaled.ToDecimal(g.Sum(m => m.QuantityScaled)), Scaled.ToDecimal(g.Sum(m => m.ValueScaled))))
            .Where(l => l.Quantity != 0 || l.Value != 0)
            .ToList();

    // ---------------------------------------------------------------- Changing

    /// <summary>Checks that the change can be made (nothing goes short, no locked month is touched, the accounts exist) without making it.</summary>
    public async Task CheckAsync(StockChange change, CancellationToken cancellationToken = default)
    {
        await RequireAccountsAsync(change, cancellationToken);
        var plan = await SimulateAsync(change, cancellationToken);
        await RequireOpenMonthsAsync(plan, change, cancellationToken);
    }

    /// <summary>Makes the change: replaces the movements of the owner, brings the other movements' values up to date, and refreshes the vouchers made from them.</summary>
    public async Task ApplyAsync(StockChange change, CancellationToken cancellationToken = default)
    {
        await RequireAccountsAsync(change, cancellationToken);
        var plan = await SimulateAsync(change, cancellationToken);
        await RequireOpenMonthsAsync(plan, change, cancellationToken);

        await store.ReplaceMovementsAsync(change.OwnerId, plan.Added, plan.ValueChanges, cancellationToken);
        await RefreshVouchersAsync(plan.RefreshOwners.Append(change.OwnerId).Distinct().ToList(), cancellationToken);
    }

    /// <summary>Takes away everything an invoice or stock document did to the stock (before it is deleted).</summary>
    public async Task RemoveAsync(Guid ownerId, bool isDocument, CancellationToken cancellationToken = default)
    {
        var change = new StockChange(ownerId, isDocument, DateTime.UtcNow, []);
        var plan = await SimulateAsync(change, cancellationToken);
        await RequireOpenMonthsAsync(plan, change, cancellationToken);

        if (isDocument)
        {
            if (await documents.FindAsync(ownerId, cancellationToken) is { CostVoucherId: { } costVoucher } document)
            {
                await vouchers.DeleteSystemAsync(costVoucher, cancellationToken);
                document.CostVoucherId = null;
                await documents.SaveAsync([document], cancellationToken);
            }
        }
        else if (await store.FindStockDocumentAsync(ownerId, cancellationToken) is { VoucherId: { } voucher } stockDocument)
        {
            await vouchers.DeleteSystemAsync(voucher, cancellationToken);
            stockDocument.VoucherId = null;
            await store.SaveStockDocumentAsync(stockDocument, cancellationToken);
        }

        await store.ReplaceMovementsAsync(ownerId, [], plan.ValueChanges, cancellationToken);
        await RefreshVouchersAsync(plan.RefreshOwners.Where(o => o != ownerId).ToList(), cancellationToken);
    }

    // ---------------------------------------------------------------- Working it out

    private sealed record Plan(IReadOnlyList<StockMovement> Added, IReadOnlyDictionary<Guid, long> ValueChanges, IReadOnlyList<Guid> RefreshOwners, IReadOnlyList<StockMovement> Kept);

    private static Guid OwnerOf(StockMovement m) => m.DocumentId ?? m.StockDocumentId!.Value;

    private async Task<Plan> SimulateAsync(StockChange change, CancellationToken cancellationToken)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var currency = CurrencyCatalog.Find(company.BaseCurrencyCode)?.Currency ?? new Currency(company.BaseCurrencyCode, 2);

        var kept = (await store.ListMovementsAsync(cancellationToken)).Where(m => OwnerOf(m) != change.OwnerId).ToList();
        var added = change.Movements.Select(n => new StockMovement
        {
            CompanyId = company.Id,
            ProductId = n.ProductId,
            WarehouseId = n.WarehouseId,
            Date = n.Date,
            Kind = n.Kind,
            QuantityScaled = n.QuantityScaled,
            Mode = n.Mode,
            GivenValueScaled = n.GivenValueScaled,
            SourceOwnerId = n.SourceOwnerId,
            DocumentId = change.IsDocument ? change.OwnerId : null,
            StockDocumentId = change.IsDocument ? null : change.OwnerId,
            SourceCreatedAt = change.OwnerCreatedAt,
        }).ToList();

        // Only the products this change touches need to be worked out again.
        var touched = added.Select(m => m.ProductId).Concat((await store.ListMovementsAsync(cancellationToken)).Where(m => OwnerOf(m) == change.OwnerId).Select(m => m.ProductId)).ToHashSet();
        var inputs = kept.Concat(added).Where(m => touched.Contains(m.ProductId)).Select(m => new MovementInput(
            m.Id, m.ProductId, m.WarehouseId, m.Date, m.SourceCreatedAt, m.QuantityScaled, m.Mode, m.GivenValueScaled, OwnerOf(m), m.SourceOwnerId));
        var result = StockEngine.Replay(inputs, currency);

        if (result.Shortfall is { } shortfall)
        {
            // A shortfall at one of this change's own movements is its own line's fault; at anybody else's, this change took the stock they need.
            var ownIndex = added.FindIndex(m => m.Id == shortfall.MovementId);
            var line = ownIndex >= 0 ? change.Movements[ownIndex] : null;
            throw new ValidationException([line is null
                ? new ValidationIssue("document", "stock.would-leave-later-sales-short")
                : new ValidationIssue($"lines[{line.LineIndex}].quantity", "stock.insufficient")]);
        }

        foreach (var movement in added)
            movement.ValueScaled = result.Values[movement.Id];

        var changes = kept.Where(m => result.Values.TryGetValue(m.Id, out var v) && v != m.ValueScaled).ToDictionary(m => m.Id, m => result.Values[m.Id]);
        var owners = kept.Where(m => changes.ContainsKey(m.Id)).Select(OwnerOf).Distinct().ToList();
        return new Plan(added, changes, owners, kept);
    }

    /// <summary>Costs that change must not be in a locked month, and nor must the change itself when it makes a voucher of its own.</summary>
    private async Task RequireOpenMonthsAsync(Plan plan, StockChange change, CancellationToken cancellationToken)
    {
        var dates = plan.Kept.Where(m => plan.RefreshOwners.Contains(OwnerOf(m)) && HasVoucher(m.Kind)).Select(m => m.Date)
            .Concat(plan.Added.Where(m => HasVoucher(m.Kind)).Select(m => m.Date)).Distinct();
        foreach (var date in dates)
        {
            if (!await vouchers.IsDateOpenAsync(date, cancellationToken))
                throw new ValidationException([new ValidationIssue("date", "stock.locked-period")]);
        }
    }

    private static bool HasVoucher(StockMovementKind kind) => kind is StockMovementKind.Sale or StockMovementKind.SaleReturn or StockMovementKind.Opening or StockMovementKind.Adjustment;

    // ---------------------------------------------------------------- Accounts

    /// <summary>The stock account and the cost of sales account of a product: its own, or the company's.</summary>
    public async Task<(Guid Inventory, Guid CostOfSales)> AccountsOfAsync(Product product, CancellationToken cancellationToken = default)
    {
        var chart = (await accounts.ListAsync(cancellationToken)).Where(a => a.IsPosting && a.IsActive).OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).ToList();
        var inventory = product.InventoryAccountId ?? chart.FirstOrDefault(a => a.Role == AccountRole.Inventory)?.Id;
        var cost = product.CostOfSalesAccountId ?? chart.FirstOrDefault(a => a.Role == AccountRole.CostOfSales)?.Id;
        if (inventory is null || cost is null)
            throw new ValidationException([new ValidationIssue("accounts", "stock.accounts-missing")]);
        return (inventory.Value, cost.Value);
    }

    private async Task RequireAccountsAsync(StockChange change, CancellationToken cancellationToken)
    {
        var ids = change.Movements.Select(m => m.ProductId).Distinct().ToList();
        if (ids.Count == 0)
            return;

        var known = (await products.ListAsync(cancellationToken)).Where(p => ids.Contains(p.Id)).ToList();
        foreach (var product in known)
            await AccountsOfAsync(product, cancellationToken);
    }

    // ---------------------------------------------------------------- Vouchers made from the movements

    private async Task RefreshVouchersAsync(IReadOnlyList<Guid> owners, CancellationToken cancellationToken)
    {
        if (owners.Count == 0)
            return;

        var movements = await store.ListMovementsAsync(cancellationToken);
        var productList = (await products.ListAsync(cancellationToken)).ToDictionary(p => p.Id);
        var chart = (await accounts.ListAsync(cancellationToken)).Where(a => a.IsPosting && a.IsActive).OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).ToList();
        var adjustmentAccount = chart.FirstOrDefault(a => a.Role == AccountRole.InventoryAdjustment)?.Id;

        foreach (var owner in owners)
        {
            var mine = movements.Where(m => OwnerOf(m) == owner && HasVoucher(m.Kind)).ToList();

            if (mine.Any(m => m.DocumentId is not null))
            {
                if (await documents.FindAsync(owner, cancellationToken) is not { } document)
                    continue;

                var lines = await LinesAsync(mine, productList, counterAccount: null, cancellationToken);
                var id = await SyncVoucherAsync(document.CostVoucherId, document.Id, VoucherKind.StockCost, document.Date, document.Number, $"Cost of stock: {document.Number}", lines, cancellationToken);
                if (id != document.CostVoucherId)
                {
                    document.CostVoucherId = id;
                    await documents.SaveAsync([document], cancellationToken);
                }
            }
            else if (await store.FindStockDocumentAsync(owner, cancellationToken) is { } stockDocument && stockDocument.Kind != StockDocumentKind.Transfer)
            {
                var counter = stockDocument.CounterAccountId ?? adjustmentAccount;
                var lines = await LinesAsync(mine, productList, counter ?? throw new ValidationException([new ValidationIssue("counterAccount", "stock.counter-account-missing")]), cancellationToken);
                var id = await SyncVoucherAsync(stockDocument.VoucherId, null, VoucherKind.StockAdjustment, stockDocument.Date, stockDocument.Number, $"Stock: {stockDocument.Number}", lines, cancellationToken);
                if (id != stockDocument.VoucherId)
                {
                    stockDocument.VoucherId = id;
                    await store.SaveStockDocumentAsync(stockDocument, cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// The lines of the voucher: for each stock account the net value that went in (debit) or out (credit), and the other side: the cost of
    /// sales account of each product for an invoice, the counter account for a stock document. Always balanced.
    /// </summary>
    private async Task<List<VoucherLineInput>> LinesAsync(
        IReadOnlyList<StockMovement> mine, IReadOnlyDictionary<Guid, Product> productList, Guid? counterAccount, CancellationToken cancellationToken)
    {
        var net = new Dictionary<Guid, long>();
        foreach (var movement in mine)
        {
            var product = productList[movement.ProductId];
            var (inventory, cost) = await AccountsOfAsync(product, cancellationToken);
            net[inventory] = net.GetValueOrDefault(inventory) + movement.ValueScaled;
            var other = counterAccount ?? cost;
            net[other] = net.GetValueOrDefault(other) - movement.ValueScaled;
        }

        return net.Where(n => n.Value != 0)
            .Select(n => new VoucherLineInput(null, n.Key, null, n.Value > 0 ? Scaled.ToDecimal(n.Value) : 0, n.Value < 0 ? Scaled.ToDecimal(-n.Value) : 0))
            .ToList();
    }

    private async Task<Guid?> SyncVoucherAsync(
        Guid? existing, Guid? documentId, VoucherKind kind, DateOnly date, string? reference, string memo, List<VoucherLineInput> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            if (existing is { } old)
                await vouchers.DeleteSystemAsync(old, cancellationToken);
            return null;
        }

        var saved = await vouchers.SaveAndPostSystemAsync(new VoucherInput(kind, date, null, reference, memo, lines), existing, documentId, cancellationToken);
        return saved.Id;
    }
}
