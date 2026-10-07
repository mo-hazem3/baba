using Baba.Application.Companies;
using Baba.Domain.Inventory;

namespace Baba.Application.Inventory;

public sealed record WarehouseInput(string Code, string NameAr, string NameEn);

public sealed record WarehouseDto(Guid Id, string Code, string NameAr, string NameEn, bool IsActive, bool IsDefault, bool InUse);

/// <summary>
/// The company's warehouses (brief section 10.4). There is always at least one: the first time any are asked for a default one is made,
/// so a company with a single place for its stock never has to think about warehouses.
/// </summary>
public sealed class WarehouseService(IStockStore store, ICompanyFiles files)
{
    private readonly SemaphoreSlim _creating = new(1, 1);

    public async Task<IReadOnlyList<WarehouseDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultAsync(cancellationToken);
        var inUse = await store.WarehouseIdsInUseAsync(cancellationToken);
        return (await store.ListWarehousesAsync(cancellationToken))
            .OrderBy(w => w.Code, StringComparer.OrdinalIgnoreCase).Select(w => ToDto(w, inUse.Contains(w.Id))).ToList();
    }

    /// <summary>The warehouse documents start with; makes the first one when the company has none.</summary>
    public async Task<Warehouse> EnsureDefaultAsync(CancellationToken cancellationToken = default)
    {
        await _creating.WaitAsync(cancellationToken);
        try
        {
            var all = await store.ListWarehousesAsync(cancellationToken);
            var found = all.FirstOrDefault(w => w is { IsDefault: true, IsActive: true }) ?? all.FirstOrDefault(w => w.IsActive);
            if (found is not null)
                return found;

            var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
            var main = new Warehouse { CompanyId = company.Id, Code = "MAIN", NameEn = "Main warehouse", NameAr = "المستودع الرئيسي", IsDefault = true };
            await store.AddWarehouseAsync(main, cancellationToken);
            return main;
        }
        finally
        {
            _creating.Release();
        }
    }

    public async Task<WarehouseDto> CreateAsync(WarehouseInput input, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        await EnsureDefaultAsync(cancellationToken);
        var all = await store.ListWarehousesAsync(cancellationToken);
        var warehouse = new Warehouse { CompanyId = company.Id };
        Apply(warehouse, input);
        Throw(Validate(warehouse, all));
        await store.AddWarehouseAsync(warehouse, cancellationToken);
        return ToDto(warehouse, false);
    }

    public async Task<WarehouseDto> UpdateAsync(Guid id, WarehouseInput input, CancellationToken cancellationToken = default)
    {
        var all = await store.ListWarehousesAsync(cancellationToken);
        var warehouse = all.FirstOrDefault(w => w.Id == id) ?? throw new NotFoundException("warehouse");
        Apply(warehouse, input);
        Throw(Validate(warehouse, all));
        await store.UpdateWarehousesAsync([warehouse], cancellationToken);
        return ToDto(warehouse, (await store.WarehouseIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task<WarehouseDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var all = await store.ListWarehousesAsync(cancellationToken);
        var warehouse = all.FirstOrDefault(w => w.Id == id) ?? throw new NotFoundException("warehouse");
        if (!active && all.Count(w => w.IsActive && w.Id != id) == 0)
            throw Refused("warehouse", "warehouse.last-one");

        warehouse.IsActive = active;
        var changed = new List<Warehouse> { warehouse };
        if (!active && warehouse.IsDefault)
        {
            warehouse.IsDefault = false;
            var next = all.First(w => w.IsActive && w.Id != id);
            next.IsDefault = true;
            changed.Add(next);
        }

        await store.UpdateWarehousesAsync(changed, cancellationToken);
        return ToDto(warehouse, (await store.WarehouseIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task<WarehouseDto> SetDefaultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var all = await store.ListWarehousesAsync(cancellationToken);
        var warehouse = all.FirstOrDefault(w => w.Id == id) ?? throw new NotFoundException("warehouse");
        if (!warehouse.IsActive)
            throw Refused("warehouse", "warehouse.inactive");

        var others = all.Where(w => w.IsDefault && w.Id != id).ToList();
        foreach (var other in others)
            other.IsDefault = false;
        warehouse.IsDefault = true;
        await store.UpdateWarehousesAsync([.. others, warehouse], cancellationToken);
        return ToDto(warehouse, (await store.WarehouseIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var all = await store.ListWarehousesAsync(cancellationToken);
        var warehouse = all.FirstOrDefault(w => w.Id == id) ?? throw new NotFoundException("warehouse");
        if ((await store.WarehouseIdsInUseAsync(cancellationToken)).Contains(id))
            throw Refused("warehouse", "warehouse.in-use");
        if (all.Count(w => w.Id != id) == 0 || warehouse.IsDefault && all.Count(w => w.IsActive && w.Id != id) == 0)
            throw Refused("warehouse", "warehouse.last-one");

        await store.DeleteWarehouseAsync(id, cancellationToken);
    }

    private static void Apply(Warehouse warehouse, WarehouseInput input)
    {
        warehouse.Code = input.Code?.Trim() ?? "";
        warehouse.NameAr = input.NameAr?.Trim() ?? "";
        warehouse.NameEn = input.NameEn?.Trim() ?? "";
        if (warehouse.NameAr.Length == 0) warehouse.NameAr = warehouse.NameEn;
        if (warehouse.NameEn.Length == 0) warehouse.NameEn = warehouse.NameAr;
    }

    private static IReadOnlyList<ValidationIssue> Validate(Warehouse warehouse, IReadOnlyList<Warehouse> all)
    {
        var issues = new List<ValidationIssue>();
        if (warehouse.Code.Length == 0)
            issues.Add(new("code", "warehouse.code-required"));
        else if (all.Any(w => w.Id != warehouse.Id && string.Equals(w.Code, warehouse.Code, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("code", "warehouse.code-duplicate"));
        if (warehouse.NameAr.Length == 0 && warehouse.NameEn.Length == 0)
            issues.Add(new("name", "warehouse.name-required"));
        return issues;
    }

    private static WarehouseDto ToDto(Warehouse w, bool inUse) => new(w.Id, w.Code, w.NameAr, w.NameEn, w.IsActive, w.IsDefault, inUse);

    private static ValidationException Refused(string field, string code) => new([new ValidationIssue(field, code)]);

    private static void Throw(IReadOnlyCollection<ValidationIssue> issues)
    {
        if (issues.Count > 0)
            throw new ValidationException(issues.ToList());
    }
}
