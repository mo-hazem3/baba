using Baba.Domain.Inventory;

namespace Baba.Application.Inventory;

/// <summary>Reads and writes warehouses, stock movements and stock documents. Implemented by Infrastructure.</summary>
public interface IStockStore
{
    // Warehouses
    Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(CancellationToken cancellationToken = default);
    Task AddWarehouseAsync(Warehouse warehouse, CancellationToken cancellationToken = default);

    /// <summary>Saves changes to several warehouses together (making one the default changes two).</summary>
    Task UpdateWarehousesAsync(IReadOnlyList<Warehouse> warehouses, CancellationToken cancellationToken = default);

    Task DeleteWarehouseAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The ids of warehouses that have movements, documents or stock documents.</summary>
    Task<IReadOnlySet<Guid>> WarehouseIdsInUseAsync(CancellationToken cancellationToken = default);

    // Movements
    Task<IReadOnlyList<StockMovement>> ListMovementsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// In one step: removes the movements of an invoice or stock document, adds its new ones, and changes the stored value of other movements
    /// that were worked out again.
    /// </summary>
    Task ReplaceMovementsAsync(
        Guid ownerId, IReadOnlyList<StockMovement> added, IReadOnlyDictionary<Guid, long> valueChanges, CancellationToken cancellationToken = default);

    // Stock documents
    Task<StockDocument?> FindStockDocumentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockDocument>> ListStockDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a stock document, or replaces the stored one with the same id (its lines are replaced by the new lines).</summary>
    Task SaveStockDocumentAsync(StockDocument document, CancellationToken cancellationToken = default);

    Task DeleteStockDocumentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<string> NextStockNumberAsync(StockDocumentKind kind, DateOnly date, CancellationToken cancellationToken = default);
}
