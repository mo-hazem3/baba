using Baba.Application.Inventory;
using Baba.Domain.Accounting;
using Baba.Domain.Inventory;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Inventory;

public sealed class StockStore(ICompanyDbContextFactory contexts) : IStockStore
{
    // ---------------------------------------------------------------- Warehouses

    public async Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.Warehouses.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task AddWarehouseAsync(Warehouse warehouse, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateWarehousesAsync(IReadOnlyList<Warehouse> warehouses, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var ids = warehouses.Select(w => w.Id).ToList();
        var stored = await context.Warehouses.Where(w => ids.Contains(w.Id)).ToDictionaryAsync(w => w.Id, cancellationToken);
        foreach (var warehouse in warehouses)
            context.Entry(stored[warehouse.Id]).CurrentValues.SetValues(warehouse);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteWarehouseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Warehouses.Remove(await context.Warehouses.SingleAsync(w => w.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> WarehouseIdsInUseAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var used = await context.StockMovements.Select(m => m.WarehouseId).Distinct().ToListAsync(cancellationToken);
        used.AddRange(await context.StockDocumentLines.Select(l => l.WarehouseId).Distinct().ToListAsync(cancellationToken));
        used.AddRange(await context.StockDocumentLines.Where(l => l.ToWarehouseId != null).Select(l => l.ToWarehouseId!.Value).Distinct().ToListAsync(cancellationToken));
        used.AddRange(await context.Documents.Where(d => d.WarehouseId != null).Select(d => d.WarehouseId!.Value).Distinct().ToListAsync(cancellationToken));
        return used.ToHashSet();
    }

    // ---------------------------------------------------------------- Movements

    public async Task<IReadOnlyList<StockMovement>> ListMovementsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.StockMovements.AsNoTracking().OrderBy(m => m.Date).ThenBy(m => m.SourceCreatedAt).ThenBy(m => m.Id).ToListAsync(cancellationToken);
    }

    public async Task ReplaceMovementsAsync(
        Guid ownerId, IReadOnlyList<StockMovement> added, IReadOnlyDictionary<Guid, long> valueChanges, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.StockMovements.Where(m => m.DocumentId == ownerId || m.StockDocumentId == ownerId).ExecuteDeleteAsync(cancellationToken);
        foreach (var (id, value) in valueChanges)
            await context.StockMovements.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.ValueScaled, value), cancellationToken);
        context.StockMovements.AddRange(added);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- Stock documents

    public async Task<StockDocument?> FindStockDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var document = await context.StockDocuments.AsNoTracking().Include(d => d.Lines).SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        document?.Lines.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));
        return document;
    }

    public async Task<IReadOnlyList<StockDocument>> ListStockDocumentsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var all = await context.StockDocuments.AsNoTracking().Include(d => d.Lines).ToListAsync(cancellationToken);
        foreach (var document in all)
            document.Lines.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));
        return all;
    }

    public async Task SaveStockDocumentAsync(StockDocument document, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var stored = await context.StockDocuments.Include(d => d.Lines).SingleOrDefaultAsync(d => d.Id == document.Id, cancellationToken);
        if (stored is null)
        {
            context.StockDocuments.Add(document);
        }
        else
        {
            context.Entry(stored).CurrentValues.SetValues(document);
            var incoming = document.Lines.ToDictionary(l => l.Id);
            foreach (var old in stored.Lines.Where(l => !incoming.ContainsKey(l.Id)).ToList())
                context.StockDocumentLines.Remove(old);
            foreach (var line in document.Lines)
            {
                var existing = stored.Lines.FirstOrDefault(l => l.Id == line.Id);
                if (existing is null)
                    context.StockDocumentLines.Add(line);
                else
                    context.Entry(existing).CurrentValues.SetValues(line);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteStockDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.StockDocuments.Remove(await context.StockDocuments.Include(d => d.Lines).SingleAsync(d => d.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> NextStockNumberAsync(StockDocumentKind kind, DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var startMonth = await context.Companies.Select(c => c.FiscalYearStartMonth).SingleAsync(cancellationToken);
        var fiscalYear = FiscalYear.Of(date, startMonth);
        var name = "Stock" + kind;

        var sequence = await context.NumberSequences.SingleOrDefaultAsync(s => s.Kind == name && s.FiscalYear == fiscalYear, cancellationToken);
        if (sequence is null)
        {
            sequence = new NumberSequence { Kind = name, FiscalYear = fiscalYear };
            context.NumberSequences.Add(sequence);
        }

        sequence.LastNumber++;
        await context.SaveChangesAsync(cancellationToken);
        var prefix = kind switch { StockDocumentKind.Opening => "OS", StockDocumentKind.Adjustment => "AD", _ => "TR" };
        return $"{prefix}-{fiscalYear}-{sequence.LastNumber:0000}";
    }
}
