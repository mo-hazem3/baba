using Baba.Domain.Accounting;

namespace Baba.Domain.Trade;

/// <summary>
/// Part of a receipt or payment set against one invoice (brief section 10.3). It says how much of the invoice was paid, in the invoice's
/// currency, and what that was worth in the company's currency on each side: on the invoice (at its rate) and on the payment (at the
/// payment's rate). The difference between the two is the exchange gain or loss, posted by the settlement voucher.
/// </summary>
public sealed class Allocation : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }

    /// <summary>The receipt or payment voucher. Deleting it deletes its allocations.</summary>
    public Guid PaymentVoucherId { get; set; }

    public Guid DocumentId { get; set; }

    /// <summary>The ledger voucher of the invoice, so reports can find its entry without opening the document.</summary>
    public Guid InvoiceVoucherId { get; set; }

    /// <summary>The voucher that books the exchange difference of the whole payment, when there was one.</summary>
    public Guid? SettlementVoucherId { get; set; }

    /// <summary>The part of the invoice paid, in the invoice's currency, in ten-thousandths.</summary>
    public long AmountScaled { get; set; }

    /// <summary>What that part of the invoice was worth in the company's currency, at the invoice's rate.</summary>
    public long InvoiceBaseScaled { get; set; }

    /// <summary>What the same part of the payment is worth in the company's currency, at the payment's rate.</summary>
    public long PaymentBaseScaled { get; set; }

    public decimal Amount
    {
        get => Scaled.ToDecimal(AmountScaled);
        set => AmountScaled = Scaled.ToScaled(value);
    }

    public decimal InvoiceBase
    {
        get => Scaled.ToDecimal(InvoiceBaseScaled);
        set => InvoiceBaseScaled = Scaled.ToScaled(value);
    }

    public decimal PaymentBase
    {
        get => Scaled.ToDecimal(PaymentBaseScaled);
        set => PaymentBaseScaled = Scaled.ToScaled(value);
    }
}
