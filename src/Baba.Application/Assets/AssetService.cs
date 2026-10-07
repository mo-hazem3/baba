using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Assets;
using Baba.Localization;

namespace Baba.Application.Assets;

public interface IAssetStore
{
    Task<IReadOnlyList<FixedAsset>> ListAsync(CancellationToken cancellationToken = default);
    Task<FixedAsset?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(FixedAsset asset, CancellationToken cancellationToken = default);
    Task UpdateAsync(FixedAsset asset, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssetDepreciation>> ListDepreciationsAsync(CancellationToken cancellationToken = default);
    Task AddDepreciationsAsync(IReadOnlyList<AssetDepreciation> rows, CancellationToken cancellationToken = default);
    Task DeleteDepreciationsOfVoucherAsync(Guid voucherId, CancellationToken cancellationToken = default);
}

public sealed record AssetInput(
    string Code,
    string NameAr,
    string NameEn,
    AssetKind Kind,
    DateOnly AcquisitionDate,
    decimal Cost,
    decimal Salvage,
    int UsefulLifeMonths,
    DepreciationMethod Method,
    decimal AnnualRate,
    Guid? AssetAccountId,
    Guid? AccumulatedAccountId,
    Guid? ExpenseAccountId,
    Guid? CostCenterId = null,
    decimal OpeningAccumulated = 0,
    DateOnly? DepreciatedThrough = null,
    string? Notes = null);

public sealed record AssetDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    AssetKind Kind,
    DateOnly AcquisitionDate,
    decimal Cost,
    decimal Salvage,
    int UsefulLifeMonths,
    DepreciationMethod Method,
    decimal AnnualRate,
    Guid AssetAccountId,
    Guid AccumulatedAccountId,
    Guid ExpenseAccountId,
    Guid? CostCenterId,
    decimal OpeningAccumulated,
    DateOnly? DepreciatedThrough,
    AssetStatus Status,
    DateOnly? DisposalDate,
    decimal DisposalProceeds,
    string? Notes,
    /// <summary>Depreciation so far: what was there when the books started plus what Baba has posted.</summary>
    decimal Accumulated,
    decimal BookValue,
    /// <summary>The first day of the last month depreciated, if any.</summary>
    DateOnly? LastDepreciatedMonth,
    /// <summary>It has depreciation or a disposal, so its terms can no longer change.</summary>
    bool IsLocked);

public sealed record DisposeInput(DateOnly Date, decimal Proceeds, Guid? ProceedsAccountId, Guid? GainLossAccountId);

/// <summary>What a depreciation run did for one month: the voucher it made, or why it could not.</summary>
public sealed record DepreciationRunItem(DateOnly Month, Guid? VoucherId, int Assets, decimal Amount, string? ProblemCode);

public sealed record DepreciationRunResult(IReadOnlyList<DepreciationRunItem> Items);

