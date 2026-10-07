using Baba.Domain.Accounting;

namespace Baba.Domain.Budgets;

/// <summary>
/// What a company plans to earn or spend on one revenue or expense account in one month of a fiscal year (brief section 10.4),
/// optionally for one cost center. A budget is all the entries of a fiscal year (and cost center).
/// </summary>
public sealed class BudgetEntry : Entity, ICompanyScoped, INotAudited
{
    public Guid CompanyId { get; set; }

    /// <summary>The fiscal year, named by the calendar year it starts in.</summary>
    public int FiscalYear { get; set; }

    public Guid AccountId { get; set; }
    public Guid? CostCenterId { get; set; }

    /// <summary>The month of the fiscal year, 1 to 12 (1 is the month the year starts in).</summary>
    public int Period { get; set; }

    public long AmountScaled { get; set; }

    public decimal Amount
    {
        get => Scaled.ToDecimal(AmountScaled);
        set => AmountScaled = Scaled.ToScaled(value);
    }
}
