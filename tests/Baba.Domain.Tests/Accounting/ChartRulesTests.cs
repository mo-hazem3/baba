using Baba.Domain.Accounting;

namespace Baba.Domain.Tests.Accounting;

public class ChartRulesTests
{
    private static Account Group(string code, AccountType type, Guid? parent = null) =>
        new() { Code = code, NameEn = code, Type = type, IsPosting = false, ParentId = parent };

    private static Account Leaf(string code, AccountType type, Guid? parent = null, AccountRole role = AccountRole.None) =>
        new() { Code = code, NameEn = code, Type = type, IsPosting = true, ParentId = parent, Role = role };

    private static IEnumerable<string> Codes(IEnumerable<PostingIssue> issues) => issues.Select(i => i.Code);

    [Fact]
    public void A_valid_new_account_under_a_group_of_the_same_type_has_no_problems()
    {
        var assets = Group("1", AccountType.Asset);
        var cash = Leaf("11", AccountType.Asset, assets.Id);

        Assert.Empty(ChartRules.Validate(cash, [assets], hasEntries: false));
    }

    [Fact]
    public void A_code_is_required_and_must_be_unique_ignoring_case_and_spaces_around_it()
    {
        var existing = Leaf("A10", AccountType.Asset);

        Assert.Contains("account.code-required", Codes(ChartRules.Validate(Leaf("  ", AccountType.Asset), [existing], false)));
        Assert.Contains("account.code-duplicate", Codes(ChartRules.Validate(Leaf("a10", AccountType.Asset), [existing], false)));
        Assert.Contains("account.code-duplicate", Codes(ChartRules.Validate(Leaf(" A10 ", AccountType.Asset), [existing], false)));
        Assert.Empty(ChartRules.Validate(existing, [existing], false)); // the account itself does not clash with itself
    }

    [Fact]
    public void At_least_one_name_is_required_in_either_language()
    {
        var noName = new Account { Code = "1", Type = AccountType.Asset, IsPosting = true };
        var arabicOnly = new Account { Code = "2", NameAr = "الصندوق", Type = AccountType.Asset, IsPosting = true };

        Assert.Contains("account.name-required", Codes(ChartRules.Validate(noName, [], false)));
        Assert.Empty(ChartRules.Validate(arabicOnly, [], false));
    }

    [Fact]
    public void A_parent_must_exist_be_a_group_and_have_the_same_type()
    {
        var group = Group("1", AccountType.Asset);
        var posting = Leaf("11", AccountType.Asset, group.Id);

        Assert.Contains("account.parent-unknown", Codes(ChartRules.Validate(Leaf("12", AccountType.Asset, Guid.NewGuid()), [group], false)));
        Assert.Contains("account.parent-must-be-group", Codes(ChartRules.Validate(Leaf("111", AccountType.Asset, posting.Id), [group, posting], false)));
        Assert.Contains("account.type-differs-from-parent", Codes(ChartRules.Validate(Leaf("12", AccountType.Expense, group.Id), [group], false)));
    }

    [Fact]
    public void Moving_an_account_under_its_own_descendant_is_refused()
    {
        var root = Group("1", AccountType.Asset);
        var middle = Group("11", AccountType.Asset, root.Id);
        var bottom = Group("111", AccountType.Asset, middle.Id);
        var all = new[] { root, middle, bottom };

        Assert.True(ChartRules.WouldCreateCycle(root.Id, bottom.Id, all));
        Assert.True(ChartRules.WouldCreateCycle(root.Id, root.Id, all));
        Assert.False(ChartRules.WouldCreateCycle(bottom.Id, root.Id, all));

        middle.ParentId = bottom.Id; // the move being validated
        Assert.Contains("account.parent-cycle", Codes(ChartRules.Validate(middle, all, false)));
    }

    [Fact]
    public void A_posting_account_cannot_have_children_and_a_group_cannot_have_entries()
    {
        var group = Group("1", AccountType.Asset);
        var child = Leaf("11", AccountType.Asset, group.Id);

        group.IsPosting = true;
        Assert.Contains("account.posting-has-children", Codes(ChartRules.Validate(group, [group, child], false)));

        var emptyGroup = Group("2", AccountType.Asset);
        Assert.Contains("account.group-has-entries", Codes(ChartRules.Validate(emptyGroup, [emptyGroup], hasEntries: true)));
    }

    [Fact]
    public void The_type_of_an_account_cannot_change_under_children_or_over_existing_entries()
    {
        var stored = Group("1", AccountType.Asset);
        var child = Leaf("11", AccountType.Asset, stored.Id);
        var changed = Group("1", AccountType.Expense);
        changed.Id = stored.Id;

        Assert.Contains("account.type-has-children", Codes(ChartRules.Validate(changed, [stored, child], false)));

        var leaf = Leaf("5", AccountType.Revenue);
        var leafChanged = Leaf("5", AccountType.Expense);
        leafChanged.Id = leaf.Id;
        Assert.Contains("account.type-has-entries", Codes(ChartRules.Validate(leafChanged, [leaf], hasEntries: true)));
        Assert.Empty(ChartRules.Validate(leafChanged, [leaf], hasEntries: false));
    }

    [Theory]
    [InlineData(AccountRole.CashOrBank, AccountType.Asset, true, false)]
    [InlineData(AccountRole.Receivable, AccountType.Asset, true, false)]
    [InlineData(AccountRole.Payable, AccountType.Liability, true, false)]
    [InlineData(AccountRole.RetainedEarnings, AccountType.Equity, true, false)]
    [InlineData(AccountRole.CashOrBank, AccountType.Expense, true, true)]
    [InlineData(AccountRole.Payable, AccountType.Asset, true, true)]
    [InlineData(AccountRole.CashOrBank, AccountType.Asset, false, true)]
    public void A_special_role_needs_the_right_type_and_a_posting_account(AccountRole role, AccountType type, bool posting, bool refused)
    {
        var account = new Account { Code = "9", NameEn = "x", Type = type, IsPosting = posting, Role = role };

        var codes = Codes(ChartRules.Validate(account, [], false)).ToList();

        Assert.Equal(refused, codes.Any(c => c is "account.role-wrong-type" or "account.role-needs-posting"));
    }

    [Fact]
    public void WithDescendants_returns_the_account_and_everything_below_it()
    {
        var root = Group("1", AccountType.Asset);
        var current = Group("11", AccountType.Asset, root.Id);
        var cash = Leaf("111", AccountType.Asset, current.Id);
        var fixedAssets = Group("12", AccountType.Asset, root.Id);
        var other = Group("2", AccountType.Liability);
        var all = new[] { root, current, cash, fixedAssets, other };

        Assert.Equal(["1", "11", "111", "12"], ChartRules.WithDescendants(root.Id, all).Select(a => a.Code).OrderBy(c => c));
        Assert.Equal(["11", "111"], ChartRules.WithDescendants(current.Id, all).Select(a => a.Code).OrderBy(c => c));
        Assert.Equal(["111"], ChartRules.WithDescendants(cash.Id, all).Select(a => a.Code));
        Assert.Empty(ChartRules.WithDescendants(Guid.NewGuid(), all));
    }

    [Theory]
    [InlineData(AccountType.Asset, true)]
    [InlineData(AccountType.Expense, true)]
    [InlineData(AccountType.Liability, false)]
    [InlineData(AccountType.Equity, false)]
    [InlineData(AccountType.Revenue, false)]
    public void Assets_and_expenses_grow_with_debits_the_rest_with_credits(AccountType type, bool debitNormal) =>
        Assert.Equal(debitNormal, new Account { Type = type }.IsDebitNormal);
}