/// <summary>
/// The fixed and intangible asset register (brief section 10.4). Depreciation is posted one voucher per month, dated the last day of
/// the month, for every asset that still has something to depreciate. A run catches up all the months that are due, so a month that
/// was missed is made good the next time, and the run that happens when a company is opened keeps the books current without anyone
/// remembering to do it.
/// </summary>
public sealed class AssetService(
    IAssetStore store,
    IAccountStore accounts,
    ICostCenterStore costCenters,
    VoucherService vouchers,
    ICompanyFiles files,
    TimeProvider clock)
{
    // ---------------------------------------------------------------- Register

    public async Task<IReadOnlyList<AssetDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = (await store.ListDepreciationsAsync(cancellationToken)).ToLookup(r => r.AssetId);
        return (await store.ListAsync(cancellationToken)).OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).Select(a => ToDto(a, [.. rows[a.Id]])).ToList();
    }

    public async Task<AssetDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var asset = await store.FindAsync(id, cancellationToken);
        if (asset is null)
            return null;
        var rows = (await store.ListDepreciationsAsync(cancellationToken)).Where(r => r.AssetId == id).ToList();
        return ToDto(asset, rows);
    }

    public async Task<AssetDto> CreateAsync(AssetInput input, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var asset = new FixedAsset { CompanyId = company.Id };
        await ApplyAsync(asset, input, termsLocked: false, cancellationToken);
        await store.AddAsync(asset, cancellationToken);
        return ToDto(asset, []);
    }

    public async Task<AssetDto> UpdateAsync(Guid id, AssetInput input, CancellationToken cancellationToken = default)
    {
        var asset = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("asset");
        var rows = (await store.ListDepreciationsAsync(cancellationToken)).Where(r => r.AssetId == id).ToList();
        await ApplyAsync(asset, input, termsLocked: rows.Count > 0 || asset.Status == AssetStatus.Disposed, cancellationToken);
        await store.UpdateAsync(asset, cancellationToken);
        return ToDto(asset, rows);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var asset = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("asset");
        if (asset.Status == AssetStatus.Disposed || (await store.ListDepreciationsAsync(cancellationToken)).Any(r => r.AssetId == id))
            throw Refused("asset", "asset.in-use");
        await store.DeleteAsync(id, cancellationToken);
    }

    /// <summary>Everything about the accounts and amounts of an asset is checked here, once, for both new and changed assets.</summary>
    private async Task ApplyAsync(FixedAsset asset, AssetInput input, bool termsLocked, CancellationToken cancellationToken)
    {
        var chart = (await accounts.ListAsync(cancellationToken)).ToList();
        var byId = chart.ToDictionary(a => a.Id);
        var issues = new List<ValidationIssue>();
        var all = await store.ListAsync(cancellationToken);

        var code = input.Code?.Trim() ?? "";
        if (code.Length == 0)
            issues.Add(new("code", "asset.code-required"));
        else if (all.Any(a => a.Id != asset.Id && string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("code", "asset.code-duplicate"));

        var nameEn = input.NameEn?.Trim() ?? "";
        var nameAr = input.NameAr?.Trim() ?? "";
        if (nameEn.Length == 0 && nameAr.Length == 0)
            issues.Add(new("name", "asset.name-required"));
        if (nameAr.Length == 0) nameAr = nameEn;
        if (nameEn.Length == 0) nameEn = nameAr;

        if (input.CostCenterId is { } center && center != Guid.Empty && !(await costCenters.ListAsync(cancellationToken)).Any(c => c.Id == center))
            issues.Add(new("costCenter", "asset.cost-center-unknown"));

        // The accounts default to the ones with the matching special use.
        Guid? Pick(Guid? chosen, Func<Account, bool> fits) =>
            chosen is { } id && id != Guid.Empty ? id : chart.Where(a => a.IsPosting && a.IsActive && fits(a)).OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.Id;
        var assetAccount = input.AssetAccountId is { } given && given != Guid.Empty ? given : (Guid?)null;
        var accumulated = Pick(input.AccumulatedAccountId, a => a.Role == AccountRole.AccumulatedDepreciation);
        var expense = Pick(input.ExpenseAccountId, a => a.Role == AccountRole.DepreciationExpense);

        void CheckAccount(string field, Guid? id, AccountType type)
        {
            if (id is not { } value || !byId.TryGetValue(value, out var account) || !account.IsPosting || !account.IsActive || account.Type != type)
                issues.Add(new(field, "asset.account-invalid"));
        }

        CheckAccount("assetAccount", assetAccount, AccountType.Asset);
        CheckAccount("accumulatedAccount", accumulated, AccountType.Asset);
        CheckAccount("expenseAccount", expense, AccountType.Expense);
        if (assetAccount is not null && assetAccount == accumulated)
            issues.Add(new("accumulatedAccount", "asset.accounts-same"));

        if (input.AcquisitionDate == default)
            issues.Add(new("acquisitionDate", "asset.date-required"));
        if (input.Cost <= 0)
            issues.Add(new("cost", "asset.cost-invalid"));
        if (input.Salvage < 0 || input.Salvage >= Math.Max(input.Cost, 0.0001m))
            issues.Add(new("salvage", "asset.salvage-invalid"));
        if (input.Method == DepreciationMethod.StraightLine && input.UsefulLifeMonths <= 0)
            issues.Add(new("usefulLife", "asset.life-invalid"));
        if (input.Method == DepreciationMethod.DecliningBalance && input.AnnualRate is <= 0 or > 100)
            issues.Add(new("annualRate", "asset.rate-invalid"));
        if (input.OpeningAccumulated < 0 || input.OpeningAccumulated > input.Cost - input.Salvage)
            issues.Add(new("openingAccumulated", "asset.opening-invalid"));

        if (termsLocked)
        {
            // Once depreciation is posted, what it was worked out from must not move under it.
            var changed = asset.AcquisitionDate != input.AcquisitionDate || asset.Cost != input.Cost || asset.Salvage != input.Salvage
                || asset.UsefulLifeMonths != input.UsefulLifeMonths || asset.Method != input.Method || asset.AnnualRate != input.AnnualRate
                || asset.AssetAccountId != assetAccount || asset.AccumulatedAccountId != accumulated || asset.ExpenseAccountId != expense
                || asset.OpeningAccumulated != input.OpeningAccumulated || asset.DepreciatedThrough != Month(input.DepreciatedThrough);
            if (changed)
                issues.Add(new("asset", "asset.terms-locked"));
        }

        if (issues.Count > 0)
            throw new ValidationException(issues);

        asset.Code = code;
        asset.NameAr = nameAr;
        asset.NameEn = nameEn;
        asset.CostCenterId = input.CostCenterId == Guid.Empty ? null : input.CostCenterId;
        asset.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        if (termsLocked)
            return;

        asset.Kind = input.Kind;
        asset.AcquisitionDate = input.AcquisitionDate;
        asset.Cost = input.Cost;
        asset.Salvage = input.Salvage;
        asset.UsefulLifeMonths = input.Method == DepreciationMethod.StraightLine ? input.UsefulLifeMonths : Math.Max(input.UsefulLifeMonths, 0);
        asset.Method = input.Method;
        asset.AnnualRate = input.Method == DepreciationMethod.DecliningBalance ? input.AnnualRate : 0;
        asset.AssetAccountId = assetAccount!.Value;
        asset.AccumulatedAccountId = accumulated!.Value;
        asset.ExpenseAccountId = expense!.Value;
        asset.OpeningAccumulated = input.OpeningAccumulated;
        asset.DepreciatedThrough = Month(input.DepreciatedThrough);
    }

    // ---------------------------------------------------------------- Depreciation

    /// <summary>The last day of the month before the current one: months that have ended can be depreciated without asking.</summary>
    public DateOnly LastCompletedMonthEnd() => FirstOfMonth(DateOnly.FromDateTime(clock.GetLocalNow().DateTime)).AddDays(-1);

    /// <summary>Posts depreciation for every month up to and including the month of <paramref name="through"/> that is not yet done.</summary>
    public Task<DepreciationRunResult> RunDepreciationAsync(DateOnly through, CancellationToken cancellationToken = default) =>
        RunAsync(through, null, cancellationToken);

    /// <summary>The run that happens when a company is opened: the months that have ended.</summary>
    public Task<DepreciationRunResult> RunDueAsync(CancellationToken cancellationToken = default) => RunAsync(LastCompletedMonthEnd(), null, cancellationToken);

    private async Task<DepreciationRunResult> RunAsync(DateOnly through, Guid? onlyAsset, CancellationToken cancellationToken)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var currency = CurrencyCatalog.Find(company.BaseCurrencyCode)?.Currency ?? new Currency(company.BaseCurrencyCode, 2);
        var items = new List<DepreciationRunItem>();

        var assets = (await store.ListAsync(cancellationToken)).Where(a => onlyAsset is null || a.Id == onlyAsset).ToList();
        var rows = (await store.ListDepreciationsAsync(cancellationToken)).ToList();
        var accumulated = assets.ToDictionary(a => a.Id, a => a.OpeningAccumulated + rows.Where(r => r.AssetId == a.Id).Sum(r => r.Amount));
        var lastMonth = assets.ToDictionary(a => a.Id, a => LastMonthOf(a, rows));
        var lastToDo = FirstOfMonth(through);

        // The earliest month any asset still needs.
        var due = assets.Where(a => a.Status == AssetStatus.Active && accumulated[a.Id] < a.Cost - a.Salvage).Select(a => lastMonth[a.Id].AddMonths(1)).ToList();
        if (due.Count == 0)
            return new DepreciationRunResult(items);

        for (var month = due.Min(); month <= lastToDo; month = month.AddMonths(1))
        {
            var lines = new List<VoucherLineInput>();
            var made = new List<(FixedAsset Asset, decimal Amount)>();
            foreach (var asset in assets.Where(a => a.Status == AssetStatus.Active && lastMonth[a.Id].AddMonths(1) == month))
            {
                var amount = DepreciationEngine.Next(
                    asset.Method, asset.Cost, asset.Salvage, asset.UsefulLifeMonths, asset.AnnualRate, accumulated[asset.Id],
                    DepreciationEngine.MonthNumber(asset.AcquisitionDate, month), currency);
                if (amount <= 0)
                    continue;

                var label = $"{asset.Code} {asset.NameEn}".Trim();
                lines.Add(new VoucherLineInput(null, asset.ExpenseAccountId, label, amount, 0, null, asset.CostCenterId));
                lines.Add(new VoucherLineInput(null, asset.AccumulatedAccountId, label, 0, amount));
                made.Add((asset, amount));
            }

            if (made.Count == 0)
                continue;

            var monthEnd = month.AddMonths(1).AddDays(-1);
            try
            {
                var voucher = await vouchers.SaveAndPostSystemAsync(
                    new VoucherInput(VoucherKind.Depreciation, monthEnd, null, $"DEP-{month:yyyy-MM}", $"Depreciation {month:yyyy-MM}", lines), null, null, cancellationToken);
                await store.AddDepreciationsAsync(
                    [.. made.Select(m => new AssetDepreciation { CompanyId = company.Id, AssetId = m.Asset.Id, Month = month, Amount = m.Amount, VoucherId = voucher.Id })], cancellationToken);
                foreach (var (asset, amount) in made)
                {
                    accumulated[asset.Id] += amount;
                    lastMonth[asset.Id] = month;
                }

                items.Add(new DepreciationRunItem(month, voucher.Id, made.Count, made.Sum(m => m.Amount), null));
            }
            catch (ValidationException e)
            {
                // A locked month (or an account problem) stops the run there: the later months follow on from this one.
                items.Add(new DepreciationRunItem(month, null, made.Count, made.Sum(m => m.Amount), e.Issues.FirstOrDefault()?.Code ?? "asset.run-failed"));
                break;
            }
        }

        return new DepreciationRunResult(items);
    }

    /// <summary>Takes back the latest month of depreciation (all its assets), for a month that has not been locked.</summary>
    public async Task UndoLastDepreciationAsync(CancellationToken cancellationToken = default)
    {
        var rows = await store.ListDepreciationsAsync(cancellationToken);
        if (rows.Count == 0)
            throw Refused("depreciation", "asset.nothing-to-undo");

        var latest = rows.Max(r => r.Month);
        if ((await store.ListAsync(cancellationToken)).Any(a => a.Status == AssetStatus.Disposed && a.DisposalDate is { } d && FirstOfMonth(d) >= latest))
            throw Refused("depreciation", "asset.disposed-since");

        foreach (var voucherId in rows.Where(r => r.Month == latest).Select(r => r.VoucherId).Distinct())
        {
            await vouchers.DeleteSystemAsync(voucherId, cancellationToken); // refused for a locked month
            await store.DeleteDepreciationsOfVoucherAsync(voucherId, cancellationToken);
        }
    }

    // ---------------------------------------------------------------- Disposal

    /// <summary>
    /// Sells or scraps an asset: depreciation is brought up to the month of the disposal, then one voucher takes off its cost and its
    /// depreciation, brings in the proceeds, and books the difference as a gain or loss.
    /// </summary>
    public async Task<AssetDto> DisposeAsync(Guid id, DisposeInput input, CancellationToken cancellationToken = default)
    {
        var asset = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("asset");
        var chart = (await accounts.ListAsync(cancellationToken)).ToList();
        var byId = chart.ToDictionary(a => a.Id);
        var issues = new List<ValidationIssue>();

        if (asset.Status == AssetStatus.Disposed)
            issues.Add(new("asset", "asset.already-disposed"));
        if (input.Date < asset.AcquisitionDate)
            issues.Add(new("date", "asset.disposal-before-acquisition"));
        if (input.Proceeds < 0)
            issues.Add(new("proceeds", "asset.proceeds-invalid"));
        if (input.Proceeds > 0 && (input.ProceedsAccountId is not { } p || !byId.TryGetValue(p, out var pa) || !pa.IsPosting || !pa.IsActive))
            issues.Add(new("proceedsAccount", "asset.account-invalid"));
        if (issues.Count > 0)
            throw new ValidationException(issues);

        var run = await RunAsync(input.Date, id, cancellationToken);
        if (run.Items.FirstOrDefault(i => i.ProblemCode is not null) is { } failed)
            throw Refused("date", failed.ProblemCode!);

        var rows = (await store.ListDepreciationsAsync(cancellationToken)).Where(r => r.AssetId == id).ToList();
        var accumulated = asset.OpeningAccumulated + rows.Sum(r => r.Amount);
        var gainOrLoss = input.Proceeds - (asset.Cost - accumulated); // positive: a gain

        var lines = new List<VoucherLineInput>();
        var label = $"{asset.Code} {asset.NameEn}".Trim();
        if (accumulated > 0)
            lines.Add(new VoucherLineInput(null, asset.AccumulatedAccountId, label, accumulated, 0));
        if (input.Proceeds > 0)
            lines.Add(new VoucherLineInput(null, input.ProceedsAccountId!.Value, label, input.Proceeds, 0));
        lines.Add(new VoucherLineInput(null, asset.AssetAccountId, label, 0, asset.Cost));
        if (gainOrLoss != 0)
        {
            var gainLoss = input.GainLossAccountId is { } chosen && chosen != Guid.Empty
                ? chosen
                : chart.Where(a => a.Role == AccountRole.AssetDisposal && a.IsPosting && a.IsActive).OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.Id;
            if (gainLoss is not { } gainLossId || !byId.TryGetValue(gainLossId, out var gl) || !gl.IsPosting || !gl.IsActive)
                throw Refused("gainLossAccount", "asset.disposal-account-missing");
            lines.Add(gainOrLoss < 0 ? new VoucherLineInput(null, gainLossId, label, -gainOrLoss, 0, null, asset.CostCenterId) : new VoucherLineInput(null, gainLossId, label, 0, gainOrLoss, null, asset.CostCenterId));
        }

        var voucher = await vouchers.SaveAndPostSystemAsync(
            new VoucherInput(VoucherKind.AssetDisposal, input.Date, null, asset.Code, $"Disposal of {label}", lines), null, null, cancellationToken);

        asset.Status = AssetStatus.Disposed;
        asset.DisposalDate = input.Date;
        asset.DisposalProceeds = input.Proceeds;
        asset.DisposalVoucherId = voucher.Id;
        await store.UpdateAsync(asset, cancellationToken);
        return ToDto(asset, rows);
    }

    /// <summary>Takes a disposal back: its voucher is deleted (for an open month) and the asset is in use again.</summary>
    public async Task<AssetDto> UndoDisposalAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var asset = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("asset");
        if (asset.Status != AssetStatus.Disposed)
            throw Refused("asset", "asset.not-disposed");

        if (asset.DisposalVoucherId is { } voucherId)
            await vouchers.DeleteSystemAsync(voucherId, cancellationToken);
        asset.Status = AssetStatus.Active;
        asset.DisposalDate = null;
        asset.DisposalProceeds = 0;
        asset.DisposalVoucherId = null;
        await store.UpdateAsync(asset, cancellationToken);
        return ToDto(asset, (await store.ListDepreciationsAsync(cancellationToken)).Where(r => r.AssetId == id).ToList());
    }

    // ---------------------------------------------------------------- Helpers

    /// <summary>The asset's position as of a date, for the register report: depreciation posted by the end of that date.</summary>
    public async Task<IReadOnlyList<AssetPosition>> PositionsAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var rows = (await store.ListDepreciationsAsync(cancellationToken)).ToLookup(r => r.AssetId);
        var result = new List<AssetPosition>();
        foreach (var asset in (await store.ListAsync(cancellationToken)).OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase))
        {
            if (asset.AcquisitionDate > asOf)
                continue;
            var disposed = asset.Status == AssetStatus.Disposed && asset.DisposalDate is { } d && d <= asOf;
            var posted = rows[asset.Id].Where(r => r.Month.AddMonths(1).AddDays(-1) <= asOf).Sum(r => r.Amount);
            result.Add(new AssetPosition(asset, asset.OpeningAccumulated + posted, disposed));
        }

        return result;
    }

    private static DateOnly LastMonthOf(FixedAsset asset, IReadOnlyList<AssetDepreciation> rows)
    {
        var last = rows.Where(r => r.AssetId == asset.Id).Select(r => r.Month).DefaultIfEmpty().Max();
        var before = FirstOfMonth(asset.AcquisitionDate).AddMonths(-1);
        var through = asset.DepreciatedThrough ?? before;
        return new[] { last, through, before }.Max();
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static DateOnly? Month(DateOnly? date) => date is { } d ? FirstOfMonth(d) : null;

    private static AssetDto ToDto(FixedAsset a, IReadOnlyList<AssetDepreciation> rows)
    {
        var accumulated = a.OpeningAccumulated + rows.Sum(r => r.Amount);
        return new AssetDto(
            a.Id, a.Code, a.NameAr, a.NameEn, a.Kind, a.AcquisitionDate, a.Cost, a.Salvage, a.UsefulLifeMonths, a.Method, a.AnnualRate,
            a.AssetAccountId, a.AccumulatedAccountId, a.ExpenseAccountId, a.CostCenterId, a.OpeningAccumulated, a.DepreciatedThrough, a.Status,
            a.DisposalDate, a.DisposalProceeds, a.Notes, accumulated, a.Cost - accumulated,
            rows.Count == 0 ? null : rows.Max(r => r.Month), rows.Count > 0 || a.Status == AssetStatus.Disposed);
    }

    private static ValidationException Refused(string field, string code) => new([new ValidationIssue(field, code)]);
}

/// <summary>An asset with its depreciation so far on a date, and whether it had been disposed of by then.</summary>
public sealed record AssetPosition(FixedAsset Asset, decimal Accumulated, bool Disposed);
