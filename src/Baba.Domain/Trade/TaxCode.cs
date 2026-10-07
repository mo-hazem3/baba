using Baba.Domain.Accounting;

namespace Baba.Domain.Trade;

/// <summary>How a tax code treats the sale or purchase it is put on.</summary>
public enum TaxTreatment
{
    /// <summary>Taxed at the code's rate.</summary>
    Standard,

    /// <summary>Taxable, but at a rate of zero (reported, no tax charged).</summary>
    Zero,

    /// <summary>Not taxed by law.</summary>
    Exempt,

    /// <summary>Outside the scope of the tax.</summary>
    OutOfScope,
}

/// <summary>
/// A tax code (brief sections 8 and 10.3): a rate, how it is treated, when it applies, and the two accounts it posts to: the tax payable
/// on sales (output) and the tax recoverable on purchases (input). The codes of the company's country come with the country pack; a
/// company can add its own. A document line keeps a copy of the rate it was written with, so changing or retiring a code never rewrites
/// an old document.
/// </summary>
public sealed class TaxCode : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";

    /// <summary>The rate in percent times 10,000 (15% is 150,000).</summary>
    public long RateScaled { get; set; }

    public TaxTreatment Treatment { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>Tax collected on sales is posted here (a liability).</summary>
    public Guid? OutputAccountId { get; set; }

    /// <summary>Tax paid on purchases is posted here (an asset: it is claimed back).</summary>
    public Guid? InputAccountId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>The code new document lines start with (at most one).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Came with the country pack. Such a code keeps its rate, treatment and dates; only its accounts and switches change.</summary>
    public bool FromPack { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public decimal Rate
    {
        get => Scaled.ToDecimal(RateScaled);
        set => RateScaled = Scaled.ToScaled(value);
    }

    public bool AppliesOn(DateOnly date) => (EffectiveFrom is null || EffectiveFrom <= date) && (EffectiveTo is null || date <= EffectiveTo);
}
