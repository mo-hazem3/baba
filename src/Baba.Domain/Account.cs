namespace Baba.Domain;

/// <summary>What an account is used for by the app itself (balances on the Summary page, voucher cash accounts, year-end close).</summary>
public enum AccountRole
{
    None,

    /// <summary>A bank or cash account. Payment and receipt vouchers are paid from and received into these.</summary>
    CashOrBank,
    Receivable,
    Payable,
    RetainedEarnings,

    /// Where realised gains and losses from exchange-rate differences are posted when a foreign-currency invoice is paid.
    ExchangeDifference,
}

/// <summary>A node in the chart of accounts tree (unlimited depth). Entries are posted to posting accounts only.</summary>
public sealed class Account : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public Guid? ParentId { get; set; }
    public AccountType Type { get; set; }
    public bool IsPosting { get; set; }
    public bool IsActive { get; set; } = true;
    public AccountRole Role { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Assets and expenses grow with debits; liabilities, equity and revenue grow with credits.</summary>
    public bool IsDebitNormal => Type is AccountType.Asset or AccountType.Expense;
}
