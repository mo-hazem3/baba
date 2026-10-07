using Baba.Domain.Accounting;

namespace Baba.Domain.Claims;

public enum ClaimStatus
{
    /// <summary>Being written. Changes nothing in the books.</summary>
    Draft,

    /// <summary>Sent for approval.</summary>
    Submitted,

    /// <summary>Approved: the expenses are in the books and the company owes the employee.</summary>
    Approved,

    /// <summary>Not approved. The employee can change it and send it again.</summary>
    Rejected,

    /// <summary>Approved and paid back to the employee.</summary>
    Paid,
}

/// <summary>An expense an employee paid for and wants back (brief section 10.4): travel, a taxi, supplies.</summary>
public sealed class ExpenseClaim : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }

    /// <summary>EC-2026-0001.</summary>
    public string Number { get; set; } = "";

    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public string? Memo { get; set; }
    public ClaimStatus Status { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }

    /// <summary>The entry made when the claim was approved: the expenses against what is owed to the employee.</summary>
    public Guid? VoucherId { get; set; }

    /// <summary>The entry that paid the employee back.</summary>
    public Guid? PaymentVoucherId { get; set; }

    public DateOnly? PaidDate { get; set; }
    public List<ExpenseClaimLine> Lines { get; set; } = [];

    public decimal Total => Lines.Sum(l => l.Amount);

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class ExpenseClaimLine : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid ClaimId { get; set; }
    public int LineNumber { get; set; }
    public DateOnly Date { get; set; }
    public string? Description { get; set; }

    /// <summary>The expense account the amount is charged to.</summary>
    public Guid AccountId { get; set; }

    public long AmountScaled { get; set; }
    public Guid? CostCenterId { get; set; }

    public decimal Amount
    {
        get => Scaled.ToDecimal(AmountScaled);
        set => AmountScaled = Scaled.ToScaled(value);
    }
}
