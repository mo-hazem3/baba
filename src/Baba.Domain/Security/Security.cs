using Baba.Domain.Accounting;

namespace Baba.Domain.Security;

/// <summary>What a person may do in an area of the program (brief section 10.4: per-module and per-action permissions).</summary>
public enum PermissionAction
{
    /// <summary>See it and read its reports.</summary>
    View,

    /// <summary>Add and change.</summary>
    Edit,

    /// <summary>Delete.</summary>
    Delete,

    /// <summary>Approve, post, lock and close: the steps that make something final. Where approval is switched on, only people with this can post.</summary>
    Approve,
}

/// <summary>The areas permissions are given for. Every screen and every API route belongs to exactly one.</summary>
public static class PermissionAreas
{
    public const string Accounting = "accounting";
    public const string Reports = "reports";
    public const string Banking = "banking";
    public const string Parties = "parties";
    public const string Trade = "trade";
    public const string Products = "products";
    public const string Tax = "tax";
    public const string Inventory = "inventory";
    public const string Assets = "assets";
    public const string Payroll = "payroll";
    public const string Claims = "claims";
    public const string Budgets = "budgets";
    public const string Settings = "settings";
    public const string Users = "users";

    /// <summary>In the order the role screen shows them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Accounting, Reports, Banking, Parties, Trade, Products, Tax, Inventory, Assets, Payroll, Claims, Budgets, Settings, Users,
    ];

    /// <summary>The optional module an area belongs to, so the role screen can leave out what the company does not use. Null: always there.</summary>
    public static string? ModuleOf(string area) => area switch
    {
        Banking => ModuleKeys.BankAndCash,
        Parties => ModuleKeys.CustomersAndSuppliers,
        Inventory => ModuleKeys.Inventory,
        Assets => ModuleKeys.FixedAssets,
        Payroll => ModuleKeys.Payroll,
        Claims => ModuleKeys.ExpenseClaims,
        Budgets => ModuleKeys.Budgets,
        _ => null,
    };
}

public static class Permission
{
    public static string Key(string area, PermissionAction action) => $"{area}.{action}";

    public static IEnumerable<string> All() => PermissionAreas.All.SelectMany(a => Enum.GetValues<PermissionAction>().Select(x => Key(a, x)));

    public static bool IsValid(string key) =>
        key.Split('.') is [var area, var action] && PermissionAreas.All.Contains(area) && Enum.TryParse<PermissionAction>(action, out _);
}

/// <summary>A role every company starts with. The permissions of a built-in role are fixed; a company copies one to make its own.</summary>
public sealed record BuiltInRole(string Key, string NameEn, string NameAr, IReadOnlyList<string> Permissions);

public static class BuiltInRoles
{
    public const string Admin = "admin";
    public const string Accountant = "accountant";
    public const string Sales = "sales";
    public const string Storekeeper = "storekeeper";
    public const string Viewer = "viewer";

    private static IEnumerable<string> Everything(string area) => Enum.GetValues<PermissionAction>().Select(a => Permission.Key(area, a));

    private static string View(string area) => Permission.Key(area, PermissionAction.View);

    private static IEnumerable<string> ViewAndEdit(string area) => [Permission.Key(area, PermissionAction.View), Permission.Key(area, PermissionAction.Edit)];

    public static IReadOnlyList<BuiltInRole> All { get; } =
    [
        new(Admin, "Administrator", "مدير النظام", [.. Permission.All()]),
        new(Accountant, "Accountant", "محاسب",
            [.. PermissionAreas.All.Where(a => a is not (PermissionAreas.Users or PermissionAreas.Settings)).SelectMany(Everything), View(PermissionAreas.Settings)]),
        new(Sales, "Sales", "مبيعات",
            [
                .. ViewAndEdit(PermissionAreas.Trade), .. ViewAndEdit(PermissionAreas.Parties), .. ViewAndEdit(PermissionAreas.Products),
                View(PermissionAreas.Reports), View(PermissionAreas.Inventory), View(PermissionAreas.Tax), View(PermissionAreas.Settings),
            ]),
        new(Storekeeper, "Storekeeper", "أمين مخزن",
            [
                .. Everything(PermissionAreas.Inventory), View(PermissionAreas.Products), View(PermissionAreas.Trade), View(PermissionAreas.Reports), View(PermissionAreas.Settings),
            ]),
        new(Viewer, "Viewer", "مشاهد (للقراءة فقط)",
            [.. PermissionAreas.All.Where(a => a is not (PermissionAreas.Users or PermissionAreas.Settings)).Select(View), View(PermissionAreas.Settings)]),
    ];
}

/// <summary>A role: a named set of permissions (brief section 10.4).</summary>
public sealed class Role : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";

    /// <summary>The key of the built-in role this is (admin, accountant ...); empty for a role the company made. Built-in roles cannot be changed or deleted.</summary>
    public string? BuiltInKey { get; set; }

    /// <summary>The permissions, as <c>area.Action</c> keys separated by commas.</summary>
    public string PermissionsText { get; set; } = "";

    public IReadOnlySet<string> Permissions => PermissionsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

    public void SetPermissions(IEnumerable<string> permissions) =>
        PermissionsText = string.Join(',', permissions.Distinct().OrderBy(p => p, StringComparer.Ordinal));

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>A person who signs in to the company file. The sign-in name is what the audit log records, so it never changes.</summary>
public sealed class AppUser : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }

    /// <summary>The name typed to sign in (not case sensitive). Fixed once made, because the audit log keeps it.</summary>
    public string UserName { get; set; } = "";

    /// <summary>The name shown on screen and on printouts.</summary>
    public string DisplayName { get; set; } = "";

    public Guid RoleId { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>The password as a salted PBKDF2 hash (never the password itself).</summary>
    public string PasswordHash { get; set; } = "";

    public string PasswordSalt { get; set; } = "";
    public int PasswordIterations { get; set; }

    /// <summary>The person has to choose their own password the next time they sign in (an administrator set or reset it).</summary>
    public bool MustChangePassword { get; set; }

    public DateTime? LastSignInAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
