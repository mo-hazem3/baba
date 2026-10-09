using Baba.Application;
using Baba.Application.Accounting;
using Baba.Application.Security;
using Baba.Application.Trade;
using Baba.Domain.Accounting;
using Baba.Domain.Security;
using Baba.Domain.Trade;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Posting and issuing that needs someone's approval (brief section 10.4).</summary>
public class ApprovalTests : AccountingFixture
{
    private const string Pw = "a long enough password";

    /// <summary>A company with an administrator, a clerk who may enter but not approve, and an accountant who may approve. The clerk is signed in.</summary>
    private async Task<Env> StartAsync(bool approvalRequired = true)
    {
        var e = await NewEnvAsync();
        await e.Access.TurnOnAccountsAsync("owner", "The Owner", Pw);
        var roles = await e.Users.ListRolesAsync();
        var clerk = await e.Users.CreateRoleAsync(new RoleInput("", "Clerk", ["accounting.View", "accounting.Edit", "trade.View", "trade.Edit"]));
        await e.Users.CreateUserAsync(new UserInput("clerk", "Clerk", clerk.Id, Pw));
        await e.Users.CreateUserAsync(new UserInput("boss", "Boss", roles.Single(r => r.NameEn == "Accountant").Id, Pw));
        if (approvalRequired)
            await e.Access.SetApprovalRequiredAsync(true);
        await SignInAsync(e, "clerk");
        return e;
    }

    private static async Task SignInAsync(Env e, string name)
    {
        e.Access.SignOut();
        await e.Access.SignInAsync(new SignInInput(name, Pw));
    }

    private static DocumentInput Sale(Env e, DocumentKind kind = DocumentKind.SalesInvoice) => new(
        kind, Oct6, null, e.Customer, null, null, null, null, 0, [new DocumentLineInput(null, null, e.Id("511"), "goods", 2, 100)]);

