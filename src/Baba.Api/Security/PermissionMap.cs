using Baba.Domain.Security;

namespace Baba.Api.Security;

/// <summary>What a request needs before it is let through.</summary>
public abstract record AccessRule
{
    /// <summary>Needed before anyone has signed in (starting the program, opening a company, signing in).</summary>
    public sealed record Open : AccessRule;

    /// <summary>Any signed-in person: lists other screens need to fill their pick-lists, and the program's own automatic runs.</summary>
    public sealed record AnySignedIn : AccessRule;

    public sealed record Needs(string Area, PermissionAction Action) : AccessRule;
}

/// <summary>
/// Which permission each route of the API needs (brief section 10.4: per-module and per-action permissions). A route that is not listed here
/// is refused, and a test goes through every route of the API to make sure none is missing.
/// </summary>
public static class PermissionMap
{
    private static readonly AccessRule.Open OpenRule = new();
    private static readonly AccessRule.AnySignedIn SignedIn = new();

    public static AccessRule? Resolve(string method, string path)
    {
        var segments = path.Split('?')[0].Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => s.ToLowerInvariant()).ToArray();
        if (segments.Length < 2 || segments[0] != "api")
            return null;

        var head = segments[1];
        var rest = segments[2..];
        var read = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        var verb = read ? PermissionAction.View : HttpMethods.IsDelete(method) ? PermissionAction.Delete : PermissionAction.Edit;

        AccessRule Needs(string area, PermissionAction action) => new AccessRule.Needs(area, action);
        // A list that other screens use to fill a pick-list: reading it needs only a sign-in, changing it needs the area.
        AccessRule Lookup(string area) => read ? SignedIn : Needs(area, verb);
        bool Has(params string[] words) => rest.Any(words.Contains);

        return head switch
        {
            "host" or "startup" or "countries" or "currencies" or "modules" or "dialogs" or "recent-files" or "session" or "openapi" => OpenRule,
            "company" => rest.Length == 0 && read || rest is ["create" or "open" or "close" or "restore"] ? OpenRule
                : rest is ["modules"] ? Needs(PermissionAreas.Settings, PermissionAction.Edit)
                : rest is ["backup"] ? Needs(PermissionAreas.Settings, PermissionAction.View)
                : null,

            "accounts" or "cost-centers" or "exchange-rates" => Lookup(PermissionAreas.Accounting),
            "vouchers" => Needs(PermissionAreas.Accounting, verb),
            "periods" or "fiscal-years" => read ? Needs(PermissionAreas.Accounting, PermissionAction.View) : Needs(PermissionAreas.Accounting, PermissionAction.Approve),
            "recurring" => rest is ["run-due"] ? SignedIn : Needs(PermissionAreas.Accounting, verb),

            "parties" => Needs(PermissionAreas.Parties, verb),
            "bank" => Needs(PermissionAreas.Banking, verb),
            "documents" or "settlements" => Needs(PermissionAreas.Trade, verb),
            "products" or "price-lists" or "pricing" => Lookup(PermissionAreas.Products),
            "tax-codes" => Lookup(PermissionAreas.Tax),
            "warehouses" => Lookup(PermissionAreas.Inventory),
            "stock" => Needs(PermissionAreas.Inventory, verb),

            "assets" => rest is ["depreciation", "run-due"] ? SignedIn : Needs(PermissionAreas.Assets, verb),
            "employees" or "salary-components" or "leave" => Needs(PermissionAreas.Payroll, verb),
            "payroll" => rest is ["runs", "run-due"] or ["end-of-service", "run-due"] ? SignedIn
                : !read && (Has("post", "unpost", "pay", "unpay", "accrue", "undo") || rest is ["settings"]) ? Needs(PermissionAreas.Payroll, PermissionAction.Approve)
                : Needs(PermissionAreas.Payroll, verb),
            "claims" => !read && Has("approve", "reject", "unapprove", "pay", "unpay") ? Needs(PermissionAreas.Claims, PermissionAction.Approve) : Needs(PermissionAreas.Claims, verb),
            "budgets" => Needs(PermissionAreas.Budgets, verb),

            "import" => rest is ["templates", ..] ? SignedIn : rest.Length > 0 ? Needs(AreaOfImport(rest[0]), PermissionAction.Edit) : null,
            "reports" => rest.Length > 0 ? Needs(AreaOfReport(rest[0]), PermissionAction.View) : null,
            "branding" => Lookup(PermissionAreas.Settings),
            "print" => Needs(PermissionAreas.Settings, PermissionAction.View),
            "dashboard" => Needs(PermissionAreas.Reports, PermissionAction.View),
            "approvals" => SignedIn, // everyone sees what they sent; approving needs the area's Approve (checked by the service)
            "users" or "roles" or "audit-log" => Needs(PermissionAreas.Users, verb),
            _ => null,
        };
    }

    /// <summary>The report or list a key names, and the area that decides who may read it.</summary>
    public static string AreaOfReport(string key) => key switch
    {
        "documents" => PermissionAreas.Trade,
        "products" => PermissionAreas.Products,
        "parties" or "aging-receivable" or "aging-payable" or "party-statement" => PermissionAreas.Parties,
        "tax-codes" or "tax-return" => PermissionAreas.Tax,
        "chart-of-accounts" or "vouchers" or "recurring" or "exchange-rates" or "cost-center-list" => PermissionAreas.Accounting,
        "price-lists" => PermissionAreas.Products,
        "bank-accounts" => PermissionAreas.Banking,
        "stock-valuation" or "stock-movements" or "stock-reorder" or "warehouses" or "stock-documents" => PermissionAreas.Inventory,
        "asset-register" or "assets" => PermissionAreas.Assets,
        "employees" or "salary-components" or "payroll-summary" or "leave-balances" or "end-of-service" => PermissionAreas.Payroll,
        "expense-claims" => PermissionAreas.Claims,
        "budget" or "budget-vs-actual" => PermissionAreas.Budgets,
        "audit-log" => PermissionAreas.Users,
        _ => PermissionAreas.Reports, // trial balance, profit and loss, balance sheet, ledgers, cost centers
    };

    private static string AreaOfImport(string what) => what switch
    {
        "products" => PermissionAreas.Products,
        "parties" => PermissionAreas.Parties,
        "opening-stock" => PermissionAreas.Inventory,
        "assets" => PermissionAreas.Assets,
        "employees" => PermissionAreas.Payroll,
        "budget" => PermissionAreas.Budgets,
        _ => PermissionAreas.Accounting, // accounts, exchange rates, journal entries, opening balances
    };
}
