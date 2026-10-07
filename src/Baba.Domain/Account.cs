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

    /// <summary>Where tax collected on sales is posted (a liability). The default account of the tax codes.</summary>
    TaxPayable,

    /// <summary>Where tax paid on purchases is posted (an asset, claimed back). The default account of the tax codes.</summary>
    TaxReceivable,

    /// <summary>The stock account: what stock on hand is worth. Bought stock is posted here and sold stock leaves it.</summary>
    Inventory,

    /// <summary>The expense a sale of stock is charged to (what the stock sold cost).</summary>
    CostOfSales,

    /// <summary>Where gains and losses found by counting stock are posted.</summary>
    InventoryAdjustment,

    /// <summary>The account depreciation builds up in (an asset account with a credit balance). The default of a new asset.</summary>
    AccumulatedDepreciation,

    /// <summary>The expense monthly depreciation is charged to. The default of a new asset.</summary>
    DepreciationExpense,

    /// <summary>Where the gain or loss on selling or scrapping an asset is posted. The default of a disposal.</summary>
    AssetDisposal,
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
