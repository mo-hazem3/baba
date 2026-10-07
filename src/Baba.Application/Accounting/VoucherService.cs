using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Localization;

namespace Baba.Application.Accounting;

public sealed record VoucherLineInput(
    Guid? Id, Guid AccountId, string? Description, decimal Debit, decimal Credit, Guid? PartyId = null, Guid? CostCenterId = null);

/// <summary>What the voucher form sends. Rows the user left completely empty must be left out; every row sent is checked.</summary>
public sealed record VoucherInput(
    VoucherKind Kind,
    DateOnly Date,
    Guid? CashAccountId,
    string? Reference,
    string? Memo,
    IReadOnlyList<VoucherLineInput> Lines,
    /// <summary>The currency the amounts are in. Null means the company's own currency.</summary>
    string? CurrencyCode = null,
    /// <summary>How many company-currency units one unit of the currency is worth. Null takes the latest rate on or before the date.</summary>
    decimal? ExchangeRate = null);

public sealed record VoucherLineDto(
    Guid Id, Guid AccountId, string? Description, decimal Debit, decimal Credit, Guid? PartyId = null, Guid? CostCenterId = null);

public sealed record VoucherDto(
    Guid Id,
    VoucherKind Kind,
    string? Number,
    DateOnly Date,
    VoucherStatus Status,
    string CurrencyCode,
    Guid? CashAccountId,
    string? Reference,
    string? Memo,
    IReadOnlyList<VoucherLineDto> Lines,
    decimal Total,
    DateTime? PostedAt,
    decimal ExchangeRate = 1m,
    Guid? DocumentId = null);

