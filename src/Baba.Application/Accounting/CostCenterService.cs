using Baba.Domain.Accounting;

namespace Baba.Application.Accounting;

/// <summary>Reads and writes cost centers. Implemented by Infrastructure.</summary>
public interface ICostCenterStore
{
    Task<IReadOnlyList<CostCenter>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The cost centers that voucher lines (including drafts) point at, so they cannot simply be deleted.</summary>
    Task<IReadOnlySet<Guid>> CostCenterIdsInUseAsync(CancellationToken cancellationToken = default);

    Task AddAsync(CostCenter costCenter, CancellationToken cancellationToken = default);
    Task UpdateAsync(CostCenter costCenter, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid costCenterId, CancellationToken cancellationToken = default);
}

public sealed record CostCenterInput(string Code, string NameAr, string NameEn);

public sealed record CostCenterDto(Guid Id, string Code, string NameAr, string NameEn, bool IsActive, bool InUse);

/// <summary>Cost centers and projects (brief section 10.2): a flat list of tags that voucher lines can carry.</summary>
public sealed class CostCenterService(ICostCenterStore costCenters)
{
    public async Task<IReadOnlyList<CostCenterDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var inUse = await costCenters.CostCenterIdsInUseAsync(cancellationToken);
        return (await costCenters.ListAsync(cancellationToken))
            .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .Select(c => ToDto(c, inUse.Contains(c.Id)))
            .ToList();
    }

    public async Task<CostCenterDto> CreateAsync(CostCenterInput input, CancellationToken cancellationToken = default)
    {
        var all = await costCenters.ListAsync(cancellationToken);
        var costCenter = new CostCenter();
        Apply(costCenter, input);

        Throw(Validate(costCenter, all));
        await costCenters.AddAsync(costCenter, cancellationToken);
        return ToDto(costCenter, inUse: false);
    }

    public async Task<CostCenterDto> UpdateAsync(Guid id, CostCenterInput input, CancellationToken cancellationToken = default)
    {
        var all = await costCenters.ListAsync(cancellationToken);
        var costCenter = all.FirstOrDefault(c => c.Id == id) ?? throw NotFound();

        var changed = new CostCenter { Id = costCenter.Id, CompanyId = costCenter.CompanyId };
        Apply(changed, input);
        Throw(Validate(changed, all));

        costCenter.Code = changed.Code;
        costCenter.NameAr = changed.NameAr;
        costCenter.NameEn = changed.NameEn;
        await costCenters.UpdateAsync(costCenter, cancellationToken);
        return ToDto(costCenter, (await costCenters.CostCenterIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task<CostCenterDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var costCenter = (await costCenters.ListAsync(cancellationToken)).FirstOrDefault(c => c.Id == id) ?? throw NotFound();
        costCenter.IsActive = active;
        await costCenters.UpdateAsync(costCenter, cancellationToken);
        return ToDto(costCenter, (await costCenters.CostCenterIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if ((await costCenters.ListAsync(cancellationToken)).All(c => c.Id != id))
            throw NotFound();
        if ((await costCenters.CostCenterIdsInUseAsync(cancellationToken)).Contains(id))
            Throw([new PostingIssue("costCenter", "cost-center.in-use")]);

        await costCenters.DeleteAsync(id, cancellationToken);
    }

    private static IReadOnlyList<PostingIssue> Validate(CostCenter costCenter, IReadOnlyList<CostCenter> all)
    {
        var issues = new List<PostingIssue>();
        if (costCenter.Code.Length == 0)
            issues.Add(new("code", "cost-center.code-required"));
        else if (all.Any(c => c.Id != costCenter.Id && string.Equals(c.Code, costCenter.Code, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("code", "cost-center.code-duplicate"));
        if (costCenter.NameAr.Length == 0 && costCenter.NameEn.Length == 0)
            issues.Add(new("name", "cost-center.name-required"));
        return issues;
    }

    private static void Apply(CostCenter costCenter, CostCenterInput input)
    {
        costCenter.Code = input.Code?.Trim() ?? "";
        costCenter.NameAr = input.NameAr?.Trim() ?? "";
        costCenter.NameEn = input.NameEn?.Trim() ?? "";
        if (costCenter.NameAr.Length == 0) costCenter.NameAr = costCenter.NameEn;
        if (costCenter.NameEn.Length == 0) costCenter.NameEn = costCenter.NameAr;
    }

    private static CostCenterDto ToDto(CostCenter c, bool inUse) => new(c.Id, c.Code, c.NameAr, c.NameEn, c.IsActive, inUse);

    private static void Throw(IReadOnlyCollection<PostingIssue> issues)
    {
        if (issues.Count > 0)
            throw new ValidationException(issues.Select(i => new ValidationIssue(i.Field, i.Code)).ToList());
    }

    private static NotFoundException NotFound() => new("cost-center");
}
