using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Trade;
using Baba.Localization;

namespace Baba.Application.Trade;

public sealed record DocumentLineInput(
    Guid? Id,
    Guid? ProductId,
    Guid? AccountId,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent = 0,
    Guid? CostCenterId = null);

/// <summary>What the document form sends. The currency and rate may be left out: they default to the company's currency, and to the latest rate on the date.</summary>
public sealed record DocumentInput(
    DocumentKind Kind,
    DateOnly Date,
    DateOnly? DueDate,
    Guid PartyId,
    string? CurrencyCode,
    decimal? ExchangeRate,
    string? Reference,
    string? Memo,
    decimal DiscountPercent,
    IReadOnlyList<DocumentLineInput> Lines);

public sealed record DocumentLineDto(
    Guid Id, Guid? ProductId, Guid? AccountId, string? Description, decimal Quantity, decimal UnitPrice, decimal DiscountPercent, Guid? CostCenterId, decimal Amount);

public sealed record DocumentDto(
    Guid Id,
    DocumentKind Kind,
    string? Number,
    DateOnly Date,
    DateOnly? DueDate,
    DocumentStatus Status,
    Guid PartyId,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Reference,
    string? Memo,
    decimal DiscountPercent,
    IReadOnlyList<DocumentLineDto> Lines,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    Guid? SourceDocumentId,
    Guid? ConvertedToId,
    Guid? VoucherId);

/// <summary>One row of a document list.</summary>
public sealed record DocumentSummary(
    Guid Id, DocumentKind Kind, string? Number, DateOnly Date, DateOnly? DueDate, DocumentStatus Status, Guid PartyId, string CurrencyCode, decimal Total, string? Reference);

