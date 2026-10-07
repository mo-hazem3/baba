using Baba.Domain.Accounting;

namespace Baba.Domain.Assets;

/// <summary>Tangible assets are depreciated; intangible ones (software, licences) are amortized the same way.</summary>
public enum AssetKind
{
    Tangible,
    Intangible,
}

public enum DepreciationMethod
{
    /// <summary>The same amount every month over the useful life.</summary>
    StraightLine,

    /// <summary>A fixed yearly percentage of what is left (the book value), so the amounts shrink.</summary>
    DecliningBalance,
}

public enum AssetStatus
{
    Active,
    Disposed,
}

/// <summary>
/// An entry of the fixed or intangible asset register (brief section 10.4). The asset's cost is posted to the ledger like any purchase;
/// the register adds what the ledger cannot say: the depreciation schedule, the book value, and the disposal.
/// </summary>
public sealed class FixedAsset : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public AssetKind Kind { get; set; }

    /// <summary>The day it was bought or put into use. Depreciation starts in this month, for the whole month.</summary>
    public DateOnly AcquisitionDate { get; set; }

    /// <summary>What it cost, in the company's currency.</summary>
    public long CostScaled { get; set; }

    /// <summary>What it is expected to be worth at the end of its life. Depreciation never goes below it.</summary>
    public long SalvageScaled { get; set; }

    public int UsefulLifeMonths { get; set; }
    public DepreciationMethod Method { get; set; }

    /// <summary>Declining balance only: the yearly rate in percent, times 10,000 (20% is 200,000).</summary>
    public long AnnualRateScaled { get; set; }

    /// <summary>The account that holds the asset's cost (an asset account).</summary>
    public Guid AssetAccountId { get; set; }

    /// <summary>The account that holds the depreciation so far (an asset account that normally has a credit balance).</summary>
    public Guid AccumulatedAccountId { get; set; }

    /// <summary>The expense the monthly depreciation is charged to.</summary>
    public Guid ExpenseAccountId { get; set; }

    public Guid? CostCenterId { get; set; }

    /// <summary>For an asset that was already in use before the books started: depreciation recorded so far, in the opening balances.</summary>
    public long OpeningAccumulatedScaled { get; set; }

    /// <summary>For such an asset: the first day of the last month that is already depreciated in that amount. Baba continues from the month after.</summary>
    public DateOnly? DepreciatedThrough { get; set; }

    public AssetStatus Status { get; set; } = AssetStatus.Active;
    public DateOnly? DisposalDate { get; set; }
    public long DisposalProceedsScaled { get; set; }
    public Guid? DisposalVoucherId { get; set; }
    public string? Notes { get; set; }

    public decimal Cost
    {
        get => Scaled.ToDecimal(CostScaled);
        set => CostScaled = Scaled.ToScaled(value);
    }

    public decimal Salvage
    {
        get => Scaled.ToDecimal(SalvageScaled);
        set => SalvageScaled = Scaled.ToScaled(value);
    }

    public decimal AnnualRate
    {
        get => Scaled.ToDecimal(AnnualRateScaled);
        set => AnnualRateScaled = Scaled.ToScaled(value);
    }

    public decimal OpeningAccumulated
    {
        get => Scaled.ToDecimal(OpeningAccumulatedScaled);
        set => OpeningAccumulatedScaled = Scaled.ToScaled(value);
    }

    public decimal DisposalProceeds
    {
        get => Scaled.ToDecimal(DisposalProceedsScaled);
        set => DisposalProceedsScaled = Scaled.ToScaled(value);
    }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>One month of depreciation of one asset, posted in a voucher.</summary>
public sealed class AssetDepreciation : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid AssetId { get; set; }

    /// <summary>The first day of the month depreciated.</summary>
    public DateOnly Month { get; set; }

    public long AmountScaled { get; set; }

    /// <summary>The voucher that posted it (dated the last day of the month).</summary>
    public Guid VoucherId { get; set; }

    public decimal Amount
    {
        get => Scaled.ToDecimal(AmountScaled);
        set => AmountScaled = Scaled.ToScaled(value);
    }
}
