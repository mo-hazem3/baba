using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Trade;
using Baba.Localization;

namespace Baba.Application.Trade;

/// <summary>Reads and writes the allocations of receipts and payments to invoices. Implemented by Infrastructure.</summary>
public interface IAllocationStore
{
    Task<IReadOnlyList<Allocation>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The allocations of one receipt or payment voucher.</summary>
    Task<IReadOnlyList<Allocation>> ForPaymentAsync(Guid paymentVoucherId, CancellationToken cancellationToken = default);

    Task AddAsync(IReadOnlyList<Allocation> allocations, CancellationToken cancellationToken = default);
}

/// <summary>An issued invoice and how much of it is still unpaid, in its own currency.</summary>
public sealed record OutstandingInvoice(
    Guid DocumentId,
    DocumentKind Kind,
    string Number,
    Guid PartyId,
    DateOnly Date,
    DateOnly? DueDate,
    string CurrencyCode,
    decimal Total,
    decimal Outstanding,
    decimal ExchangeRate = 1m);

/// <summary>How many invoices (and supplier bills) are past their due date, and what they come to in the company's currency.</summary>
public sealed record OverdueSummary(int InvoiceCount, decimal InvoiceAmount, int BillCount, decimal BillAmount);

public sealed record SettlementLineInput(Guid DocumentId, decimal Amount);

/// <summary>
/// Money received from a customer or paid to a supplier, with the invoices it pays. The currency is the one the invoices are in; the
/// rate may be left out to use the latest on the date. <c>OnAccount</c> is any part that is not for an invoice (an advance).
/// </summary>
public sealed record SettlementInput(
    Guid PartyId,
    DateOnly Date,
    Guid CashAccountId,
    string? CurrencyCode,
    decimal? ExchangeRate,
    string? Reference,
    string? Memo,
    IReadOnlyList<SettlementLineInput> Allocations,
    decimal OnAccount);

/// <param name="ExchangeGain">What was gained (positive) or lost (negative) because the rate moved between the invoices and the payment, in the company's currency.</param>
public sealed record SettlementResult(VoucherDto Payment, Guid? SettlementVoucherId, decimal ExchangeGain);

