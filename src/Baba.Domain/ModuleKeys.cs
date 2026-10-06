namespace Baba.Domain;

/// <summary>Optional modules a company can switch on or off (brief section 10). Core accounting is always on.</summary>
public static class ModuleKeys
{
    public const string BankAndCash = "bank-cash";
    public const string CustomersAndSuppliers = "customers-suppliers";
    public const string CostCenters = "cost-centers";
    public const string Sales = "sales";
    public const string Purchases = "purchases";
    public const string Inventory = "inventory";
    public const string FixedAssets = "fixed-assets";
    public const string Payroll = "payroll";
    public const string ExpenseClaims = "expense-claims";
    public const string Budgets = "budgets";

    public static IReadOnlyList<string> All { get; } =
    [
        BankAndCash, CustomersAndSuppliers, CostCenters, Sales, Purchases,
        Inventory, FixedAssets, Payroll, ExpenseClaims, Budgets,
    ];
}