/// <summary>
/// Payment, receipt and journal vouchers (brief section 10.1). A voucher is a draft (saved for later, no ledger entries) or posted
/// (submitted, with entries). Posted vouchers stay editable and deletable, every change goes to the audit log, and a locked period
/// cannot be changed.
/// </summary>
public sealed class VoucherService(
    IVoucherStore vouchers,
    IAccountStore accounts,
    IPartyStore parties,
    ICostCenterStore costCenters,
    Banking.IReconciliationStore reconciliations,
    IPeriodStore periods,
    ICurrencyRateStore currencyRates,
    ICompanyFiles files,
    TimeProvider clock)
{
    public async Task<VoucherDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await vouchers.FindAsync(id, cancellationToken) is { } voucher ? ToDto(voucher) : null;

    public Task<IReadOnlyList<VoucherSummary>> ListAsync(VoucherSearch search, CancellationToken cancellationToken = default) =>
        vouchers.SearchAsync(search, cancellationToken);

    /// <summary>Saves a new voucher (id null) or changes an existing one, as a draft. A posted voucher saved this way goes back to draft.</summary>
    public async Task<VoucherDto> SaveDraftAsync(Guid? id, VoucherInput input, CancellationToken cancellationToken = default)
    {
        RefuseSystemKind(input.Kind);
        var (voucher, context, wasPosted, oldDate) = await PrepareAsync(id, input, cancellationToken);
        voucher.Status = VoucherStatus.Draft;
        voucher.PostedAt = null;

        var issues = PostingEngine.ValidateDraft(voucher, context).ToList();
        if (wasPosted)
            AddLockedPeriodIssue(issues, oldDate, context);
        await AddOpeningAndReconciledIssuesAsync(issues, voucher, id, wasPosted, cancellationToken);
        Throw(issues);

        await vouchers.SaveAsync(voucher, [], cancellationToken);
        return ToDto(voucher);
    }

    /// <summary>Saves (new or changed) and posts. Editing a posted voucher regenerates its ledger entries.</summary>
    public async Task<VoucherDto> SaveAndPostAsync(Guid? id, VoucherInput input, CancellationToken cancellationToken = default)
    {
        RefuseSystemKind(input.Kind);
        var (voucher, context, wasPosted, oldDate) = await PrepareAsync(id, input, cancellationToken);
        return await PostPreparedAsync(voucher, context, wasPosted, oldDate, id, cancellationToken);
    }

    /// <summary>For the year-end close only: saves and posts a voucher of a kind that people cannot make by hand.</summary>
    internal async Task<VoucherDto> SaveAndPostSystemAsync(VoucherInput input, CancellationToken cancellationToken = default)
    {
        var (voucher, context, wasPosted, oldDate) = await PrepareAsync(null, input, cancellationToken);
        return await PostPreparedAsync(voucher, context, wasPosted, oldDate, null, cancellationToken);
    }

    /// <summary>For the year-end close only: deletes a voucher of a kind that people cannot delete by hand.</summary>
    internal async Task DeleteSystemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var voucher = await vouchers.FindAsync(id, cancellationToken) ?? throw new NotFoundException("voucher");
        var issues = new List<PostingIssue>();
        AddLockedPeriodIssue(issues, voucher.Date, await ContextAsync(null, cancellationToken));
        Throw(issues);
        await vouchers.DeleteAsync(id, cancellationToken);
    }

    /// <summary>The closing entry of a year is made by closing the year, so it cannot be saved or deleted like other vouchers.</summary>
    private static void RefuseSystemKind(VoucherKind kind)
    {
        if (kind == VoucherKind.Closing)
            throw new ValidationException([new ValidationIssue("kind", "voucher.system-generated")]);
    }

    /// <summary>
    /// Only one opening-balances voucher may exist, and a posted voucher whose entries have been checked against a bank statement
    /// cannot be changed until that reconciliation is undone.
    /// </summary>
    private async Task AddOpeningAndReconciledIssuesAsync(
        List<PostingIssue> issues, Voucher voucher, Guid? id, bool wasPosted, CancellationToken cancellationToken)
    {
        if (voucher.Kind == VoucherKind.Opening && id is null
            && (await vouchers.SearchAsync(new VoucherSearch(VoucherKind.Opening, Limit: 1), cancellationToken)).Count > 0)
        {
            issues.Add(new PostingIssue("kind", "opening.already-exists"));
        }

        if (wasPosted && id is { } existing && await reconciliations.HasReconciledEntriesAsync(existing, cancellationToken))
            issues.Add(new PostingIssue("voucher", "voucher.reconciled"));
    }

    /// <summary>Posts a draft exactly as it was saved.</summary>
    public async Task<VoucherDto> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var voucher = await vouchers.FindAsync(id, cancellationToken) ?? throw new NotFoundException("voucher");
        RefuseSystemKind(voucher.Kind);
        var context = await ContextAsync(voucher.CurrencyCode, cancellationToken);
        return await PostPreparedAsync(voucher, context, voucher.Status == VoucherStatus.Posted, voucher.Date, id, cancellationToken);
    }

    /// <summary>Deletes a voucher and its ledger entries. A posted voucher in a locked period cannot be deleted.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var voucher = await vouchers.FindAsync(id, cancellationToken) ?? throw new NotFoundException("voucher");
        RefuseSystemKind(voucher.Kind);
        if (voucher.Status == VoucherStatus.Posted)
        {
            var issues = new List<PostingIssue>();
            AddLockedPeriodIssue(issues, voucher.Date, await ContextAsync(null, cancellationToken));
            if (await reconciliations.HasReconciledEntriesAsync(id, cancellationToken))
                issues.Add(new PostingIssue("voucher", "voucher.reconciled"));
            Throw(issues);
        }

        await vouchers.DeleteAsync(id, cancellationToken);
    }

    private async Task<VoucherDto> PostPreparedAsync(
        Voucher voucher, PostingContext context, bool wasPosted, DateOnly oldDate, Guid? id, CancellationToken cancellationToken)
    {
        voucher.Status = VoucherStatus.Posted;

        var issues = PostingEngine.ValidateForPosting(voucher, context).ToList();
        if (wasPosted)
            AddLockedPeriodIssue(issues, oldDate, context); // the voucher's old date must be open too: its old entries are being replaced
        await AddOpeningAndReconciledIssuesAsync(issues, voucher, id, wasPosted, cancellationToken);
        Throw(issues);

        voucher.PostedAt = clock.GetUtcNow().UtcDateTime;
        await vouchers.SaveAsync(voucher, PostingEngine.GenerateEntries(voucher, context), cancellationToken);
        return ToDto(voucher);
    }

    /// <summary>Loads or starts the voucher, applies the form to it, and builds the context the rules need.</summary>
    private async Task<(Voucher Voucher, PostingContext Context, bool WasPosted, DateOnly OldDate)> PrepareAsync(
        Guid? id, VoucherInput input, CancellationToken cancellationToken)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        if (!string.IsNullOrWhiteSpace(input.CurrencyCode) && CurrencyCatalog.Find(input.CurrencyCode.Trim()) is null)
            throw new ValidationException([new ValidationIssue("currency", "currency.unknown")]);
        var context = await ContextAsync(input.CurrencyCode, cancellationToken);

        Voucher voucher;
        var wasPosted = false;
        var oldDate = input.Date;
        if (id is { } existingId)
        {
            voucher = await vouchers.FindAsync(existingId, cancellationToken) ?? throw new NotFoundException("voucher");
            if (voucher.Kind != input.Kind)
                throw new ValidationException([new ValidationIssue("kind", "voucher.kind-cannot-change")]);
            wasPosted = voucher.Status == VoucherStatus.Posted;
            oldDate = voucher.Date;
        }
        else
        {
            voucher = new Voucher { CompanyId = company.Id, Kind = input.Kind };
        }

        voucher.Date = input.Date;
        voucher.CurrencyCode = string.IsNullOrWhiteSpace(input.CurrencyCode) ? company.BaseCurrencyCode : input.CurrencyCode.Trim().ToUpperInvariant();
        voucher.ExchangeRateScaled = voucher.CurrencyCode == company.BaseCurrencyCode
            ? FxRate.One
            : FxRate.ToScaled(input.ExchangeRate ?? await LatestRateAsync(voucher.CurrencyCode, input.Date, cancellationToken));
        voucher.CashAccountId = input.Kind.UsesCashAccount() ? input.CashAccountId : null;
        voucher.Reference = Clean(input.Reference);
        voucher.Memo = Clean(input.Memo);

        var oldLines = voucher.Lines.ToDictionary(l => l.Id);
        voucher.Lines = input.Lines.Select((line, index) => new VoucherLine
        {
            // Keep the id of a row that was already there, so the audit log shows a change instead of a delete and a create.
            Id = line.Id is { } lineId && oldLines.ContainsKey(lineId) ? lineId : Guid.CreateVersion7(),
            CompanyId = company.Id,
            VoucherId = voucher.Id,
            LineNumber = index + 1,
            AccountId = line.AccountId,
            PartyId = line.PartyId == Guid.Empty ? null : line.PartyId,
            CostCenterId = line.CostCenterId == Guid.Empty ? null : line.CostCenterId,
            Description = Clean(line.Description),
            Debit = line.Debit,
            Credit = line.Credit,
        }).ToList();

        return (voucher, context, wasPosted, oldDate);
    }

    private async Task<decimal> LatestRateAsync(string currencyCode, DateOnly date, CancellationToken cancellationToken) =>
        (await currencyRates.ListAsync(cancellationToken))
            .Where(r => r.CurrencyCode == currencyCode && r.Date <= date)
            .OrderByDescending(r => r.Date).Select(r => r.Rate).FirstOrDefault(); // 0 when there is none: the engine says the rate is invalid

    private async Task<PostingContext> ContextAsync(string? currencyCode, CancellationToken cancellationToken)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var baseCurrency = CurrencyCatalog.Find(company.BaseCurrencyCode)?.Currency ?? new Currency(company.BaseCurrencyCode, 2);
        var locked = (await periods.ListAsync(cancellationToken)).Where(p => p.IsLocked).ToList();
        var allAccounts = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        var voucherCurrency = CurrencyCatalog.Find(currencyCode ?? company.BaseCurrencyCode)?.Currency ?? baseCurrency;
        var allParties = (await parties.ListAsync(cancellationToken)).ToDictionary(p => p.Id);
        var allCostCenters = (await costCenters.ListAsync(cancellationToken)).ToDictionary(c => c.Id);

        return new PostingContext(
            allAccounts, baseCurrency, voucherCurrency, date => !locked.Any(p => p.Start <= date && date <= p.End), allParties, allCostCenters);
    }

    private static void AddLockedPeriodIssue(List<PostingIssue> issues, DateOnly date, PostingContext context)
    {
        if (!context.IsDateOpen(date) && !issues.Any(i => i.Code == "date.locked-period"))
            issues.Add(new PostingIssue("date", "date.locked-period"));
    }

    private static void Throw(IReadOnlyCollection<PostingIssue> issues)
    {
        if (issues.Count > 0)
            throw new ValidationException(issues.Select(i => new ValidationIssue(i.Field, i.Code)).ToList());
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static VoucherDto ToDto(Voucher v) => new(
        v.Id, v.Kind, v.Number, v.Date, v.Status, v.CurrencyCode, v.CashAccountId, v.Reference, v.Memo,
        v.Lines.OrderBy(l => l.LineNumber).Select(l => new VoucherLineDto(l.Id, l.AccountId, l.Description, l.Debit, l.Credit, l.PartyId, l.CostCenterId)).ToList(),
        v.Kind == VoucherKind.Receipt ? v.TotalCredit : v.TotalDebit,
        v.PostedAt, v.ExchangeRate);
}
