using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Trade;
using Baba.Localization;

namespace Baba.Application.Trade;

/// <summary>Reads and writes the company's tax codes. Implemented by Infrastructure.</summary>
public interface ITaxCodeStore
{
    Task<IReadOnlyList<TaxCode>> ListAsync(CancellationToken cancellationToken = default);
    Task AddAsync(IReadOnlyList<TaxCode> codes, CancellationToken cancellationToken = default);

    /// <summary>Saves changes to several codes together (setting the default changes two).</summary>
    Task UpdateAsync(IReadOnlyList<TaxCode> codes, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The ids of codes that a document line or a product uses.</summary>
    Task<IReadOnlySet<Guid>> InUseAsync(CancellationToken cancellationToken = default);
}

public sealed record TaxCodeInput(
    string Code,
    string NameAr,
    string NameEn,
    decimal Rate,
    TaxTreatment Treatment,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    Guid? OutputAccountId,
    Guid? InputAccountId);

public sealed record TaxCodeDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    decimal Rate,
    TaxTreatment Treatment,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    Guid? OutputAccountId,
    Guid? InputAccountId,
    bool IsActive,
    bool IsDefault,
    bool FromPack,
    bool InUse);

/// <summary>
/// The company's tax codes (brief sections 8 and 10.3). The codes of the company's country come from its pack the first time they are
/// asked for (and any that are missing are added later), pointing at the company's tax accounts. The company can switch codes off,
/// choose the default, change the accounts, and add codes of its own. A code that came with the pack keeps its rate, treatment and
/// dates; documents keep their own copy of the rate in any case.
/// </summary>
public sealed class TaxService(ITaxCodeStore codes, IAccountStore accounts, CountryPackRegistry countryPacks, ICompanyFiles files)
{
    // Several screens ask for the codes at the same moment when a company is first shown; only one of them may create the missing ones.
    private readonly SemaphoreSlim _seeding = new(1, 1);