/// <summary>
/// Sales and purchase documents (brief section 10.3): quotes, orders, delivery and receipt notes, invoices, credit and debit notes.
/// A draft has no number and no effect. Issuing numbers it; for an invoice or a credit or debit note it also posts to the ledger,
/// through a voucher that this service makes and keeps in step with the document (so the posting engine stays the only maker of ledger
/// entries). Quotes, orders and notes turn into the next document in the chain with one click.
/// </summary>
public sealed class DocumentService(
    IDocumentStore documents,
    IPartyStore parties,
    IAccountStore accounts,
    IProductStore products,
    ICostCenterStore costCenters,
    ICurrencyRateStore currencyRates,
    VoucherService vouchers,
    ICompanyFiles files,
    TimeProvider clock)
{
    // ---------------------------------------------------------------- Reading

    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(DocumentSearch search, CancellationToken cancellationToken = default)
    {
        var company = Company();
        return (await documents.SearchAsync(search, cancellationToken)).Select(d =>
        {
            var totals = DocumentMath.Compute(d.Lines, d.DiscountPercent, CurrencyOf(d.CurrencyCode, company));
            return new DocumentSummary(d.Id, d.Kind, d.Number, d.Date, d.DueDate, d.Status, d.PartyId, d.CurrencyCode, totals.Total, d.Reference);
        }).ToList();
    }

    public async Task<DocumentDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await documents.FindAsync(id, cancellationToken) is { } document ? ToDto(document) : null;

    // ---------------------------------------------------------------- Saving and issuing

    /// <summary>Saves a new document, or changes an existing draft, without issuing it.</summary>
    public async Task<DocumentDto> SaveDraftAsync(Guid? id, DocumentInput input, CancellationToken cancellationToken = default)
    {
        var existing = await FindForEditAsync(id, cancellationToken);
        if (existing is { Status: not DocumentStatus.Draft })
            throw Refused("document", "document.not-draft"); // an issued document is changed with Save, which keeps it issued

        var (document, issues) = await BuildAsync(existing, input, cancellationToken);
        Throw(issues);
        await documents.SaveAsync([document], cancellationToken);
        return ToDto(document);
    }

    /// <summary>Saves (new or changed) and issues. Editing an issued document makes its ledger entries again.</summary>
    public async Task<DocumentDto> IssueAsync(Guid? id, DocumentInput input, CancellationToken cancellationToken = default)
    {
        var existing = await FindForEditAsync(id, cancellationToken);
        var (document, issues) = await BuildAsync(existing, input, cancellationToken);
        Throw(issues);
        await IssueBuiltAsync(document, cancellationToken);
        return ToDto(document);
    }

    /// <summary>Issues a draft exactly as it was saved.</summary>
    public async Task<DocumentDto> IssueSavedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stored = await documents.FindAsync(id, cancellationToken) ?? throw new NotFoundException("document");
        if (stored.Status != DocumentStatus.Draft)
            throw Refused("document", "document.not-draft");

        var input = new DocumentInput(
            stored.Kind, stored.Date, stored.DueDate, stored.PartyId, stored.CurrencyCode, stored.ExchangeRate, stored.Reference, stored.Memo, stored.DiscountPercent,
            stored.Lines.Select(l => new DocumentLineInput(l.Id, l.ProductId, l.AccountId, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.CostCenterId)).ToList());
        return await IssueAsync(id, input, cancellationToken);
    }

    private async Task IssueBuiltAsync(Document document, CancellationToken cancellationToken)
    {
        var company = Company();
        var currency = CurrencyOf(document.CurrencyCode, company);
        var totals = DocumentMath.Compute(document.Lines, document.DiscountPercent, currency);

        if (document.Kind.Posts())
        {
            var issues = new List<ValidationIssue>();
            for (var i = 0; i < document.Lines.Count; i++)
            {
                if (totals.PostedAmounts[i] != 0 && document.Lines[i].AccountId is null)
                    issues.Add(new($"lines[{i}].account", "line.account-required"));
            }

            if (totals.Total <= 0)
                issues.Add(new("lines", "document.total-not-positive"));

            var control = await ControlAccountAsync(document.Kind, cancellationToken);
            if (control is null)
                issues.Add(new("controlAccount", "document.control-account-missing"));
            Throw(issues);

            var (input, included) = ToVoucherInput(document, totals, control!.Id);
            try
            {
                var voucher = await vouchers.SaveAndPostSystemAsync(input, document.VoucherId, document.Id, cancellationToken);
                document.VoucherId = voucher.Id;
                document.Number = voucher.Number;
            }
            catch (ValidationException e)
            {
                throw new ValidationException(e.Issues.Select(i => Remap(i, included)).ToList());
            }
        }
        else if (document.Number is null)
        {
            document.Number = await documents.NextNumberAsync(document.Kind, document.Date, cancellationToken);
        }

        document.Status = DocumentStatus.Issued;
        await documents.SaveAsync([document], cancellationToken);
    }

    /// <summary>
    /// The voucher an invoice or note posts through. Line one is the customer's or supplier's account for the whole document; the others
    /// are the document's lines, each at its posted amount (after the document discount). Lines of zero are left out.
    /// </summary>
    private static (VoucherInput Input, IReadOnlyList<int> Included) ToVoucherInput(Document document, DocumentTotals totals, Guid controlAccountId)
    {
        var controlIsDebit = document.Kind is DocumentKind.SalesInvoice or DocumentKind.PurchaseDebitNote;
        var lines = new List<VoucherLineInput>
        {
            new(null, controlAccountId, null, controlIsDebit ? totals.Total : 0, controlIsDebit ? 0 : totals.Total, document.PartyId),
        };

        var included = new List<int>();
        for (var i = 0; i < document.Lines.Count; i++)
        {
            var amount = totals.PostedAmounts[i];
            if (amount == 0)
                continue;

            var line = document.Lines[i];
            var onDebitSide = controlIsDebit ? amount < 0 : amount > 0;
            lines.Add(new VoucherLineInput(
                null, line.AccountId!.Value, line.Description, onDebitSide ? Math.Abs(amount) : 0, onDebitSide ? 0 : Math.Abs(amount), null, line.CostCenterId));
            included.Add(i);
        }

        var input = new VoucherInput(
            document.Kind.VoucherKind(), document.Date, null, document.Reference, document.Memo, lines, document.CurrencyCode, document.ExchangeRate);
        return (input, included);
    }

    /// <summary>The voucher's problems, named by the voucher's line numbers, put back on the document's own lines and fields.</summary>
    private static ValidationIssue Remap(ValidationIssue issue, IReadOnlyList<int> included)
    {
        var match = System.Text.RegularExpressions.Regex.Match(issue.Field, @"^lines\[(\d+)\]\.(\w+)$");
        if (!match.Success)
            return issue;

        var index = int.Parse(match.Groups[1].Value);
        if (index == 0)
            return new ValidationIssue(match.Groups[2].Value == "party" ? "party" : "controlAccount", issue.Code);
        return index - 1 < included.Count ? new ValidationIssue($"lines[{included[index - 1]}].{match.Groups[2].Value}", issue.Code) : issue;
    }

    // ---------------------------------------------------------------- Converting and deleting

    /// <summary>Turns a document into the next one in the chain (or a credit or debit note) as a new draft with the same lines.</summary>
    public async Task<DocumentDto> ConvertAsync(Guid id, DocumentKind target, CancellationToken cancellationToken = default)
    {
        var source = await documents.FindAsync(id, cancellationToken) ?? throw new NotFoundException("document");
        if (source.Status == DocumentStatus.Converted)
            throw Refused("document", "document.converted");
        if (source.Status != DocumentStatus.Issued)
            throw Refused("document", "document.not-issued");
        if (!source.Kind.ConvertibleTo().Contains(target))
            throw Refused("kind", "document.cannot-convert");

        var company = Company();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var party = (await parties.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == source.PartyId);
        var foreign = source.CurrencyCode != company.BaseCurrencyCode;

        var copy = new Document
        {
            CompanyId = source.CompanyId,
            Kind = target,
            Date = today,
            DueDate = today.AddDays(party?.PaymentTermsDays ?? 0),
            Status = DocumentStatus.Draft,
            PartyId = source.PartyId,
            CurrencyCode = source.CurrencyCode,
            ExchangeRateScaled = foreign ? FxRate.ToScaled(await LatestRateAsync(source.CurrencyCode, today, cancellationToken, fallback: source.ExchangeRate)) : FxRate.One,
            Reference = source.Reference,
            Memo = source.Memo,
            DiscountPercentScaled = source.DiscountPercentScaled,
            SourceDocumentId = source.Id,
        };
        copy.Lines = source.Lines.OrderBy(l => l.LineNumber).Select((l, i) => new DocumentLine
        {
            CompanyId = source.CompanyId,
            DocumentId = copy.Id,
            LineNumber = i + 1,
            ProductId = l.ProductId,
            AccountId = l.AccountId,
            Description = l.Description,
            QuantityScaled = l.QuantityScaled,
            UnitPriceScaled = l.UnitPriceScaled,
            DiscountPercentScaled = l.DiscountPercentScaled,
            CostCenterId = l.CostCenterId,
        }).ToList();

        var changed = new List<Document> { copy };
        if (source.Kind.IsConsumedByConversion())
        {
            source.Status = DocumentStatus.Converted;
            source.ConvertedToId = copy.Id;
            changed.Add(source);
        }

        await documents.SaveAsync(changed, cancellationToken);
        return ToDto(copy);
    }

    /// <summary>
    /// Deletes a document. An invoice or note takes its ledger entries with it (unless its month is locked). A document that has been
    /// converted cannot be deleted until what it was converted into is; deleting that gives the original back.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await documents.FindAsync(id, cancellationToken) ?? throw new NotFoundException("document");
        if (document.Status == DocumentStatus.Converted)
            throw Refused("document", "document.converted");

        if (document.VoucherId is { } voucherId)
            await vouchers.DeleteSystemAsync(voucherId, cancellationToken);

        if (document.SourceDocumentId is { } sourceId
            && await documents.FindAsync(sourceId, cancellationToken) is { } source
            && source.ConvertedToId == document.Id)
        {
            source.Status = DocumentStatus.Issued;
            source.ConvertedToId = null;
            await documents.SaveAsync([source], cancellationToken);
        }

        await documents.DeleteAsync(id, cancellationToken);
    }

    // ---------------------------------------------------------------- Building and checking

    private async Task<Document?> FindForEditAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (id is not { } existingId)
            return null;
        var existing = await documents.FindAsync(existingId, cancellationToken) ?? throw new NotFoundException("document");
        if (existing.Status == DocumentStatus.Converted)
            throw Refused("document", "document.converted");
        return existing;
    }

    /// <summary>Applies the form to a new or existing document and checks everything that must be true to save it, even as a draft.</summary>
    private async Task<(Document Document, IReadOnlyList<ValidationIssue> Issues)> BuildAsync(Document? existing, DocumentInput input, CancellationToken cancellationToken)
    {
        var company = Company();
        var issues = new List<ValidationIssue>();

        if (existing is not null && existing.Kind != input.Kind)
            issues.Add(new("kind", "document.kind-cannot-change"));

        var party = (await parties.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == input.PartyId);
        if (party is null)
            issues.Add(new("party", "document.party-required"));
        else if (party.Kind != input.Kind.PartyKind())
            issues.Add(new("party", "document.party-wrong-kind"));
        else if (!party.IsActive && existing?.PartyId != party.Id)
            issues.Add(new("party", "document.party-inactive"));

        if (input.Date == default)
            issues.Add(new("date", "date.required"));

        var currencyCode = string.IsNullOrWhiteSpace(input.CurrencyCode) ? company.BaseCurrencyCode : input.CurrencyCode.Trim().ToUpperInvariant();
        decimal rate = 1m;
        if (CurrencyCatalog.Find(currencyCode) is null)
        {
            issues.Add(new("currency", "currency.unknown"));
        }
        else if (currencyCode != company.BaseCurrencyCode)
        {
            rate = input.ExchangeRate ?? await LatestRateAsync(currencyCode, input.Date, cancellationToken);
            if (rate <= 0)
                issues.Add(new("exchangeRate", "exchange-rate.invalid"));
        }

        if (input.DiscountPercent is < 0 or > 100)
            issues.Add(new("discountPercent", "document.discount-invalid"));
        if (input.Lines is null || input.Lines.Count == 0)
            issues.Add(new("lines", "lines.required"));

        var allProducts = (await products.ListAsync(cancellationToken)).ToDictionary(p => p.Id);
        var allCostCenters = (await costCenters.ListAsync(cancellationToken)).ToDictionary(c => c.Id);
        var chart = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        var oldLines = existing?.Lines.ToDictionary(l => l.Id) ?? [];

        var document = existing ?? new Document { CompanyId = company.Id, Kind = input.Kind };
        var lines = new List<DocumentLine>();
        for (var i = 0; i < (input.Lines?.Count ?? 0); i++)
        {
            var line = input.Lines![i];
            string Field(string name) => $"lines[{i}].{name}";

            Product? product = null;
            if (line.ProductId is { } productId && productId != Guid.Empty && !allProducts.TryGetValue(productId, out product))
                issues.Add(new(Field("product"), "line.product-unknown"));
            if (line.Quantity <= 0)
                issues.Add(new(Field("quantity"), "line.quantity-invalid"));
            if (line.UnitPrice < 0)
                issues.Add(new(Field("price"), "line.price-negative"));
            if (line.DiscountPercent is < 0 or > 100)
                issues.Add(new(Field("discount"), "line.discount-invalid"));
            if (line.CostCenterId is { } costCenterId && costCenterId != Guid.Empty && !allCostCenters.ContainsKey(costCenterId))
                issues.Add(new(Field("costCenter"), "line.cost-center-unknown"));

            // The account the line posts to, or the product's own account for this direction of trade.
            var accountId = line.AccountId == Guid.Empty ? null : line.AccountId;
            accountId ??= input.Kind.IsSales() ? product?.SalesAccountId : product?.PurchaseAccountId;
            if (accountId is { } id && (!chart.TryGetValue(id, out var account) || !account.IsPosting || !account.IsActive))
                issues.Add(new(Field("account"), "line.account-invalid"));

            var description = string.IsNullOrWhiteSpace(line.Description) ? (product is null ? null : (product.NameEn.Length > 0 ? product.NameEn : product.NameAr)) : line.Description.Trim();
            lines.Add(new DocumentLine
            {
                Id = line.Id is { } lineId && oldLines.ContainsKey(lineId) ? lineId : Guid.CreateVersion7(),
                CompanyId = company.Id,
                DocumentId = document.Id,
                LineNumber = i + 1,
                ProductId = product?.Id,
                AccountId = accountId,
                Description = description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                CostCenterId = line.CostCenterId == Guid.Empty ? null : line.CostCenterId,
            });
        }

        document.Date = input.Date;
        document.DueDate = input.DueDate ?? (input.Date == default ? null : input.Date.AddDays(party?.PaymentTermsDays ?? 0));
        document.PartyId = input.PartyId;
        document.CurrencyCode = currencyCode;
        document.ExchangeRate = currencyCode == company.BaseCurrencyCode ? 1m : (rate > 0 ? rate : 1m);
        document.Reference = Clean(input.Reference);
        document.Memo = Clean(input.Memo);
        document.DiscountPercent = input.DiscountPercent;
        document.Lines = lines;
        if (existing is null)
            document.Status = DocumentStatus.Draft;
        return (document, issues);
    }

    private async Task<Account?> ControlAccountAsync(DocumentKind kind, CancellationToken cancellationToken)
    {
        var role = kind.IsSales() ? AccountRole.Receivable : AccountRole.Payable;
        return (await accounts.ListAsync(cancellationToken))
            .Where(a => a.Role == role && a.IsPosting && a.IsActive)
            .OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private async Task<decimal> LatestRateAsync(string currencyCode, DateOnly date, CancellationToken cancellationToken, decimal fallback = 0) =>
        (await currencyRates.ListAsync(cancellationToken))
            .Where(r => r.CurrencyCode == currencyCode && r.Date <= date)
            .OrderByDescending(r => r.Date).Select(r => r.Rate).DefaultIfEmpty(fallback).First();

    private DocumentDto ToDto(Document d)
    {
        var totals = DocumentMath.Compute(d.Lines.OrderBy(l => l.LineNumber).ToList(), d.DiscountPercent, CurrencyOf(d.CurrencyCode, Company()));
        var ordered = d.Lines.OrderBy(l => l.LineNumber).ToList();
        return new DocumentDto(
            d.Id, d.Kind, d.Number, d.Date, d.DueDate, d.Status, d.PartyId, d.CurrencyCode, d.ExchangeRate, d.Reference, d.Memo, d.DiscountPercent,
            ordered.Select((l, i) => new DocumentLineDto(l.Id, l.ProductId, l.AccountId, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.CostCenterId, totals.LineAmounts[i])).ToList(),
            totals.Subtotal, totals.DiscountAmount, totals.Total, d.SourceDocumentId, d.ConvertedToId, d.VoucherId);
    }

    private CompanyInfo Company() => files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");

    private static Currency CurrencyOf(string code, CompanyInfo company) =>
        CurrencyCatalog.Find(string.IsNullOrEmpty(code) ? company.BaseCurrencyCode : code)?.Currency ?? new Currency(company.BaseCurrencyCode, 2);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static ValidationException Refused(string field, string code) => new([new ValidationIssue(field, code)]);

    private static void Throw(IReadOnlyCollection<ValidationIssue> issues)
    {
        if (issues.Count > 0)
            throw new ValidationException(issues.ToList());
    }
}
