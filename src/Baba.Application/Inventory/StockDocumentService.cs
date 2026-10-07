using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Application.Trade;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Inventory;

namespace Baba.Application.Inventory;

public sealed record StockLineInput(Guid ProductId, Guid WarehouseId, Guid? ToWarehouseId, decimal Quantity, decimal? UnitCost);

public sealed record StockDocumentInput(StockDocumentKind Kind, DateOnly Date, string? Memo, Guid? CounterAccountId, IReadOnlyList<StockLineInput> Lines);

public sealed record StockLineDto(Guid Id, Guid ProductId, Guid WarehouseId, Guid? ToWarehouseId, decimal Quantity, decimal? UnitCost);

public sealed record StockDocumentDto(
    Guid Id, StockDocumentKind Kind, string Number, DateOnly Date, string? Memo, Guid? CounterAccountId, Guid? VoucherId,
    IReadOnlyList<StockLineDto> Lines, decimal Value);

/// <summary>
/// Opening stock, stock adjustments (counts, losses, finds) and transfers between warehouses (brief section 10.4). An opening or an
/// adjustment is posted to the ledger (the stock account against the counter account), a transfer changes only where stock is. What a line
/// that takes stock out is worth, and a line that brings stock in without a cost, is the average cost at that date.
/// </summary>
public sealed class StockDocumentService(
    IStockStore store, IProductStore products, IAccountStore accounts, StockService stock, ICompanyFiles files, TimeProvider clock)
{
    public async Task<IReadOnlyList<StockDocumentDto>> ListAsync(StockDocumentKind? kind = null, CancellationToken cancellationToken = default)
    {
        var movements = await store.ListMovementsAsync(cancellationToken);
        return (await store.ListStockDocumentsAsync(cancellationToken))
            .Where(d => kind is null || d.Kind == kind)
            .OrderByDescending(d => d.Date).ThenByDescending(d => d.Number, StringComparer.Ordinal)
            .Select(d => ToDto(d, movements)).ToList();
    }

    public async Task<StockDocumentDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await store.FindStockDocumentAsync(id, cancellationToken) is { } document
            ? ToDto(document, await store.ListMovementsAsync(cancellationToken))
            : null;

    /// <summary>Saves a new stock document (id null) or replaces the lines and details of an existing one.</summary>
    public async Task<StockDocumentDto> SaveAsync(Guid? id, StockDocumentInput input, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var existing = id is { } existingId ? await store.FindStockDocumentAsync(existingId, cancellationToken) ?? throw new NotFoundException("stock-document") : null;
        if (existing is not null && existing.Kind != input.Kind)
            throw new ValidationException([new ValidationIssue("kind", "stockdoc.kind-cannot-change")]);

        var productList = (await products.ListAsync(cancellationToken)).ToDictionary(p => p.Id);
        var warehouses = (await store.ListWarehousesAsync(cancellationToken)).ToDictionary(w => w.Id);
        var chart = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        var issues = new List<ValidationIssue>();

        if (input.Date == default)
            issues.Add(new("date", "date.required"));
        if (input.Lines is null || input.Lines.Count == 0)
            issues.Add(new("lines", "lines.required"));
        if (input.Kind != StockDocumentKind.Transfer && input.CounterAccountId is { } counter
            && (!chart.TryGetValue(counter, out var counterAccount) || !counterAccount.IsPosting || !counterAccount.IsActive))
            issues.Add(new("counterAccount", "stockdoc.counter-account-invalid"));

        var document = existing ?? new StockDocument { CompanyId = company.Id, Kind = input.Kind };
        var oldLines = existing?.Lines.OrderBy(l => l.LineNumber).ToList() ?? [];
        var lines = new List<StockDocumentLine>();
        for (var i = 0; i < (input.Lines?.Count ?? 0); i++)
        {
            var line = input.Lines![i];
            string Field(string name) => $"lines[{i}].{name}";

            if (!productList.TryGetValue(line.ProductId, out var product) || !product.IsStockItem)
                issues.Add(new(Field("product"), "stockdoc.product-not-stock"));
            if (!warehouses.TryGetValue(line.WarehouseId, out var from) || !from.IsActive)
                issues.Add(new(Field("warehouse"), "stockdoc.warehouse-invalid"));

            switch (input.Kind)
            {
                case StockDocumentKind.Opening when line.Quantity <= 0:
                    issues.Add(new(Field("quantity"), "stockdoc.quantity-positive"));
                    break;
                case StockDocumentKind.Adjustment when line.Quantity == 0:
                    issues.Add(new(Field("quantity"), "stockdoc.quantity-zero"));
                    break;
                case StockDocumentKind.Transfer:
                    if (line.Quantity <= 0)
                        issues.Add(new(Field("quantity"), "stockdoc.quantity-positive"));
                    if (line.ToWarehouseId is not { } to || !warehouses.TryGetValue(to, out var target) || !target.IsActive)
                        issues.Add(new(Field("toWarehouse"), "stockdoc.warehouse-invalid"));
                    else if (to == line.WarehouseId)
                        issues.Add(new(Field("toWarehouse"), "stockdoc.same-warehouse"));
                    break;
            }

            if (line.UnitCost is < 0)
                issues.Add(new(Field("unitCost"), "stockdoc.cost-negative"));

            lines.Add(new StockDocumentLine
            {
                Id = i < oldLines.Count ? oldLines[i].Id : Guid.CreateVersion7(),
                CompanyId = company.Id,
                StockDocumentId = document.Id,
                LineNumber = i + 1,
                ProductId = line.ProductId,
                WarehouseId = line.WarehouseId,
                ToWarehouseId = input.Kind == StockDocumentKind.Transfer ? line.ToWarehouseId : null,
                Quantity = line.Quantity,
                UnitCost = input.Kind == StockDocumentKind.Transfer ? null : line.UnitCost,
            });
        }

        if (issues.Count > 0)
            throw new ValidationException(issues);

        document.Date = input.Date;
        document.Memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim();
        document.CounterAccountId = input.Kind == StockDocumentKind.Transfer ? null : input.CounterAccountId == Guid.Empty ? null : input.CounterAccountId;
        document.Lines = lines;
        if (existing is null)
        {
            document.Number = await store.NextStockNumberAsync(input.Kind, input.Date, cancellationToken);
            document.CreatedAt = clock.GetUtcNow().UtcDateTime;
        }

        var change = new StockChange(document.Id, IsDocument: false, document.CreatedAt == default ? clock.GetUtcNow().UtcDateTime : document.CreatedAt, MovementsOf(document));
        await stock.CheckAsync(change, cancellationToken); // nothing is saved if it would leave stock short or touch a locked month
        await store.SaveStockDocumentAsync(document, cancellationToken);
        await stock.ApplyAsync(change with { OwnerCreatedAt = (await store.FindStockDocumentAsync(document.Id, cancellationToken))!.CreatedAt }, cancellationToken);
        return (await GetAsync(document.Id, cancellationToken))!;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await store.FindStockDocumentAsync(id, cancellationToken) ?? throw new NotFoundException("stock-document");
        await stock.RemoveAsync(id, isDocument: false, cancellationToken);
        await store.DeleteStockDocumentAsync(id, cancellationToken);
    }

    private static IReadOnlyList<NewMovement> MovementsOf(StockDocument document)
    {
        var movements = new List<NewMovement>();
        foreach (var (line, i) in document.Lines.OrderBy(l => l.LineNumber).Select((l, i) => (l, i)))
        {
            switch (document.Kind)
            {
                case StockDocumentKind.Transfer:
                    movements.Add(new(i, line.ProductId, line.WarehouseId, document.Date, StockMovementKind.TransferOut, -line.QuantityScaled, CostMode.Transfer));
                    movements.Add(new(i, line.ProductId, line.ToWarehouseId!.Value, document.Date, StockMovementKind.TransferIn, line.QuantityScaled, CostMode.Transfer));
                    break;
                case StockDocumentKind.Opening:
                    movements.Add(Cost(line, document, StockMovementKind.Opening, i));
                    break;
                default:
                    movements.Add(line.QuantityScaled < 0
                        ? new NewMovement(i, line.ProductId, line.WarehouseId, document.Date, StockMovementKind.Adjustment, line.QuantityScaled, CostMode.OutAtAverage)
                        : Cost(line, document, StockMovementKind.Adjustment, i));
                    break;
            }
        }

        return movements;
    }

    /// <summary>Stock coming in: at the cost given on the line, or, when there is none, at the average cost.</summary>
    private static NewMovement Cost(StockDocumentLine line, StockDocument document, StockMovementKind kind, int index) =>
        line.UnitCost is { } unit
            ? new NewMovement(index, line.ProductId, line.WarehouseId, document.Date, kind, line.QuantityScaled, CostMode.Given, Scaled.ToScaled(Math.Round(line.Quantity * unit, 4, MidpointRounding.AwayFromZero)))
            : new NewMovement(index, line.ProductId, line.WarehouseId, document.Date, kind, line.QuantityScaled, CostMode.InAtAverage);

    private static StockDocumentDto ToDto(StockDocument d, IReadOnlyList<StockMovement> movements)
    {
        var mine = movements.Where(m => m.StockDocumentId == d.Id).ToList();
        var value = Scaled.ToDecimal(mine.Where(m => m.Kind is StockMovementKind.Opening or StockMovementKind.Adjustment).Sum(m => m.ValueScaled));
        return new StockDocumentDto(
            d.Id, d.Kind, d.Number, d.Date, d.Memo, d.CounterAccountId, d.VoucherId,
            d.Lines.OrderBy(l => l.LineNumber).Select(l => new StockLineDto(l.Id, l.ProductId, l.WarehouseId, l.ToWarehouseId, l.Quantity, l.UnitCost)).ToList(),
            value);
    }
}
