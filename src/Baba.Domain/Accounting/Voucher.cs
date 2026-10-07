namespace Baba.Domain.Accounting;

public enum VoucherKind
{
    /// <summary>سند صرف: money paid out of a bank or cash account.</summary>
    Payment,

    /// <summary>سند قبض: money received into a bank or cash account.</summary>
    Receipt,

    /// <summary>قيد يومية: free debit and credit lines.</summary>
    Journal,

    /// <summary>سند تحويل: money moved from one bank or cash account to another. One line (the account it goes to).</summary>
    Transfer,

    /// <summary>أرصدة افتتاحية: the balances of the balance sheet accounts on the day the books start. At most one per company.</summary>
    Opening,

    /// <summary>قيد إقفال: made by closing a fiscal year, which moves the year's profit into retained earnings. Never edited by hand.</summary>
    Closing,

    /// <summary>The ledger entries of a sales invoice, made and kept in step by the invoice itself. Never edited by hand.</summary>
    SalesInvoice,

    /// <summary>The ledger entries of a sales credit note. Made by the credit note itself.</summary>
    SalesCreditNote,

    /// <summary>The ledger entries of a purchase invoice. Made by the invoice itself.</summary>
    PurchaseInvoice,

    /// <summary>The ledger entries of a purchase debit note. Made by the debit note itself.</summary>
    PurchaseDebitNote,

    /// <summary>The exchange gain or loss when a payment is worth a different amount in the company's currency than the invoices it pays. Made by the payment itself.</summary>
    FxSettlement,

    /// <summary>What the stock sold on an invoice cost (or the stock taken back on a credit note): cost of sales against the stock account. Made by the invoice.</summary>
    StockCost,

    /// <summary>The value of an opening stock or a stock adjustment: the stock account against the other side. Made by the stock document.</summary>
    StockAdjustment,

    /// <summary>One month of depreciation of the asset register: expense against accumulated depreciation. Made by the depreciation run.</summary>
    Depreciation,

    /// <summary>The disposal of an asset: its cost and depreciation come off, the proceeds come in, the difference is a gain or loss. Made by the disposal.</summary>
    AssetDisposal,

    /// <summary>One month's payroll: salaries and employer insurance charged, what is owed to employees, insurance and deductions credited. Made by the payroll run.</summary>
    Payroll,

    /// <summary>The payment of a payroll run's net salaries out of a bank or cash account. Made by the run's Pay action.</summary>
    SalaryPayment,

    /// <summary>The monthly addition to (or release from) the end-of-service provision. Made by the provision run.</summary>
    EndOfServiceAccrual,

    /// <summary>An approved expense claim: the expenses against what the company owes the employee. Made by the claim.</summary>
    ExpenseClaim,

    /// <summary>The payment of an approved expense claim to the employee. Made by the claim's Pay action.</summary>
    ClaimPayment,
}

public enum VoucherStatus
{
    /// <summary>Saved for later. Makes no ledger entries.</summary>
    Draft,

    /// <summary>Submitted. Its ledger entries exist and show in every report.</summary>
    Posted,
}

/// <summary>
/// A payment, receipt or journal voucher. A draft has no ledger entries; posting creates them, and editing a posted voucher
/// regenerates them. Ledger entries are never edited directly (see <see cref="PostingEngine"/>).
/// </summary>
public sealed class Voucher : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public VoucherKind Kind { get; set; }

    /// <summary>The human-readable number, such as PV-2026-0001. Given when the voucher is first posted (so a new draft has none) and kept for good.</summary>
    public string? Number { get; set; }

    /// <summary>The business date (not a time), stored as a date only.</summary>
    public DateOnly Date { get; set; }

    public VoucherStatus Status { get; set; }

    /// <summary>The transaction currency. The data model supports other currencies from day one; the screens come later.</summary>
    public string CurrencyCode { get; set; } = "";

    /// <summary>How many base-currency units one unit of <see cref="CurrencyCode"/> is worth, in millionths (1,000,000 means 1).</summary>
    public long ExchangeRateScaled { get; set; } = FxRate.One;

    /// <summary>The bank or cash account a payment is paid from, or a receipt is received into. Not used by journal vouchers.</summary>
    public Guid? CashAccountId { get; set; }

    /// <summary>The sales or purchase document this voucher is the ledger side of. Such a voucher is changed only through its document.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>A cheque number, bank reference or similar.</summary>
    public string? Reference { get; set; }

    public string? Memo { get; set; }
    public DateTime? PostedAt { get; set; }

    public List<VoucherLine> Lines { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public decimal ExchangeRate
    {
        get => FxRate.ToDecimal(ExchangeRateScaled);
        set => ExchangeRateScaled = FxRate.ToScaled(value);
    }

    public decimal TotalDebit => Scaled.ToDecimal(Lines.Sum(l => l.DebitScaled));
    public decimal TotalCredit => Scaled.ToDecimal(Lines.Sum(l => l.CreditScaled));
}