/// <summary>
/// Receipts from customers and payments to suppliers that are set against specific invoices (brief section 10.3). It makes an ordinary
/// receipt or payment voucher, records which invoices it pays, and, when the exchange rate moved since the invoices were issued, makes
/// a voucher that books the realized gain or loss to the exchange-differences account. Deleting the receipt or payment takes all of
/// that away again.
/// </summary>
public sealed class SettlementService(
    IDocumentStore documents,
    IPartyStore parties,
    IAccountStore accounts,
    IAllocationStore allocations,
    VoucherService vouchers,
    ILedgerQuery ledger,
    ICurrencyRateStore currencyRates,
    ICompanyFiles files)
{
    /// <summary>The issued invoices (all, or of one customer or supplier) with what is left to pay on each.</summary>
    public async Task<IReadOnlyList<OutstandingInvoice>> OutstandingAsync(Guid? partyId, CancellationToken cancellationToken = default)
    {
        var company = Company();
        var paid = (await allocations.ListAsync(cancellationToken)).GroupBy(a => a.DocumentId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));

        var invoices = new List<Document>();
        var notes = new List<Document>();
        foreach (var kind in new[] { DocumentKind.SalesInvoice, DocumentKind.PurchaseInvoice })
            invoices.AddRange(await documents.SearchAsync(new DocumentSearch(kind, DocumentStatus.Issued, partyId, Limit: 10_000), cancellationToken));
        foreach (var kind in new[] { DocumentKind.SalesCreditNote, DocumentKind.PurchaseDebitNote })
            notes.AddRange(await documents.SearchAsync(new DocumentSearch(kind, DocumentStatus.Issued, partyId, Limit: 10_000), cancellationToken));

        var credited = notes.Where(n => n.SourceDocumentId is not null)
            .GroupBy(n => n.SourceDocumentId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(n => Total(n, company)));

        return invoices.OrderBy(d => d.Date).ThenBy(d => d.Number, StringComparer.Ordinal).Select(d =>
        {
            var total = Total(d, company);
            var outstanding = total - paid.GetValueOrDefault(d.Id) - credited.GetValueOrDefault(d.Id);
            return new OutstandingInvoice(d.Id, d.Kind, d.Number ?? "", d.PartyId, d.Date, d.DueDate, d.CurrencyCode, total, Math.Max(outstanding, 0), d.ExchangeRate);
        }).ToList();
    }

    /// <summary>What is past its due date on the given day: customers' invoices and suppliers' bills, in the company's currency.</summary>
    public async Task<OverdueSummary> OverdueAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var company = Company();
        var baseCurrency = CurrencyOf(company.BaseCurrencyCode, company);
        var late = (await OutstandingAsync(null, cancellationToken)).Where(o => o.Outstanding > 0 && o.DueDate is { } due && due < asOf).ToList();
        decimal Base(IEnumerable<OutstandingInvoice> list) => list.Sum(o => Money.Round(o.Outstanding * o.ExchangeRate, baseCurrency));

        var invoices = late.Where(o => o.Kind == DocumentKind.SalesInvoice).ToList();
        var bills = late.Where(o => o.Kind == DocumentKind.PurchaseInvoice).ToList();
        return new OverdueSummary(invoices.Count, Base(invoices), bills.Count, Base(bills));
    }

    public async Task<SettlementResult> SettleAsync(SettlementInput input, CancellationToken cancellationToken = default)
    {
        var company = Company();
        var baseCurrency = CurrencyOf(company.BaseCurrencyCode, company);
        var issues = new List<ValidationIssue>();

        var party = (await parties.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == input.PartyId);
        if (party is null)
            throw Refused("party", "document.party-required");
        if (!party.IsActive)
            issues.Add(new("party", "document.party-inactive"));
        if (input.Date == default)
            issues.Add(new("date", "date.required"));

        var customer = party.Kind == PartyKind.Customer;
        var available = (await OutstandingAsync(party.Id, cancellationToken)).ToDictionary(o => o.DocumentId);
        var chosen = new List<(SettlementLineInput Line, OutstandingInvoice Invoice, Document Document)>();
        var seen = new HashSet<Guid>();
        for (var i = 0; i < input.Allocations.Count; i++)
        {
            var line = input.Allocations[i];
            if (line.Amount <= 0)
                issues.Add(new($"allocations[{i}].amount", "settlement.amount-invalid"));
            else if (!seen.Add(line.DocumentId) || !available.TryGetValue(line.DocumentId, out var invoice) || invoice.Outstanding <= 0)
                issues.Add(new($"allocations[{i}].document", "settlement.document-unavailable"));
            else if (line.Amount > invoice.Outstanding)
                issues.Add(new($"allocations[{i}].amount", "settlement.amount-too-high"));
            else if (await documents.FindAsync(line.DocumentId, cancellationToken) is { } document)
                chosen.Add((line, invoice, document));
        }

        if (input.OnAccount < 0)
            issues.Add(new("onAccount", "settlement.amount-invalid"));
        if (input.Allocations.Count == 0 && input.OnAccount <= 0)
            issues.Add(new("allocations", "settlement.nothing-to-settle"));

        // The payment is in the currency of the invoices it pays.
        var invoiceCurrencies = chosen.Select(c => c.Invoice.CurrencyCode).Distinct().ToList();
        if (invoiceCurrencies.Count > 1)
            issues.Add(new("allocations", "settlement.mixed-currencies"));
        var currencyCode = invoiceCurrencies.Count == 1
            ? invoiceCurrencies[0]
            : string.IsNullOrWhiteSpace(input.CurrencyCode) ? company.BaseCurrencyCode : input.CurrencyCode.Trim().ToUpperInvariant();
        if (invoiceCurrencies.Count == 1 && !string.IsNullOrWhiteSpace(input.CurrencyCode) && !string.Equals(input.CurrencyCode.Trim(), currencyCode, StringComparison.OrdinalIgnoreCase))
            issues.Add(new("currency", "settlement.currency-mismatch"));
        if (CurrencyCatalog.Find(currencyCode) is null)
            issues.Add(new("currency", "currency.unknown"));

        var foreign = currencyCode != company.BaseCurrencyCode;
        var rate = !foreign ? 1m : input.ExchangeRate ?? await LatestRateAsync(currencyCode, input.Date, cancellationToken);
        if (foreign && rate <= 0)
            issues.Add(new("exchangeRate", "exchange-rate.invalid"));

        var control = (await accounts.ListAsync(cancellationToken))
            .Where(a => a.Role == (customer ? AccountRole.Receivable : AccountRole.Payable) && a.IsPosting && a.IsActive)
            .OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (control is null)
            issues.Add(new("controlAccount", "document.control-account-missing"));

        if (issues.Count > 0)
            throw new ValidationException(issues);

        // What each invoice part was worth when it was issued, and what the matching part of the payment is worth now.
        var entries = (await ledger.PartyEntriesAsync(party.Id, null, cancellationToken)).ToList();
        var already = (await allocations.ListAsync(cancellationToken)).ToLookup(a => a.DocumentId);
        var invoiceBases = new List<decimal>();
        foreach (var (line, invoice, document) in chosen)
        {
            var invoiceBase = document.VoucherId is { } vid ? BaseOf(entries, vid, customer) : 0m;
            var settledBase = already[document.Id].Sum(a => a.InvoiceBase);
            var notesBase = (await NotesAsync(document, cancellationToken)).Sum(n => n.VoucherId is { } nid ? BaseOf(entries, nid, !customer) : 0m);
            var clearsInvoice = line.Amount == invoice.Outstanding;
            invoiceBases.Add(clearsInvoice
                ? invoiceBase - settledBase - notesBase
                : Money.Round(line.Amount * document.ExchangeRate, baseCurrency));
        }

        var allocatedTotal = chosen.Sum(c => c.Line.Amount);
        var paymentBaseTotal = Money.Round(allocatedTotal * rate, baseCurrency);
        var paymentBases = new List<decimal>();
        foreach (var (line, _, _) in chosen)
            paymentBases.Add(Money.Round(line.Amount * rate, baseCurrency));
        if (paymentBases.Count > 0)
            paymentBases[^1] += paymentBaseTotal - paymentBases.Sum(); // the parts add up to the whole

        var difference = paymentBases.Sum() - invoiceBases.Sum(); // positive: more company currency paid or received than the invoices were worth
        var gain = customer ? difference : -difference;

        Account? exchangeAccount = null;
        if (gain != 0)
        {
            exchangeAccount = (await accounts.ListAsync(cancellationToken))
                .Where(a => a.Role == AccountRole.ExchangeDifference && a.IsPosting && a.IsActive)
                .OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (exchangeAccount is null)
                throw Refused("exchangeAccount", "fx.account-required");
        }

        var total = allocatedTotal + input.OnAccount;
        var kind = customer ? VoucherKind.Receipt : VoucherKind.Payment;
        var payment = await vouchers.SaveAndPostAsync(null, new VoucherInput(
            kind, input.Date, input.CashAccountId, input.Reference, input.Memo,
            [new VoucherLineInput(null, control!.Id, null, customer ? 0 : total, customer ? total : 0, party.Id)],
            foreign ? currencyCode : null, foreign ? rate : 1m), cancellationToken);

        try
        {
            Guid? settlementId = null;
            if (exchangeAccount is not null)
            {
                var amount = Math.Abs(gain);
                // A gain takes the party's account up and the exchange account down (income); a loss does the opposite.
                var controlLine = new VoucherLineInput(null, control.Id, null, gain > 0 ? amount : 0, gain > 0 ? 0 : amount, party.Id);
                var exchangeLine = new VoucherLineInput(null, exchangeAccount.Id, null, gain > 0 ? 0 : amount, gain > 0 ? amount : 0);
                var settlement = await vouchers.SaveAndPostSystemAsync(
                    new VoucherInput(VoucherKind.FxSettlement, input.Date, null, input.Reference, input.Memo, [controlLine, exchangeLine]),
                    cancellationToken: cancellationToken);
                settlementId = settlement.Id;
            }

            var rows = chosen.Select((c, i) => new Allocation
            {
                CompanyId = company.Id,
                PaymentVoucherId = payment.Id,
                DocumentId = c.Document.Id,
                InvoiceVoucherId = c.Document.VoucherId ?? Guid.Empty,
                SettlementVoucherId = settlementId,
                Amount = c.Line.Amount,
                InvoiceBase = invoiceBases[i],
                PaymentBase = paymentBases[i],
            }).ToList();
            if (rows.Count > 0)
                await allocations.AddAsync(rows, cancellationToken);
            return new SettlementResult(payment, settlementId, gain);
        }
        catch
        {
            await vouchers.DeleteAsync(payment.Id, CancellationToken.None); // nothing half-made is left behind
            throw;
        }
    }

    // ---------------------------------------------------------------- Helpers

    /// <summary>The credit or debit notes issued from an invoice.</summary>
    private async Task<IReadOnlyList<Document>> NotesAsync(Document invoice, CancellationToken cancellationToken)
    {
        var noteKind = invoice.Kind == DocumentKind.SalesInvoice ? DocumentKind.SalesCreditNote : DocumentKind.PurchaseDebitNote;
        return (await documents.SearchAsync(new DocumentSearch(noteKind, DocumentStatus.Issued, invoice.PartyId, Limit: 10_000), cancellationToken))
            .Where(n => n.SourceDocumentId == invoice.Id).ToList();
    }

    /// <summary>The company-currency amount of a voucher's entry on the party's account: the debit of a sales invoice, the credit of a purchase invoice.</summary>
    private static decimal BaseOf(IEnumerable<PartyEntry> entries, Guid voucherId, bool debitSide) =>
        entries.Where(e => e.VoucherId == voucherId).Sum(e => debitSide ? e.Debit : e.Credit);

    private decimal Total(Document document, CompanyInfo company) =>
        DocumentMath.Compute(document.Lines.OrderBy(l => l.LineNumber).ToList(), document.DiscountPercent, CurrencyOf(document.CurrencyCode, company)).Total;

    private async Task<decimal> LatestRateAsync(string currencyCode, DateOnly date, CancellationToken cancellationToken) =>
        (await currencyRates.ListAsync(cancellationToken))
            .Where(r => r.CurrencyCode == currencyCode && r.Date <= date)
            .OrderByDescending(r => r.Date).Select(r => r.Rate).FirstOrDefault();

    private CompanyInfo Company() => files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");

    private static Currency CurrencyOf(string code, CompanyInfo company) =>
        CurrencyCatalog.Find(string.IsNullOrEmpty(code) ? company.BaseCurrencyCode : code)?.Currency ?? new Currency(company.BaseCurrencyCode, 2);

    private static ValidationException Refused(string field, string code) => new([new ValidationIssue(field, code)]);
}
