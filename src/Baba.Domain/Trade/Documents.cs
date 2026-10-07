using Baba.Domain.Accounting;

namespace Baba.Domain.Trade;

public enum DocumentKind
{
    /// <summary>عرض سعر: an offer to a customer. Posts nothing.</summary>
    Quote,

    /// <summary>أمر بيع: what a customer has ordered. Posts nothing.</summary>
    SalesOrder,

    /// <summary>إذن تسليم: goods or services handed over. Posts nothing (stock arrives in Phase 5).</summary>
    DeliveryNote,

    /// <summary>فاتورة مبيعات: debits the customer's receivable account and credits revenue.</summary>
    SalesInvoice,

    /// <summary>إشعار دائن: takes back some or all of a sales invoice. The opposite of an invoice.</summary>
    SalesCreditNote,

    /// <summary>أمر شراء: what was ordered from a supplier. Posts nothing.</summary>
    PurchaseOrder,

    /// <summary>إذن استلام: goods or services received. Posts nothing.</summary>
    GoodsReceipt,

    /// <summary>فاتورة مشتريات: debits expenses or assets and credits the supplier's payable account.</summary>
    PurchaseInvoice,

    /// <summary>إشعار مدين: takes back some or all of a purchase invoice. The opposite of a purchase invoice.</summary>
    PurchaseDebitNote,
}

public enum DocumentStatus
{
    /// <summary>Saved for later. Has no number and no effect on the books.</summary>
    Draft,

    /// <summary>Numbered. For invoices and credit or debit notes this means posted to the ledger.</summary>
    Issued,

    /// <summary>A quote, order or delivery/receipt note that has been turned into the next document in the chain.</summary>
    Converted,
}

/// <summary>What each kind of document is and what can follow it, so the rules are written once.</summary>
public static class DocumentKindExtensions
{
    public static bool IsSales(this DocumentKind kind) => kind is DocumentKind.Quote or DocumentKind.SalesOrder or DocumentKind.DeliveryNote or DocumentKind.SalesInvoice or DocumentKind.SalesCreditNote;

    public static bool IsPurchase(this DocumentKind kind) => !kind.IsSales();

    /// <summary>The customer or supplier a document of this kind is for.</summary>
    public static PartyKind PartyKind(this DocumentKind kind) => kind.IsSales() ? Accounting.PartyKind.Customer : Accounting.PartyKind.Supplier;

    /// <summary>Invoices and credit or debit notes make ledger entries (through a voucher that Baba makes); the others do not.</summary>
    public static bool Posts(this DocumentKind kind) =>
        kind is DocumentKind.SalesInvoice or DocumentKind.SalesCreditNote or DocumentKind.PurchaseInvoice or DocumentKind.PurchaseDebitNote;

    /// <summary>A credit or debit note: it undoes an invoice, so its lines go the other way.</summary>
    public static bool IsNote(this DocumentKind kind) => kind is DocumentKind.SalesCreditNote or DocumentKind.PurchaseDebitNote;

    /// <summary>The voucher kind a posting document is made into.</summary>
    public static VoucherKind VoucherKind(this DocumentKind kind) => kind switch
    {
        DocumentKind.SalesInvoice => Accounting.VoucherKind.SalesInvoice,
        DocumentKind.SalesCreditNote => Accounting.VoucherKind.SalesCreditNote,
        DocumentKind.PurchaseInvoice => Accounting.VoucherKind.PurchaseInvoice,
        DocumentKind.PurchaseDebitNote => Accounting.VoucherKind.PurchaseDebitNote,
        _ => throw new InvalidOperationException($"{kind} does not post."),
    };

    /// <summary>The documents one click can turn this one into (brief section 10.3).</summary>
    public static IReadOnlyList<DocumentKind> ConvertibleTo(this DocumentKind kind) => kind switch
    {
        DocumentKind.Quote => [DocumentKind.SalesOrder, DocumentKind.SalesInvoice],
        DocumentKind.SalesOrder => [DocumentKind.DeliveryNote, DocumentKind.SalesInvoice],
        DocumentKind.DeliveryNote => [DocumentKind.SalesInvoice],
        DocumentKind.SalesInvoice => [DocumentKind.SalesCreditNote],
        DocumentKind.PurchaseOrder => [DocumentKind.GoodsReceipt, DocumentKind.PurchaseInvoice],
        DocumentKind.GoodsReceipt => [DocumentKind.PurchaseInvoice],
        DocumentKind.PurchaseInvoice => [DocumentKind.PurchaseDebitNote],
        _ => [],
    };

    /// <summary>Quotes, orders and delivery or receipt notes are used up by the next document in the chain; invoices can have any number of notes.</summary>
    public static bool IsConsumedByConversion(this DocumentKind kind) => !kind.IsNote() && !kind.Posts();
}