/// <summary>
/// One line of a voucher. Payment lines carry a debit (what the money was spent on), receipt lines a credit (where the money
/// came from), and journal lines either. The bank or cash side of a payment or receipt is added when the voucher is posted.
/// </summary>
public sealed class VoucherLine : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid VoucherId { get; set; }

    /// <summary>1, 2, 3 ... in the order shown on screen and in print.</summary>
    public int LineNumber { get; set; }

    public Guid AccountId { get; set; }
    public string? Description { get; set; }

    /// <summary>The customer or supplier this line belongs to. Required on receivable and payable accounts, not allowed on others.</summary>
    public Guid? PartyId { get; set; }

    /// <summary>An optional cost center or project tag.</summary>
    public Guid? CostCenterId { get; set; }

    public long DebitScaled { get; set; }
    public long CreditScaled { get; set; }

    public decimal Debit
    {
        get => Scaled.ToDecimal(DebitScaled);
        set => DebitScaled = Scaled.ToScaled(value);
    }

    public decimal Credit
    {
        get => Scaled.ToDecimal(CreditScaled);
        set => CreditScaled = Scaled.ToScaled(value);
    }
}

/// <summary>What each kind of voucher does, so the rules are written once instead of in every place that looks at the kind.</summary>
public static class VoucherKindExtensions
{
    /// <summary>Paid from or received into one bank or cash account, named on the voucher.</summary>
    public static bool UsesCashAccount(this VoucherKind kind) => kind is VoucherKind.Payment or VoucherKind.Receipt or VoucherKind.Transfer;

    /// <summary>Money leaves the bank or cash account: the lines are debits and the account is credited for the total.</summary>
    public static bool PaysOut(this VoucherKind kind) => kind is VoucherKind.Payment or VoucherKind.Transfer;

    /// <summary>Lines are written as debits and credits that the user balances.</summary>
    public static bool HasFreeLines(this VoucherKind kind) =>
        kind is VoucherKind.Journal or VoucherKind.Opening or VoucherKind.Closing or VoucherKind.FxSettlement or VoucherKind.StockCost or VoucherKind.StockAdjustment or VoucherKind.Depreciation or VoucherKind.AssetDisposal or VoucherKind.Payroll or VoucherKind.SalaryPayment or VoucherKind.EndOfServiceAccrual or VoucherKind.ExpenseClaim or VoucherKind.ClaimPayment || kind.IsDocument();

    /// <summary>Made by Baba itself (closing a year, an invoice, an exchange difference) and never saved or deleted by hand.</summary>
    public static bool IsSystemMade(this VoucherKind kind) =>
        kind is VoucherKind.Closing or VoucherKind.FxSettlement or VoucherKind.StockCost or VoucherKind.StockAdjustment or VoucherKind.Depreciation or VoucherKind.AssetDisposal or VoucherKind.Payroll or VoucherKind.SalaryPayment or VoucherKind.EndOfServiceAccrual or VoucherKind.ExpenseClaim or VoucherKind.ClaimPayment || kind.IsDocument();

    /// <summary>
    /// The ledger side of an invoice or a credit or debit note. Its first line is the customer's or supplier's account for the whole
    /// document, and that line absorbs the rounding when the other lines are converted to the company currency, so it balances exactly.
    /// </summary>
    public static bool IsDocument(this VoucherKind kind) =>
        kind is VoucherKind.SalesInvoice or VoucherKind.SalesCreditNote or VoucherKind.PurchaseInvoice or VoucherKind.PurchaseDebitNote;
}
