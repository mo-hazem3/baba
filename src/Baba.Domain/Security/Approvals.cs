namespace Baba.Domain.Security;

public enum ApprovalSubject
{
    Voucher,
    Document,
}

public enum ApprovalState
{
    /// <summary>Waiting for someone who may approve.</summary>
    Pending,

    /// <summary>Approved: the voucher was posted or the document issued.</summary>
    Approved,

    /// <summary>Not approved. The person who sent it can change it and send it again.</summary>
    Rejected,
}

/// <summary>
/// A voucher or document sent for approval (brief section 10.4: an approval workflow for vouchers and invoices). While the company asks for
/// approval, only people with the Approve permission can post or issue; everyone else saves a draft and sends it here.
/// </summary>
public sealed class ApprovalRequest : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public ApprovalSubject Subject { get; set; }
    public Guid SubjectId { get; set; }
    public ApprovalState State { get; set; }

    /// <summary>What the sender wrote to the approver.</summary>
    public string? Note { get; set; }

    public string SubmittedBy { get; set; } = "";
    public DateTime SubmittedAt { get; set; }

    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }

    /// <summary>Why it was not approved.</summary>
    public string? DecisionNote { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>The company's security choices (there is one row).</summary>
public sealed class SecuritySettings : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }

    /// <summary>Posting a voucher or issuing an invoice needs someone with the Approve permission; others send theirs for approval.</summary>
    public bool ApprovalRequired { get; set; }
}