    public async Task<IReadOnlyList<TaxCodeDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsurePackCodesAsync(cancellationToken);
        var inUse = await codes.InUseAsync(cancellationToken);
        return (await codes.ListAsync(cancellationToken))
            .OrderBy(c => c.Treatment).ThenByDescending(c => c.Rate).ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .Select(c => ToDto(c, inUse.Contains(c.Id))).ToList();
    }

    public async Task<TaxCodeDto> CreateAsync(TaxCodeInput input, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var all = await codes.ListAsync(cancellationToken);
        var code = new TaxCode { CompanyId = company.Id };
        Apply(code, input);
        Throw(await ValidateAsync(code, all, cancellationToken));
        await codes.AddAsync([code], cancellationToken);
        return ToDto(code, false);
    }

    public async Task<TaxCodeDto> UpdateAsync(Guid id, TaxCodeInput input, CancellationToken cancellationToken = default)
    {
        var all = await codes.ListAsync(cancellationToken);
        var code = all.FirstOrDefault(c => c.Id == id) ?? throw new NotFoundException("tax-code");

        if (code.FromPack)
        {
            // The law decides these; the company only chooses names and accounts.
            code.NameAr = input.NameAr?.Trim() ?? code.NameAr;
            code.NameEn = input.NameEn?.Trim() ?? code.NameEn;
            code.OutputAccountId = Clean(input.OutputAccountId);
            code.InputAccountId = Clean(input.InputAccountId);
        }
        else
        {
            Apply(code, input);
        }

        Throw(await ValidateAsync(code, all, cancellationToken));
        await codes.UpdateAsync([code], cancellationToken);
        return ToDto(code, (await codes.InUseAsync(cancellationToken)).Contains(id));
    }

    public async Task<TaxCodeDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var code = (await codes.ListAsync(cancellationToken)).FirstOrDefault(c => c.Id == id) ?? throw new NotFoundException("tax-code");
        code.IsActive = active;
        if (!active)
            code.IsDefault = false;
        await codes.UpdateAsync([code], cancellationToken);
        return ToDto(code, (await codes.InUseAsync(cancellationToken)).Contains(id));
    }

    /// <summary>Makes this code the one new document lines start with, and no other.</summary>
    public async Task<TaxCodeDto> SetDefaultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var all = await codes.ListAsync(cancellationToken);
        var code = all.FirstOrDefault(c => c.Id == id) ?? throw new NotFoundException("tax-code");
        if (!code.IsActive)
            throw Refused("code", "tax.inactive");

        var changed = all.Where(c => c.IsDefault && c.Id != id).ToList();
        foreach (var other in changed)
            other.IsDefault = false;
        code.IsDefault = true;
        await codes.UpdateAsync([.. changed, code], cancellationToken);
        return ToDto(code, (await codes.InUseAsync(cancellationToken)).Contains(id));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var code = (await codes.ListAsync(cancellationToken)).FirstOrDefault(c => c.Id == id) ?? throw new NotFoundException("tax-code");
        if (code.FromPack)
            throw Refused("code", "tax.pack-code");
        if ((await codes.InUseAsync(cancellationToken)).Contains(id))
            throw Refused("code", "tax.in-use");
        await codes.DeleteAsync(id, cancellationToken);
    }

    // ---------------------------------------------------------------- Pack codes

    /// <summary>Adds the tax codes of the company's country that the company does not have yet, and fills in tax accounts that were missing.</summary>
    private async Task EnsurePackCodesAsync(CancellationToken cancellationToken)
    {
        await _seeding.WaitAsync(cancellationToken);
        try
        {
            await SeedAsync(cancellationToken);
        }
        finally
        {
            _seeding.Release();
        }
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var pack = countryPacks.Find(company.CountryCode);
        if (pack is null || pack.TaxCodes.Count == 0)
            return;

        var existing = (await codes.ListAsync(cancellationToken)).ToList();
        var chart = (await accounts.ListAsync(cancellationToken)).Where(a => a.IsPosting && a.IsActive).OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).ToList();
        var output = chart.FirstOrDefault(a => a.Role == AccountRole.TaxPayable)?.Id;
        var input = chart.FirstOrDefault(a => a.Role == AccountRole.TaxReceivable)?.Id;

        var added = new List<TaxCode>();
        foreach (var definition in pack.TaxCodes.Where(d => !existing.Any(e => string.Equals(e.Code, d.Code, StringComparison.OrdinalIgnoreCase))))
        {
            var treatment = definition.Category switch
            {
                TaxCategory.Zero => TaxTreatment.Zero,
                TaxCategory.Exempt => TaxTreatment.Exempt,
                TaxCategory.OutOfScope => TaxTreatment.OutOfScope,
                _ => TaxTreatment.Standard,
            };
            added.Add(new TaxCode
            {
                CompanyId = company.Id,
                Code = definition.Code,
                NameAr = definition.NameAr,
                NameEn = definition.NameEn,
                Rate = definition.Rate,
                Treatment = treatment,
                EffectiveFrom = definition.EffectiveFrom,
                EffectiveTo = definition.EffectiveTo,
                OutputAccountId = output,
                InputAccountId = input,
                FromPack = true,
                // The first standard-rate code is what lines start with, until the company chooses another.
                IsDefault = treatment == TaxTreatment.Standard && !existing.Any(e => e.IsDefault) && !added.Any(a => a.IsDefault),
            });
        }

        var filled = existing.Where(e => e.FromPack && ((e.OutputAccountId is null && output is not null) || (e.InputAccountId is null && input is not null))).ToList();
        foreach (var code in filled)
        {
            code.OutputAccountId ??= output;
            code.InputAccountId ??= input;
        }

        if (filled.Count > 0)
            await codes.UpdateAsync(filled, cancellationToken);
        if (added.Count > 0)
            await codes.AddAsync(added, cancellationToken);
    }

    // ---------------------------------------------------------------- Checking

    private static void Apply(TaxCode code, TaxCodeInput input)
    {
        code.Code = input.Code?.Trim() ?? "";
        code.NameAr = input.NameAr?.Trim() ?? "";
        code.NameEn = input.NameEn?.Trim() ?? "";
        code.Treatment = input.Treatment;
        code.Rate = input.Treatment == TaxTreatment.Standard ? input.Rate : 0m; // only a standard code has a rate
        code.EffectiveFrom = input.EffectiveFrom;
        code.EffectiveTo = input.EffectiveTo;
        code.OutputAccountId = Clean(input.OutputAccountId);
        code.InputAccountId = Clean(input.InputAccountId);
        if (code.NameAr.Length == 0) code.NameAr = code.NameEn;
        if (code.NameEn.Length == 0) code.NameEn = code.NameAr;
    }

    private async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(TaxCode code, IReadOnlyList<TaxCode> all, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        if (code.Code.Length == 0)
            issues.Add(new("code", "tax.code-required"));
        else if (all.Any(c => c.Id != code.Id && string.Equals(c.Code, code.Code, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("code", "tax.code-duplicate"));
        if (code.NameAr.Length == 0 && code.NameEn.Length == 0)
            issues.Add(new("name", "tax.name-required"));
        if (code.Rate is < 0 or > 100)
            issues.Add(new("rate", "tax.rate-invalid"));
        if (code.EffectiveFrom is { } from && code.EffectiveTo is { } to && to < from)
            issues.Add(new("effectiveTo", "tax.dates-invalid"));

        var chart = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        if (code.OutputAccountId is { } o && (!chart.TryGetValue(o, out var oa) || !oa.IsPosting || oa.Type != AccountType.Liability))
            issues.Add(new("outputAccount", "tax.account-invalid"));
        if (code.InputAccountId is { } i && (!chart.TryGetValue(i, out var ia) || !ia.IsPosting || ia.Type != AccountType.Asset))
            issues.Add(new("inputAccount", "tax.account-invalid"));
        return issues;
    }

    private static Guid? Clean(Guid? id) => id == Guid.Empty ? null : id;

    private static TaxCodeDto ToDto(TaxCode c, bool inUse) =>
        new(c.Id, c.Code, c.NameAr, c.NameEn, c.Rate, c.Treatment, c.EffectiveFrom, c.EffectiveTo, c.OutputAccountId, c.InputAccountId, c.IsActive, c.IsDefault, c.FromPack, inUse);

    private static ValidationException Refused(string field, string code) => new([new ValidationIssue(field, code)]);

    private static void Throw(IReadOnlyCollection<ValidationIssue> issues)
    {
        if (issues.Count > 0)
            throw new ValidationException(issues.ToList());
    }
}
