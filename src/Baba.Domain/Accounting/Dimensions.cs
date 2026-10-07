namespace Baba.Domain.Accounting;

public enum PartyKind
{
    /// <summary>Someone who owes the company money (sales, receivables).</summary>
    Customer,

    /// <summary>Someone the company owes money to (purchases, payables).</summary>
    Supplier,
}

/// <summary>
/// A customer or supplier. Ledger lines on a receivable or payable account are tagged with the party they belong to, which is what
/// makes the sub-ledger: statements, balances and aging per party are all computed from those tags (brief section 10.2).
/// </summary>
public sealed class Party : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public PartyKind Kind { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    /// <summary>The party's own tax or commercial registration number, kept as typed (formats are the country pack's business).</summary>
    public string? TaxNumber { get; set; }

    /// <summary>The most a customer should owe, in the base currency. 0 means no limit.</summary>
    public long CreditLimitScaled { get; set; }

    /// <summary>Days after the date of an entry when it falls due. 0 means due at once. Used by the aging reports.</summary>
    public int PaymentTermsDays { get; set; }

    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public decimal CreditLimit
    {
        get => Scaled.ToDecimal(CreditLimitScaled);
        set => CreditLimitScaled = Scaled.ToScaled(value);
    }
}

/// <summary>
/// A cost center or project: an optional tag on any ledger line, so a profit and loss can be read for one department or job
/// (brief section 10.2).
/// </summary>
public sealed class CostCenter : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