    [Fact]
    public async Task Without_the_approval_setting_everyone_with_the_rights_posts_directly()
    {
        var e = await StartAsync(approvalRequired: false);

        var posted = await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 50m)));

        Assert.Equal(VoucherStatus.Posted, posted.Status);
        Assert.Contains("approval.not-needed", Codes(await RefusedAsync(() => e.Approvals.SubmitVoucherAsync(null, Receipt(e, Oct6, ("511", 50m)), null))));
    }

    [Fact]
    public async Task Approval_cannot_be_switched_on_for_a_company_without_users()
    {
        var e = await NewEnvAsync();

        Assert.Contains("approval.needs-accounts", Codes(await RefusedAsync(() => e.Access.SetApprovalRequiredAsync(true))));
    }

    [Fact]
    public async Task A_clerk_cannot_post_but_can_send_a_voucher_and_the_approver_posts_it()
    {
        var e = await StartAsync();

        Assert.Contains("approval.required", Codes(await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 50m))))));
        var draft = await e.Vouchers.SaveDraftAsync(null, Receipt(e, Oct6, ("511", 50m)));
        Assert.Contains("approval.required", Codes(await RefusedAsync(() => e.Vouchers.PostAsync(draft.Id))));

        var sent = await e.Approvals.SubmitSavedVoucherAsync(draft.Id, "for the rent receipt");
        Assert.Equal(ApprovalState.Pending, sent.State);
        Assert.Equal("clerk", sent.SubmittedBy);
        Assert.False(sent.CanDecide);
        Assert.Single(await e.Approvals.ListAsync());
        Assert.Equal(0m, await BalanceAsync(e, "511"));
        await Assert.ThrowsAsync<ForbiddenException>(() => e.Approvals.ApproveAsync(sent.Id)); // a clerk cannot approve their own

        await SignInAsync(e, "boss");
        var waiting = Assert.Single(await e.Approvals.ListAsync());
        Assert.True(waiting.CanDecide);
        Assert.Equal("for the rent receipt", waiting.Note);

        var approved = await e.Approvals.ApproveAsync(sent.Id);

        Assert.Equal(ApprovalState.Approved, approved.State);
        Assert.Equal("boss", approved.DecidedBy);
        Assert.NotNull(approved.Number);
        Assert.Equal(VoucherStatus.Posted, (await e.Vouchers.GetAsync(draft.Id))!.Status);
        Assert.Equal(-50m, await BalanceAsync(e, "511"));
        Assert.Empty(await e.Approvals.ListAsync()); // nothing waits, and an approved request is the sender's to see

        await SignInAsync(e, "clerk");
        Assert.Equal(ApprovalState.Approved, Assert.Single(await e.Approvals.ListAsync()).State);
    }

    [Fact]
    public async Task A_rejected_voucher_stays_a_draft_and_the_clerk_sees_why_and_sends_it_again()
    {
        var e = await StartAsync();
        var sent = await e.Approvals.SubmitVoucherAsync(null, Receipt(e, Oct6, ("511", 50m)), null);

        await SignInAsync(e, "boss");
        var rejected = await e.Approvals.RejectAsync(sent.Id, "wrong customer");
        Assert.Equal(ApprovalState.Rejected, rejected.State);
        Assert.Contains("approval.not-pending", Codes(await RefusedAsync(() => e.Approvals.ApproveAsync(sent.Id))));

        await SignInAsync(e, "clerk");
        var seen = Assert.Single(await e.Approvals.ListAsync());
        Assert.Equal("wrong customer", seen.DecisionNote);
        Assert.Equal(VoucherStatus.Draft, (await e.Vouchers.GetAsync(sent.SubjectId))!.Status);

        var again = await e.Approvals.SubmitSavedVoucherAsync(sent.SubjectId, "fixed");
        Assert.Equal(ApprovalState.Pending, again.State);
        Assert.Equal(again.Id, Assert.Single(await e.Approvals.ListAsync()).Id); // the rejected one is replaced
    }

    [Fact]
    public async Task Changing_or_deleting_a_voucher_withdraws_what_was_sent_for_it()
    {
        var e = await StartAsync();
        var sent = await e.Approvals.SubmitVoucherAsync(null, Receipt(e, Oct6, ("511", 50m)), null);

        await e.Vouchers.SaveDraftAsync(sent.SubjectId, Receipt(e, Oct6, ("511", 60m))); // changed after sending: the approver must see the new figures
        Assert.Empty(await e.Approvals.ListAsync());

        var second = await e.Approvals.SubmitSavedVoucherAsync(sent.SubjectId, null);
        await e.Vouchers.DeleteAsync(sent.SubjectId);
        Assert.Empty(await e.Approvals.ListAsync());
        await SignInAsync(e, "boss");
        await Assert.ThrowsAsync<NotFoundException>(() => e.Approvals.ApproveAsync(second.Id));
    }

    [Fact]
    public async Task An_approver_posts_directly_and_the_program_own_runs_are_not_held_back()
    {
        var e = await StartAsync();
        await SignInAsync(e, "boss");

        Assert.Equal(VoucherStatus.Posted, (await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 50m)))).Status);

        await SignInAsync(e, "clerk");
        using (e.Session.AsSystem())
            Assert.Equal(VoucherStatus.Posted, (await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 10m)))).Status);
    }

    [Fact]
    public async Task A_clerk_cannot_undo_or_delete_a_posted_voucher_either()
    {
        var e = await StartAsync();
        await SignInAsync(e, "boss");
        var posted = await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 50m)));

        await SignInAsync(e, "clerk");

        Assert.Contains("approval.required", Codes(await RefusedAsync(() => e.Vouchers.SaveDraftAsync(posted.Id, Receipt(e, Oct6, ("511", 50m))))));
        Assert.Contains("approval.required", Codes(await RefusedAsync(() => e.Vouchers.DeleteAsync(posted.Id))));
        await e.Vouchers.DeleteAsync((await e.Vouchers.SaveDraftAsync(null, Receipt(e, Oct6, ("511", 5m)))).Id); // a draft is the clerk's own business
    }

    [Fact]
    public async Task An_invoice_is_issued_only_after_approval_but_a_quote_needs_none()
    {
        var e = await StartAsync();

        Assert.Contains("approval.required", Codes(await RefusedAsync(() => e.Trade.IssueAsync(null, Sale(e)))));
        Assert.Equal(DocumentStatus.Issued, (await e.Trade.IssueAsync(null, Sale(e, DocumentKind.Quote))).Status);
        Assert.Contains("approval.not-needed", Codes(await RefusedAsync(() => e.Approvals.SubmitDocumentAsync(null, Sale(e, DocumentKind.Quote), null))));

        var sent = await e.Approvals.SubmitDocumentAsync(null, Sale(e), "please issue");
        Assert.Equal(ApprovalSubject.Document, sent.Subject);
        Assert.Equal(DocumentStatus.Draft, (await e.Trade.GetAsync(sent.SubjectId))!.Status);
        Assert.Equal(0m, await BalanceAsync(e, "511"));

        await SignInAsync(e, "boss");
        var approved = await e.Approvals.ApproveAsync(sent.Id);

        Assert.Equal(ApprovalState.Approved, approved.State);
        var issued = (await e.Trade.GetAsync(sent.SubjectId))!;
        Assert.Equal(DocumentStatus.Issued, issued.Status);
        Assert.NotNull(issued.Number);
        Assert.Equal(-200m, await BalanceAsync(e, "511"));
    }
}
