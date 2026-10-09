using Baba.Application.Abstractions;
using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Application.Trade;
using Baba.Domain.Security;
using Baba.Domain.Trade;

namespace Baba.Application.Security;

public interface IApprovalStore
{
    Task<IReadOnlyList<ApprovalRequest>> ListApprovalsAsync(CancellationToken cancellationToken = default);
    Task<ApprovalRequest?> FindApprovalAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default);
    Task UpdateApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default);

    /// <summary>Takes away the requests of a voucher or document that were not approved (it was changed or deleted, so they no longer describe it).</summary>
    Task RemoveOpenApprovalsAsync(ApprovalSubject subject, Guid subjectId, CancellationToken cancellationToken = default);
}

public sealed record ApprovalDto(
    Guid Id,
    ApprovalSubject Subject,
    Guid SubjectId,
    ApprovalState State,
    /// <summary>The kind of voucher (Payment, Receipt ...) or document (SalesInvoice ...), as the enum name.</summary>
    string Kind,
    string? Number,
    DateOnly Date,
    decimal Amount,
    string CurrencyCode,
    string? Note,
    string SubmittedBy,
    DateTime SubmittedAt,
    string? DecidedBy,
    DateTime? DecidedAt,
    string? DecisionNote,
    /// <summary>The signed-in person may approve or reject this one.</summary>
    bool CanDecide);

public sealed record SubmitInput(string? Note);

public sealed record RejectInput(string? Note);

