namespace Baba.Domain.Accounting;

/// <summary>
/// Rules for the chart of accounts tree (unlimited depth). Pure functions over the accounts, so the same rules apply on
/// create, edit, move and delete.
/// </summary>
public static class ChartRules
{
    /// <summary>Checks an account that is being added or changed against the rest of the chart.</summary>
    /// <param name="account">The account as it will be saved.</param>
    /// <param name="all">Every account of the company, including the one being changed (matched by id).</param>
    /// <param name="hasEntries">Whether the account already has voucher lines or ledger entries.</param>
    public static IReadOnlyList<PostingIssue> Validate(Account account, IReadOnlyCollection<Account> all, bool hasEntries)
    {
        var issues = new List<PostingIssue>();
        var others = all.Where(a => a.Id != account.Id).ToList();
        var existing = all.FirstOrDefault(a => a.Id == account.Id);

        var code = account.Code?.Trim() ?? "";
        if (code.Length == 0)
            issues.Add(new("code", "account.code-required"));
        else if (others.Any(a => string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("code", "account.code-duplicate"));

        if (string.IsNullOrWhiteSpace(account.NameAr) && string.IsNullOrWhiteSpace(account.NameEn))
            issues.Add(new("name", "account.name-required"));

        if (account.ParentId is { } parentId)
        {
            var parent = others.FirstOrDefault(a => a.Id == parentId);
            if (parent is null)
                issues.Add(new("parent", "account.parent-unknown"));
            else if (parent.IsPosting)
                issues.Add(new("parent", "account.parent-must-be-group"));
            else if (parent.Type != account.Type)
                issues.Add(new("type", "account.type-differs-from-parent"));
            else if (WouldCreateCycle(account.Id, parentId, all))
                issues.Add(new("parent", "account.parent-cycle"));
        }

        var children = others.Where(a => a.ParentId == account.Id).ToList();
        if (account.IsPosting && children.Count > 0)
            issues.Add(new("isPosting", "account.posting-has-children"));
        if (!account.IsPosting && hasEntries)
            issues.Add(new("isPosting", "account.group-has-entries"));

        // Children follow their parent's type, so the type of a group cannot change under them.
        if (existing is not null && existing.Type != account.Type)
        {
            if (children.Count > 0)
                issues.Add(new("type", "account.type-has-children"));
            else if (hasEntries)
                issues.Add(new("type", "account.type-has-entries"));
        }

        if (account.Role != AccountRole.None)
        {
            var typeFits = account.Role switch
            {
                AccountRole.CashOrBank or AccountRole.Receivable => account.Type == AccountType.Asset,
                AccountRole.Payable or AccountRole.TaxPayable => account.Type == AccountType.Liability,
                AccountRole.TaxReceivable or AccountRole.Inventory or AccountRole.AccumulatedDepreciation => account.Type == AccountType.Asset,
                AccountRole.CostOfSales or AccountRole.DepreciationExpense or AccountRole.SalaryExpense or AccountRole.SocialInsuranceExpense or AccountRole.EndOfServiceExpense => account.Type == AccountType.Expense,
                AccountRole.SalariesPayable or AccountRole.SocialInsurancePayable or AccountRole.EndOfServiceProvision or AccountRole.ExpenseClaimsPayable => account.Type == AccountType.Liability,
                AccountRole.InventoryAdjustment or AccountRole.AssetDisposal => account.Type is AccountType.Expense or AccountType.Revenue,
                AccountRole.RetainedEarnings => account.Type == AccountType.Equity,
                AccountRole.ExchangeDifference => account.Type is AccountType.Revenue or AccountType.Expense,
                _ => true,
            };
            if (!typeFits)
                issues.Add(new("role", "account.role-wrong-type"));
            else if (!account.IsPosting)
                issues.Add(new("role", "account.role-needs-posting"));
        }

        return issues;
    }

    /// <summary>True if making <paramref name="newParentId"/> the parent of <paramref name="accountId"/> would put an account inside itself.</summary>
    public static bool WouldCreateCycle(Guid accountId, Guid newParentId, IReadOnlyCollection<Account> all)
    {
        var byId = all.ToDictionary(a => a.Id);
        for (Guid? current = newParentId; current is { } id; current = byId.GetValueOrDefault(id)?.ParentId)
        {
            if (id == accountId)
                return true;
        }

        return false;
    }

    /// <summary>The account and every account below it.</summary>
    public static IReadOnlyList<Account> WithDescendants(Guid accountId, IReadOnlyCollection<Account> all)
    {
        var result = new List<Account>();
        var childrenOf = all.Where(a => a.ParentId is not null).ToLookup(a => a.ParentId!.Value);
        var root = all.FirstOrDefault(a => a.Id == accountId);
        if (root is null)
            return result;

        var queue = new Queue<Account>([root]);
        while (queue.Count > 0)
        {
            var next = queue.Dequeue();
            result.Add(next);
            foreach (var child in childrenOf[next.Id])
                queue.Enqueue(child);
        }

        return result;
    }
}