/// <summary>
/// A sales or purchase document (brief section 10.3): quote, order, delivery or receipt note, invoice, credit or debit note. A document
/// belongs to a customer or supplier and has lines of quantity, price and discount. Invoices and notes post to the ledger through a voucher
/// that Baba makes and keeps in step with the document.
/// </summary>
public sealed class Document : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public DocumentKind Kind { get; set; }

    /// <summary>QT-2026-0001 and so on, given when the document is first issued. An invoice has the number of its voucher.</summary>
    public string? Number { get; set; }

    public DateOnly Date { get; set; }
    public DateOnly? DueDate { get; set; }
    public DocumentStatus Status { get; set; }
    public Guid PartyId { get; set; }

    public string CurrencyCode { get; set; } = "";

    /// <summary>How many company-currency units one unit of the document's currency is worth, in millionths.</summary>
    public long ExchangeRateScaled { get; set; } = FxRate.One;

    /// <summary>The customer's or supplier's own reference, such as their purchase order number.</summary>
    public string? Reference { get; set; }

    public string? Memo { get; set; }

    /// <summary>A discount on the whole document, in percent times 10,000 (10% is 100,000).</summary>
    public long DiscountPercentScaled { get; set; }

    /// <summary>The document this one was made from (a quote for an order, an invoice for a credit note).</summary>
    public Guid? SourceDocumentId { get; set; }

    /// <summary>The document this one was turned into, while it is <see cref="DocumentStatus.Converted"/>.</summary>
    public Guid? ConvertedToId { get; set; }

    /// <summary>The voucher an invoice or note posts through.</summary>
    public Guid? VoucherId { get; set; }

    public List<DocumentLine> Lines { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public decimal ExchangeRate
    {
        get => FxRate.ToDecimal(ExchangeRateScaled);
        set => ExchangeRateScaled = FxRate.ToScaled(value);
    }

    public decimal DiscountPercent
    {
        get => DiscountPercentScaled / Scaled.Scale;
        set => DiscountPercentScaled = Scaled.ToScaled(value);
    }
}

public sealed class DocumentLine : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public int LineNumber { get; set; }
    public Guid? ProductId { get; set; }

    /// <summary>The revenue account (sales) or expense or asset account (purchases) the line is posted to. Needed by the time an invoice is issued.</summary>
    public Guid? AccountId { get; set; }

    public string? Description { get; set; }
    public long QuantityScaled { get; set; }
    public long UnitPriceScaled { get; set; }

    /// <summary>A discount on the line, in percent times 10,000.</summary>
    public long DiscountPercentScaled { get; set; }

    public Guid? CostCenterId { get; set; }

    public decimal Quantity
    {
        get => Scaled.ToDecimal(QuantityScaled);
        set => QuantityScaled = Scaled.ToScaled(value);
    }

    public decimal UnitPrice
    {
        get => Scaled.ToDecimal(UnitPriceScaled);
        set => UnitPriceScaled = Scaled.ToScaled(value);
    }

    public decimal DiscountPercent
    {
        get => DiscountPercentScaled / Scaled.Scale;
        set => DiscountPercentScaled = Scaled.ToScaled(value);
    }
}

/// <summary>QT-2026-0001 and so on, for the documents that do not post. (Invoices and notes take the number of their voucher.)</summary>
public static class DocumentNumber
{
    public static string Prefix(DocumentKind kind) => kind switch
    {
        DocumentKind.Quote => "QT",
        DocumentKind.SalesOrder => "SO",
        DocumentKind.DeliveryNote => "DL",
        DocumentKind.PurchaseOrder => "PO",
        DocumentKind.GoodsReceipt => "GR",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "This kind of document takes the number of its voucher."),
    };

    public static string Format(DocumentKind kind, int fiscalYear, int sequence) => $"{Prefix(kind)}-{fiscalYear}-{sequence:0000}";
}

/// <summary>The money of a document: line amounts, the document discount, and the total, all rounded to the document currency's decimals.</summary>
public sealed record DocumentTotals(
    IReadOnlyList<decimal> LineAmounts,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    IReadOnlyList<decimal> PostedAmounts);

public static class DocumentMath
{
    /// <summary>
    /// Line amount = quantity x price less the line discount, rounded. The document discount is taken off the subtotal, and the amounts
    /// that are posted are each line's share of that (rounded), with any rounding left over added to the biggest line, so the posted
    /// amounts always add up to the total exactly.
    /// </summary>
    public static DocumentTotals Compute(IReadOnlyList<DocumentLine> lines, decimal documentDiscountPercent, Currency currency)
    {
        var amounts = lines
            .Select(l => Money.Round(l.Quantity * l.UnitPrice * (1 - l.DiscountPercent / 100m), currency))
            .ToList();
        var subtotal = amounts.Sum();
        var discount = Money.Round(subtotal * documentDiscountPercent / 100m, currency);
        var total = subtotal - discount;

        var posted = amounts.Select(a => Money.Round(a * (1 - documentDiscountPercent / 100m), currency)).ToList();
        var leftover = total - posted.Sum();
        if (leftover != 0 && posted.Count > 0)
        {
            var biggest = amounts.IndexOf(amounts.MaxBy(Math.Abs));
            posted[biggest] += leftover;
        }

        return new DocumentTotals(amounts, subtotal, discount, total, posted);
    }
}