/// <summary>Sending vouchers and invoices for approval, and approving or rejecting them (brief section 10.4).</summary>
public sealed class ApprovalService(
    IApprovalStore store,
    VoucherService vouchers,
    DocumentService documents,
    AccessService access,
    ICurrentUser currentUser,
    ICompanyFiles files,
    TimeProvider clock)
{
    private CompanyInfo CompanyOrThrow() => files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");

    private static string AreaOf(ApprovalSubject subject) => subject == ApprovalSubject.Voucher ? PermissionAreas.Accounting : PermissionAreas.Trade;

    /// <summary>Saves a voucher as a draft and sends it for approval.</summary>
    public async Task<ApprovalDto> SubmitVoucherAsync(Guid? id, VoucherInput input, string? note, CancellationToken cancellationToken = default)
    {
        await access.RequireApprovalSwitchedOnAsync(cancellationToken);
        var draft = await vouchers.SaveDraftAsync(id, input, cancellationToken);
        return await SubmitAsync(ApprovalSubject.Voucher, draft.Id, note, cancellationToken);
    }

    /// <summary>Sends a draft voucher that is already saved.</summary>
    public async Task<ApprovalDto> SubmitSavedVoucherAsync(Guid id, string? note, CancellationToken cancellationToken = default)
    {
        await access.RequireApprovalSwitchedOnAsync(cancellationToken);
        var voucher = await vouchers.GetAsync(id, cancellationToken) ?? throw new NotFoundException("voucher");
        if (voucher.Status != Domain.Accounting.VoucherStatus.Draft)
            throw Refused("voucher", "approval.not-draft");
        return await SubmitAsync(ApprovalSubject.Voucher, id, note, cancellationToken);
    }

    /// <summary>Saves a document as a draft and sends it for approval.</summary>
    public async Task<ApprovalDto> SubmitDocumentAsync(Guid? id, DocumentInput input, string? note, CancellationToken cancellationToken = default)
    {
        await access.RequireApprovalSwitchedOnAsync(cancellationToken);
        if (!input.Kind.Posts())
            throw Refused("document", "approval.not-needed"); // quotes, orders and delivery notes post nothing
        var draft = await documents.SaveDraftAsync(id, input, cancellationToken);
        return await SubmitAsync(ApprovalSubject.Document, draft.Id, note, cancellationToken);
    }

    public async Task<ApprovalDto> SubmitSavedDocumentAsync(Guid id, string? note, CancellationToken cancellationToken = default)
    {
        await access.RequireApprovalSwitchedOnAsync(cancellationToken);
        var document = await documents.GetAsync(id, cancellationToken) ?? throw new NotFoundException("document");
        if (document.Status != DocumentStatus.Draft)
            throw Refused("document", "approval.not-draft");
        if (!document.Kind.Posts())
            throw Refused("document", "approval.not-needed");
        return await SubmitAsync(ApprovalSubject.Document, id, note, cancellationToken);
    }

    private async Task<ApprovalDto> SubmitAsync(ApprovalSubject subject, Guid subjectId, string? note, CancellationToken cancellationToken)
    {
        var company = CompanyOrThrow();
        await store.RemoveOpenApprovalsAsync(subject, subjectId, cancellationToken); // a new request replaces an earlier one (waiting or rejected)
        var request = new ApprovalRequest
        {
            CompanyId = company.Id,
            Subject = subject,
            SubjectId = subjectId,
            State = ApprovalState.Pending,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            SubmittedBy = currentUser.UserId,
            SubmittedAt = clock.GetUtcNow().UtcDateTime,
        };
        await store.AddApprovalAsync(request, cancellationToken);
        return (await DescribeAsync(request, cancellationToken))!;
    }

    /// <summary>
    /// What waits for the signed-in person to decide, and what they sent themselves that was decided or still waits.
    /// Someone who may approve sees everything pending in the areas they may approve.
    /// </summary>
    public async Task<IReadOnlyList<ApprovalDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ApprovalDto>();
        foreach (var request in (await store.ListApprovalsAsync(cancellationToken)).OrderByDescending(r => r.SubmittedAt))
        {
            var mine = string.Equals(request.SubmittedBy, currentUser.UserId, StringComparison.OrdinalIgnoreCase);
            var mayDecide = access.Can(AreaOf(request.Subject), PermissionAction.Approve);
            // Decided requests stay on the list for the person who sent them (so a rejection is seen), pending ones for those who may decide.
            if (!(mine || mayDecide && request.State == ApprovalState.Pending))
                continue;
            if (await DescribeAsync(request, cancellationToken) is { } dto)
                result.Add(dto);
        }

        return result;
    }

    public async Task<ApprovalDto> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await store.FindApprovalAsync(id, cancellationToken) ?? throw new NotFoundException("approval");
        if (request.State != ApprovalState.Pending)
            throw Refused("approval", "approval.not-pending");
        access.Require(AreaOf(request.Subject), PermissionAction.Approve);
        if (await DescribeAsync(request, cancellationToken) is null)
            throw Refused("approval", "approval.not-pending"); // it was posted another way or deleted meanwhile

        // Posting is the approver's act: the voucher or document is posted as saved, by someone who is allowed to.
        if (request.Subject == ApprovalSubject.Voucher)
            await vouchers.PostAsync(request.SubjectId, cancellationToken);
        else
            await documents.IssueSavedAsync(request.SubjectId, cancellationToken);

        request.State = ApprovalState.Approved;
        request.DecidedBy = currentUser.UserId;
        request.DecidedAt = clock.GetUtcNow().UtcDateTime;
        await store.UpdateApprovalAsync(request, cancellationToken);
        return (await DescribeAsync(request, cancellationToken))!;
    }

    public async Task<ApprovalDto> RejectAsync(Guid id, string? note, CancellationToken cancellationToken = default)
    {
        var request = await store.FindApprovalAsync(id, cancellationToken) ?? throw new NotFoundException("approval");
        if (request.State != ApprovalState.Pending)
            throw Refused("approval", "approval.not-pending");
        access.Require(AreaOf(request.Subject), PermissionAction.Approve);

        request.State = ApprovalState.Rejected;
        request.DecidedBy = currentUser.UserId;
        request.DecidedAt = clock.GetUtcNow().UtcDateTime;
        request.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await store.UpdateApprovalAsync(request, cancellationToken);
        return (await DescribeAsync(request, cancellationToken))!;
    }

    private async Task<ApprovalDto?> DescribeAsync(ApprovalRequest r, CancellationToken cancellationToken)
    {
        var decide = r.State == ApprovalState.Pending && access.Can(AreaOf(r.Subject), PermissionAction.Approve);
        if (r.Subject == ApprovalSubject.Voucher)
        {
            var v = await vouchers.GetAsync(r.SubjectId, cancellationToken);
            if (v is null || r.State == ApprovalState.Pending && v.Status != Domain.Accounting.VoucherStatus.Draft)
                return null; // gone, or posted another way: nothing is waiting any more
            return new ApprovalDto(r.Id, r.Subject, r.SubjectId, r.State, v.Kind.ToString(), v.Number, v.Date, v.Total, v.CurrencyCode, r.Note, r.SubmittedBy, r.SubmittedAt, r.DecidedBy, r.DecidedAt, r.DecisionNote, decide);
        }

        var d = await documents.GetAsync(r.SubjectId, cancellationToken);
        if (d is null || r.State == ApprovalState.Pending && d.Status != DocumentStatus.Draft)
            return null;
        return new ApprovalDto(r.Id, r.Subject, r.SubjectId, r.State, d.Kind.ToString(), d.Number, d.Date, d.Total, d.CurrencyCode, r.Note, r.SubmittedBy, r.SubmittedAt, r.DecidedBy, r.DecidedAt, r.DecisionNote, decide);
    }

    private static ValidationException Refused(string field, string code) => new([new ValidationIssue(field, code)]);
}
