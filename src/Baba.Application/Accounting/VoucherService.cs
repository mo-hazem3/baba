using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Localization;

namespace Baba.Application.Accounting;

public sealed record VoucherLineInput(Guid? Id, Guid AccountId, string? Description, decimal Debit, decimal Credit);

/// <summary>What the voucher form sends. Rows the user left completely empty must be left out; every row sent is checked.</summary>
public sealed record VoucherInput(
    VoucherKind Kind,
    DateOnly Date,
    Guid? CashAccountId,
    string? Reference,
    string? Memo,
    IReadOnlyList<VoucherLineInput> Lines);

public sealed record VoucherLineDto(Guid Id, Guid AccountId, string? Description, decimal Debit, decimal Credit);

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
    DateTime? PostedAt);

/// <summary>
/// Payment, receipt and journal vouchers (brief section 10.1). A voucher is a draft (saved for later, no ledger entries) or posted
/// (submitted, with entries). Posted vouchers stay editable and deletable, every change goes to the audit log, and a locked period
/// cannot be changed.
/// </summary>
public sealed class VoucherService(
    IVoucherStore vouchers,
    IAccountStore accounts,
    IPeriodStore periods,
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
        var (voucher, context, wasPosted, oldDate) = await PrepareAsync(id, input, cancellationToken);
        voucher.Status = VoucherStatus.Draft;
        voucher.PostedAt = null;

        var issues = PostingEngine.ValidateDraft(voucher, context).ToList();
        if (wasPosted)
            AddLockedPeriodIssue(issues, oldDate, context);
        Throw(issues);

        await vouchers.SaveAsync(voucher, [], cancellationToken);
        return ToDto(voucher);
    }

    /// <summary>Saves (new or changed) and posts. Editing a posted voucher regenerates its ledger entries.</summary>
    public async Task<VoucherDto> SaveAndPostAsync(Guid? id, VoucherInput input, CancellationToken cancellationToken = default)
    {
        var (voucher, context, wasPosted, oldDate) = await PrepareAsync(id, input, cancellationToken);
        return await PostPreparedAsync(voucher, context, wasPosted, oldDate, cancellationToken);
    }

    /// <summary>Posts a draft exactly as it was saved.</summary>
    public async Task<VoucherDto> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var voucher = await vouchers.FindAsync(id, cancellationToken) ?? throw new NotFoundException("voucher");
        var context = await ContextAsync(cancellationToken);
        return await PostPreparedAsync(voucher, context, voucher.Status == VoucherStatus.Posted, voucher.Date, cancellationToken);
    }

    /// <summary>Deletes a voucher and its ledger entries. A posted voucher in a locked period cannot be deleted.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var voucher = await vouchers.FindAsync(id, cancellationToken) ?? throw new NotFoundException("voucher");
        if (voucher.Status == VoucherStatus.Posted)
        {
            var issues = new List<PostingIssue>();
            AddLockedPeriodIssue(issues, voucher.Date, await ContextAsync(cancellationToken));
            Throw(issues);
        }

        await vouchers.DeleteAsync(id, cancellationToken);
    }

    private async Task<VoucherDto> PostPreparedAsync(
        Voucher voucher, PostingContext context, bool wasPosted, DateOnly oldDate, CancellationToken cancellationToken)
    {
        voucher.Status = VoucherStatus.Posted;

        var issues = PostingEngine.ValidateForPosting(voucher, context).ToList();
        if (wasPosted)
            AddLockedPeriodIssue(issues, oldDate, context); // the voucher's old date must be open too: its old entries are being replaced
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
        var context = await ContextAsync(cancellationToken);

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
        voucher.CurrencyCode = company.BaseCurrencyCode; // transaction currency = base currency until the multi-currency screens exist
        voucher.ExchangeRateScaled = FxRate.One;
        voucher.CashAccountId = input.Kind == VoucherKind.Journal ? null : input.CashAccountId;
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
            Description = Clean(line.Description),
            Debit = line.Debit,
            Credit = line.Credit,
        }).ToList();

        return (voucher, context, wasPosted, oldDate);
    }

    private async Task<PostingContext> ContextAsync(CancellationToken cancellationToken)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var baseCurrency = CurrencyCatalog.Find(company.BaseCurrencyCode)?.Currency ?? new Currency(company.BaseCurrencyCode, 2);
        var locked = (await periods.ListAsync(cancellationToken)).Where(p => p.IsLocked).ToList();
        var allAccounts = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);

        return new PostingContext(allAccounts, baseCurrency, baseCurrency, date => !locked.Any(p => p.Start <= date && date <= p.End));
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
        v.Lines.OrderBy(l => l.LineNumber).Select(l => new VoucherLineDto(l.Id, l.AccountId, l.Description, l.Debit, l.Credit)).ToList(),
        v.Kind == VoucherKind.Receipt ? v.TotalCredit : v.TotalDebit,
        v.PostedAt);
}
