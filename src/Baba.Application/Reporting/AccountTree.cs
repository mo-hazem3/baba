using Baba.Domain;

namespace Baba.Application.Reporting;

/// <summary>The chart of accounts as a tree in code order, with sums rolled up from the postable accounts to their groups.</summary>
internal sealed class AccountTree
{
    private readonly Dictionary<Guid, Account> _byId;
    private readonly ILookup<Guid, Account> _children;

    public AccountTree(IEnumerable<Account> accounts)
    {
        var all = accounts.ToList();
        _byId = all.ToDictionary(a => a.Id);
        _children = all.Where(a => a.ParentId is { } p && _byId.ContainsKey(p))
            .OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase)
            .ToLookup(a => a.ParentId!.Value);

        // Accounts with no (known) parent are the top of the tree.
        Roots = all.Where(a => a.ParentId is not { } p || !_byId.ContainsKey(p))
            .OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<Account> Roots { get; }

    public IReadOnlyList<Account> ChildrenOf(Guid id) => _children[id].ToList();

    public Account? Find(Guid id) => _byId.GetValueOrDefault(id);

    /// <summary>The value of a group is the total of everything below it; a postable account is its own value.</summary>
    public Dictionary<Guid, T> Rollup<T>(Func<Account, T> own, Func<T, T, T> add, T zero)
    {
        var result = new Dictionary<Guid, T>();

        T Visit(Account account)
        {
            var total = account.IsPosting ? own(account) : zero;
            foreach (var child in _children[account.Id])
                total = add(total, Visit(child));
            result[account.Id] = total;
            return total;
        }

        foreach (var root in Roots)
            Visit(root);
        return result;
    }

    /// <summary>Walks the tree in code order, children after their parent, with each account's depth.</summary>
    public IEnumerable<(Account Account, int Level)> Walk(IEnumerable<Account> from)
    {
        foreach (var account in from.OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase))
        {
            yield return (account, Depth(account));
            foreach (var child in Walk(_children[account.Id]))
                yield return child;
        }
    }

    public int Depth(Account account)
    {
        var depth = 0;
        for (var parent = account.ParentId; parent is { } id && _byId.TryGetValue(id, out var p); parent = p.ParentId)
            depth++;
        return depth;
    }
}
