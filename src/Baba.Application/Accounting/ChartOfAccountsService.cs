using Baba.Domain;
using Baba.Domain.Accounting;

namespace Baba.Application.Accounting;

public sealed record AccountDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    Guid? ParentId,
    AccountType Type,
    bool IsPosting,
    bool IsActive,
    AccountRole Role,
    bool HasEntries);

public sealed record AccountInput(
    string Code,
    string NameAr,
    string NameEn,
    Guid? ParentId,
    AccountType Type,
    bool IsPosting,
    AccountRole Role);

/// <summary>
/// The chart of accounts (brief section 10.1): an unlimited-depth tree. Accounts can be added, edited, moved, deactivated and
/// deleted. Deleting is blocked while an account has entries, and the app offers to move them instead.
/// </summary>
public sealed class ChartOfAccountsService(IAccountStore accounts)
{
    public async Task<IReadOnlyList<AccountDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var all = await accounts.ListAsync(cancellationToken);
        var withEntries = await accounts.AccountIdsWithEntriesAsync(cancellationToken);
        return all.OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).Select(a => ToDto(a, withEntries.Contains(a.Id))).ToList();
    }

    public async Task<AccountDto> CreateAsync(AccountInput input, CancellationToken cancellationToken = default)
    {
        var all = await accounts.ListAsync(cancellationToken);
        var account = new Account();
        Apply(account, input);

        Throw(ChartRules.Validate(account, all, hasEntries: false));
        await accounts.AddAsync(account, cancellationToken);
        return ToDto(account, hasEntries: false);
    }

    /// <summary>Changes an account. Changing <see cref="AccountInput.ParentId"/> moves it (with everything below it) in the tree.</summary>
    public async Task<AccountDto> UpdateAsync(Guid id, AccountInput input, CancellationToken cancellationToken = default)
    {
        var all = await accounts.ListAsync(cancellationToken);
        var account = all.FirstOrDefault(a => a.Id == id) ?? throw NotFound();
        var hasEntries = (await accounts.AccountIdsWithEntriesAsync(cancellationToken)).Contains(id);

        var changed = new Account { Id = account.Id, CompanyId = account.CompanyId, IsActive = account.IsActive };
        Apply(changed, input);
        Throw(ChartRules.Validate(changed, all, hasEntries));

        CopyEditable(changed, account);
        await accounts.UpdateAsync(account, cancellationToken);
        return ToDto(account, hasEntries);
    }

    /// <summary>An inactive account stays in reports but cannot be posted to (and a bank account cannot be used by new vouchers).</summary>
    public async Task<AccountDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var account = (await accounts.ListAsync(cancellationToken)).FirstOrDefault(a => a.Id == id) ?? throw NotFound();
        account.IsActive = active;
        await accounts.UpdateAsync(account, cancellationToken);
        return ToDto(account, (await accounts.AccountIdsWithEntriesAsync(cancellationToken)).Contains(id));
    }

    /// <summary>Deletes an account that has no entries and no children. Otherwise says why (the screen then offers to move the entries).</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var all = await accounts.ListAsync(cancellationToken);
        if (all.All(a => a.Id != id))
            throw NotFound();

        var issues = new List<PostingIssue>();
        if (all.Any(a => a.ParentId == id))
            issues.Add(new("account", "account.has-children"));
        if ((await accounts.AccountIdsWithEntriesAsync(cancellationToken)).Contains(id))
            issues.Add(new("account", "account.has-entries"));
        Throw(issues);

        await accounts.DeleteAsync(id, cancellationToken);
    }

    /// <summary>Moves all entries of one posting account to another of the same type, which then lets the first one be deleted.</summary>
    public async Task<int> MoveEntriesAsync(Guid fromId, Guid toId, CancellationToken cancellationToken = default)
    {
        var all = await accounts.ListAsync(cancellationToken);
        var from = all.FirstOrDefault(a => a.Id == fromId) ?? throw NotFound();
        var to = all.FirstOrDefault(a => a.Id == toId);

        var issues = new List<PostingIssue>();
        if (to is null)
            issues.Add(new("target", "move.target-unknown"));
        else if (to.Id == from.Id)
            issues.Add(new("target", "move.same-account"));
        else if (!to.IsPosting || !to.IsActive)
            issues.Add(new("target", "move.target-not-usable"));
        else if (to.Type != from.Type)
            issues.Add(new("target", "move.type-differs"));
        else if (from.Role == AccountRole.CashOrBank && to.Role != AccountRole.CashOrBank)
            issues.Add(new("target", "move.target-not-cash")); // vouchers pay from and receive into these
        else if (await accounts.HasReconciledEntriesAsync(fromId, cancellationToken))
            issues.Add(new("target", "move.has-reconciled")); // the bank statement was checked against these entries: undo that first
        Throw(issues);

        return await accounts.MoveEntriesAsync(fromId, toId, cancellationToken);
    }

    private static void Apply(Account account, AccountInput input)
    {
        account.Code = input.Code?.Trim() ?? "";
        account.NameAr = input.NameAr?.Trim() ?? "";
        account.NameEn = input.NameEn?.Trim() ?? "";
        account.ParentId = input.ParentId;
        account.Type = input.Type;
        account.IsPosting = input.IsPosting;
        account.Role = input.Role;

        // A name in one language is used for both, so lists and search always have something to show.
        if (account.NameAr.Length == 0) account.NameAr = account.NameEn;
        if (account.NameEn.Length == 0) account.NameEn = account.NameAr;
    }

    private static void CopyEditable(Account from, Account to)
    {
        to.Code = from.Code;
        to.NameAr = from.NameAr;
        to.NameEn = from.NameEn;
        to.ParentId = from.ParentId;
        to.Type = from.Type;
        to.IsPosting = from.IsPosting;
        to.Role = from.Role;
    }

    private static AccountDto ToDto(Account a, bool hasEntries) =>
        new(a.Id, a.Code, a.NameAr, a.NameEn, a.ParentId, a.Type, a.IsPosting, a.IsActive, a.Role, hasEntries);

    private static void Throw(IReadOnlyCollection<PostingIssue> issues)
    {
        if (issues.Count > 0)
            throw new ValidationException(issues.Select(i => new ValidationIssue(i.Field, i.Code)).ToList());
    }

    private static NotFoundException NotFound() => new("account");
}
