namespace Baba.Domain.Accounting;

/// <summary>
/// One line of the general ledger. Every report is computed from these, and balances are never stored.
/// Ledger entries are made only by <see cref="PostingEngine"/> when a voucher is posted, and are replaced as a set when a
/// posted voucher is edited: nothing else can create or change one (the constructor and setters are not public).
/// </summary>
public sealed class LedgerEntry : Entity, ICompanyScoped, INotAudited
{
    private LedgerEntry()
    {
    }

    public Guid CompanyId { get; set; }
    public Guid VoucherId { get; private set; }
    public DateOnly Date { get; private set; }
    public Guid AccountId { get; private set; }
    public string? Description { get; private set; }

    /// <summary>The position within its voucher (0, 1, 2 ...), so statements list a voucher's entries in a stable order.</summary>
    public int Sequence { get; private set; }

    /// <summary>The amounts in the transaction currency.</summary>
    public long DebitScaled { get; private set; }

    public long CreditScaled { get; private set; }
    public string CurrencyCode { get; private set; } = "";
    public long FxRateScaled { get; private set; } = FxRate.One;

    /// <summary>The same amounts in the company's base currency. Reports add these up.</summary>
    public long BaseDebitScaled { get; private set; }

    public long BaseCreditScaled { get; private set; }

    public decimal Debit => Scaled.ToDecimal(DebitScaled);
    public decimal Credit => Scaled.ToDecimal(CreditScaled);
    public decimal BaseDebit => Scaled.ToDecimal(BaseDebitScaled);
    public decimal BaseCredit => Scaled.ToDecimal(BaseCreditScaled);

    internal static LedgerEntry Create(
        Guid companyId, Guid voucherId, DateOnly date, Guid accountId, string? description, int sequence,
        long debitScaled, long creditScaled, string currencyCode, long fxRateScaled,
        long baseDebitScaled, long baseCreditScaled) => new()
    {
        CompanyId = companyId,
        VoucherId = voucherId,
        Date = date,
        AccountId = accountId,
        Description = description,
        Sequence = sequence,
        DebitScaled = debitScaled,
        CreditScaled = creditScaled,
        CurrencyCode = currencyCode,
        FxRateScaled = fxRateScaled,
        BaseDebitScaled = baseDebitScaled,
        BaseCreditScaled = baseCreditScaled,
    };
}
