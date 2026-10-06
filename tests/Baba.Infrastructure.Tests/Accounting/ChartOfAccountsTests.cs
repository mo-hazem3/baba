using Baba.Application;
using Baba.Application.Accounting;
using Baba.Domain;
using Baba.Domain.Accounting;

namespace Baba.Infrastructure.Tests.Accounting;

public class ChartOfAccountsTests : AccountingFixture
{
    [Fact]
    public async Task A_new_company_starts_with_a_usable_chart_with_cash_receivable_payable_and_retained_earnings_roles()
    {
        var e = await NewEnvAsync();
        var accounts = await e.Chart.ListAsync();

        Assert.Equal(AccountRole.CashOrBank, e.ByCode["111"].Role);
        Assert.Equal(AccountRole.CashOrBank, e.ByCode["112"].Role);
        Assert.Equal(AccountRole.Receivable, e.ByCode["113"].Role);
        Assert.Equal(AccountRole.Payable, e.ByCode["211"].Role);
        Assert.Equal(AccountRole.RetainedEarnings, e.ByCode["34"].Role);
        Assert.All(accounts, a => Assert.False(a.HasEntries));
        Assert.Equal(accounts.Select(a => a.Code).OrderBy(c => c, StringComparer.OrdinalIgnoreCase), accounts.Select(a => a.Code));

        // The tree is intact: every parent exists and is a group of the same type.
        var byId = accounts.ToDictionary(a => a.Id);
        foreach (var account in accounts.Where(a => a.ParentId is not null))
        {
            var parent = byId[account.ParentId!.Value];
            Assert.False(parent.IsPosting);
            Assert.Equal(parent.Type, account.Type);
        }
    }

    [Fact]
    public async Task Accounts_can_be_added_edited_moved_and_deactivated()
    {
        var e = await NewEnvAsync();

        var added = await e.Chart.CreateAsync(new AccountInput("4281", "", "Training", e.Id("42"), AccountType.Expense, true, AccountRole.None));
        Assert.Equal("Training", added.NameAr); // one name is used for both languages
        Assert.False(added.HasEntries);

        var renamed = await e.Chart.UpdateAsync(added.Id, new AccountInput("4281", "التدريب", "Staff training", e.Id("42"), AccountType.Expense, true, AccountRole.None));
        Assert.Equal(("Staff training", "التدريب"), (renamed.NameEn, renamed.NameAr));

        // Move it under another group of the same type (a new group first).
        var group = await e.Chart.CreateAsync(new AccountInput("43", "مصروفات الموظفين", "Staff costs", e.Id("4"), AccountType.Expense, false, AccountRole.None));
        var moved = await e.Chart.UpdateAsync(added.Id, new AccountInput("4301", "التدريب", "Staff training", group.Id, AccountType.Expense, true, AccountRole.None));
        Assert.Equal(group.Id, moved.ParentId);

        var inactive = await e.Chart.SetActiveAsync(added.Id, false);
        Assert.False(inactive.IsActive);
        Assert.False((await e.Chart.ListAsync()).Single(a => a.Id == added.Id).IsActive);
    }

    [Fact]
    public async Task The_chart_rules_are_enforced_with_translatable_codes()
    {
        var e = await NewEnvAsync();

        var duplicate = await RefusedAsync(() => e.Chart.CreateAsync(new AccountInput("422", "x", "x", e.Id("42"), AccountType.Expense, true, AccountRole.None)));
        Assert.Contains("account.code-duplicate", Codes(duplicate));

        var underPosting = await RefusedAsync(() => e.Chart.CreateAsync(new AccountInput("4221", "x", "x", e.Id("422"), AccountType.Expense, true, AccountRole.None)));
        Assert.Contains("account.parent-must-be-group", Codes(underPosting));

        var wrongType = await RefusedAsync(() => e.Chart.CreateAsync(new AccountInput("4299", "x", "x", e.Id("42"), AccountType.Revenue, true, AccountRole.None)));
        Assert.Contains("account.type-differs-from-parent", Codes(wrongType));

        // A group cannot move inside its own descendant.
        var cycle = await RefusedAsync(() => e.Chart.UpdateAsync(e.Id("1"), new AccountInput("1", "الأصول", "Assets", e.Id("11"), AccountType.Asset, false, AccountRole.None)));
        Assert.Contains("account.parent-cycle", Codes(cycle));

        var role = await RefusedAsync(() => e.Chart.CreateAsync(new AccountInput("4298", "x", "x", e.Id("42"), AccountType.Expense, true, AccountRole.CashOrBank)));
        Assert.Contains("account.role-wrong-type", Codes(role));
    }

