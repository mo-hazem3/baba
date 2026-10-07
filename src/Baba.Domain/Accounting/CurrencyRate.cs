namespace Baba.Domain.Accounting;

/// <summary>
/// How many base-currency units one unit of a foreign currency was worth from a date on (in millionths, like voucher rates). A document
/// in a foreign currency starts from the latest rate on or before its date, and the person can change it (brief section 5).
/// </summary>
public sealed class CurrencyRate : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string CurrencyCode { get; set; } = "";
    public DateOnly Date { get; set; }
    public long RateScaled { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public decimal Rate
    {
        get => FxRate.ToDecimal(RateScaled);
        set => RateScaled = FxRate.ToScaled(value);
    }
}
