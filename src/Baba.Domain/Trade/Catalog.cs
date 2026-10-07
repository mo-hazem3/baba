using Baba.Domain.Accounting;

namespace Baba.Domain.Trade;

/// <summary>
/// A product or service that is sold or bought (brief section 10.3): code, bilingual names, a unit, default prices and default accounts.
/// Choosing one on a document fills in the description, the price and the account. (Stock tracking comes with the inventory phase.)
/// </summary>
public sealed class Product : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";

    /// <summary>"piece", "hour", "kg"...: shown on documents next to the quantity.</summary>
    public string? Unit { get; set; }

    /// <summary>The price it is sold at, in the company's currency.</summary>
    public long SalePriceScaled { get; set; }

    /// <summary>The price it is bought at, in the company's currency.</summary>
    public long PurchasePriceScaled { get; set; }

    /// <summary>The revenue account sales of it are posted to.</summary>
    public Guid? SalesAccountId { get; set; }

    /// <summary>The expense or asset account purchases of it are posted to.</summary>
    public Guid? PurchaseAccountId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public decimal SalePrice
    {
        get => Scaled.ToDecimal(SalePriceScaled);
        set => SalePriceScaled = Scaled.ToScaled(value);
    }

    public decimal PurchasePrice
    {
        get => Scaled.ToDecimal(PurchasePriceScaled);
        set => PurchasePriceScaled = Scaled.ToScaled(value);
    }
}

/// <summary>
/// A named set of prices (brief section 10.3), for example "Wholesale" or "Dollar prices". A customer can be given one; it then overrides
/// the products' own sale prices for the products it lists, in its own currency.
/// </summary>
public sealed class PriceList : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";

    /// <summary>The currency the prices are in. Empty means the company's own currency.</summary>
    public string CurrencyCode { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public List<PriceListLine> Lines { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class PriceListLine : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid PriceListId { get; set; }
    public Guid ProductId { get; set; }
    public long PriceScaled { get; set; }

    public decimal Price
    {
        get => Scaled.ToDecimal(PriceScaled);
        set => PriceScaled = Scaled.ToScaled(value);
    }
}
