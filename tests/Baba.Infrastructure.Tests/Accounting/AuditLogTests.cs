using Baba.Application.Security;
using Baba.Domain;
using Baba.Domain.Security;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>The audit log viewer: who changed what, in words, with filters, and no secrets (brief section 10.4).</summary>
public class AuditLogTests : AccountingFixture
{
    [Fact]
    public async Task A_voucher_that_is_added_changed_and_deleted_leaves_three_readable_entries()
    {
        var e = await NewEnvAsync();
        var posted = await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 50m)));
        await e.Vouchers.SaveAndPostAsync(posted.Id, Receipt(e, Oct6, ("511", 50m)) with { Memo = "corrected memo" });
        await e.Vouchers.DeleteAsync(posted.Id);

        var entries = (await e.Audit.ListAsync(new AuditSearch(Entity: "Voucher"))).Where(a => a.EntityId == posted.Id).ToList();

        Assert.Equal([AuditAction.Deleted, AuditAction.Updated, AuditAction.Created], entries.Select(a => a.Action)); // newest first
        Assert.All(entries, a => Assert.Equal("accountant", a.UserId));
        Assert.All(entries, a => Assert.Equal(("Voucher", "سند"), (a.EntityLabelEn, a.EntityLabelAr)));
        var updated = entries.Single(a => a.Action == AuditAction.Updated);
        var memo = updated.Changes.Single(c => c.Field == "Memo");
        Assert.Equal("receipt memo", memo.Before);
        Assert.Equal("corrected memo", memo.After);
        Assert.DoesNotContain(updated.Changes, c => c.Before == c.After); // unchanged fields are left out
        Assert.Equal(posted.Number, entries.Single(a => a.Action == AuditAction.Created).Reference);
    }

    [Fact]
    public async Task The_log_can_be_filtered_by_person_and_by_day_and_limited()
    {
        var e = await NewEnvAsync();
        await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 50m)));

        Assert.NotEmpty(await e.Audit.ListAsync(new AuditSearch(User: "ACCOUNTANT"))); // not case sensitive
        Assert.Empty(await e.Audit.ListAsync(new AuditSearch(User: "somebody else")));
        var today = DateOnly.FromDateTime(Clock.GetLocalNow().DateTime);
        Assert.NotEmpty(await e.Audit.ListAsync(new AuditSearch(From: today.AddDays(-1), To: today.AddDays(1))));
        Assert.Empty(await e.Audit.ListAsync(new AuditSearch(To: today.AddDays(-30))));
        Assert.Single(await e.Audit.ListAsync(new AuditSearch(Limit: 1)));
    }

    [Fact]
    public async Task Passwords_never_show_in_the_log_only_that_one_changed()
    {
        var e = await NewEnvAsync();
        var admin = await e.Users.TurnOnAccountsAsync("owner", "The Owner", "correct horse");
        await e.Users.ResetPasswordAsync(admin.Id, "another password");

        var entries = await e.Audit.ListAsync(new AuditSearch(Entity: "AppUser"));

        Assert.Equal(2, entries.Count);
        Assert.DoesNotContain(entries.SelectMany(a => a.Changes), c => c.Field.StartsWith("PasswordH") || c.Field == "PasswordSalt");
        Assert.Contains(entries.SelectMany(a => a.Changes), c => c.Field == "Password" && c.After == "changed");
        Assert.Equal("owner", entries.First().Reference);
    }

    [Fact]
    public async Task The_log_comes_as_a_table_for_export_in_both_languages()
    {
        var e = await NewEnvAsync();
        await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 50m)));

        var report = await e.Audit.ReportAsync(new AuditSearch(Entity: "Voucher"));

        Assert.Equal("audit-log", report.Key);
        Assert.All(report.Columns, c => Assert.False(string.IsNullOrEmpty(c.TitleAr)));
        var row = Assert.Single(report.Rows);
        Assert.Equal("Added", row.Cells[2].Text);
        Assert.Equal("إضافة", row.Cells[2].TextAr);
        Assert.Contains("Memo = receipt memo", row.Cells[5].Text);
    }
}