    [Fact]
    public async Task An_account_with_entries_or_children_cannot_be_deleted_and_the_reason_is_clear()
    {
        var e = await NewEnvAsync();
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 100m)));

        var withChildren = await RefusedAsync(() => e.Chart.DeleteAsync(e.Id("42")));
        Assert.Contains("account.has-children", Codes(withChildren));

        var withEntries = await RefusedAsync(() => e.Chart.DeleteAsync(e.Id("422")));
        Assert.Contains("account.has-entries", Codes(withEntries));
        Assert.True((await e.Chart.ListAsync()).Single(a => a.Code == "422").HasEntries);
        Assert.True((await e.Chart.ListAsync()).Single(a => a.Code == "111").HasEntries); // the bank side counts too

        // An unused account can go.
        await e.Chart.DeleteAsync(e.Id("424"));
        Assert.DoesNotContain(await e.Chart.ListAsync(), a => a.Code == "424");
        await Assert.ThrowsAsync<NotFoundException>(() => e.Chart.DeleteAsync(e.Id("424")));
    }

    [Fact]
    public async Task Entries_can_be_moved_to_another_account_of_the_same_type_and_then_the_old_one_deleted()
    {
        var e = await NewEnvAsync();
        var draft = await e.Vouchers.SaveDraftAsync(null, Payment(e, Oct6, ("424", 20m)));
        var posted = await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("424", 80m), ("423", 5m)));
        Assert.Equal(80m, await BalanceAsync(e, "424"));

        var touched = await e.Chart.MoveEntriesAsync(e.Id("424"), e.Id("427"));

        Assert.Equal(2, touched);
        Assert.Equal(0m, await BalanceAsync(e, "424"));
        Assert.Equal(80m, await BalanceAsync(e, "427"));
        Assert.Equal(5m, await BalanceAsync(e, "423")); // untouched
        Assert.All((await e.Vouchers.GetAsync(posted.Id))!.Lines.Where(l => l.Debit == 80m), l => Assert.Equal(e.Id("427"), l.AccountId));
        Assert.Equal(e.Id("427"), (await e.Vouchers.GetAsync(draft.Id))!.Lines.Single().AccountId);

        await e.Chart.DeleteAsync(e.Id("424")); // now unused
    }

    [Fact]
    public async Task Moving_entries_checks_the_target()
    {
        var e = await NewEnvAsync();
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("424", 10m)));

        Assert.Contains("move.same-account", Codes(await RefusedAsync(() => e.Chart.MoveEntriesAsync(e.Id("424"), e.Id("424")))));
        Assert.Contains("move.target-not-usable", Codes(await RefusedAsync(() => e.Chart.MoveEntriesAsync(e.Id("424"), e.Id("42")))));
        Assert.Contains("move.type-differs", Codes(await RefusedAsync(() => e.Chart.MoveEntriesAsync(e.Id("424"), e.Id("512")))));
        Assert.Contains("move.target-unknown", Codes(await RefusedAsync(() => e.Chart.MoveEntriesAsync(e.Id("424"), Guid.NewGuid()))));
        Assert.Contains("move.target-not-cash", Codes(await RefusedAsync(() => e.Chart.MoveEntriesAsync(e.Id("111"), e.Id("113"))))); // asset, but not a bank/cash account
    }

    [Fact]
    public async Task Changes_to_the_chart_are_written_to_the_audit_log()
    {
        var e = await NewEnvAsync("amal");
        var added = await e.Chart.CreateAsync(new AccountInput("4281", "التدريب", "Training", e.Id("42"), AccountType.Expense, true, AccountRole.None));
        await e.Chart.UpdateAsync(added.Id, new AccountInput("4281", "التدريب", "Staff training", e.Id("42"), AccountType.Expense, true, AccountRole.None));

        using var context = e.Files.Create();
        var log = context.AuditLog.Where(l => l.EntityId == added.Id).OrderBy(l => l.At).ThenBy(l => l.Action).ToList();

        Assert.Contains(log, l => l.Action == AuditAction.Created && l.UserId == "amal");
        var updated = Assert.Single(log, l => l.Action == AuditAction.Updated);
        Assert.Equal("{\"NameEn\":\"Training\"}", updated.BeforeJson);
        Assert.Equal("{\"NameEn\":\"Staff training\"}", updated.AfterJson);
    }
}
