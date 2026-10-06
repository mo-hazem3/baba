using Baba.Domain;

namespace Baba.Localization;

/// <summary>
/// The generic starting chart from the brief (section 10.1). Country packs can use it as is,
/// extend it, or replace it with their own template.
/// </summary>
public static class DefaultChartOfAccounts
{
    public static ChartOfAccountsTemplate Template { get; } = new(
        Key: "default",
        NameEn: "Standard chart of accounts",
        NameAr: "دليل الحسابات القياسي",
        Accounts:
        [
            // Assets
            new("1", "Assets", "الأصول", AccountType.Asset, null, false),
            new("11", "Current assets", "الأصول المتداولة", AccountType.Asset, "1", false),
            new("12", "Fixed assets", "الأصول الثابتة", AccountType.Asset, "1", false),
            new("13", "Intangible assets", "الأصول غير الملموسة", AccountType.Asset, "1", false),

            // Liabilities
            new("2", "Liabilities", "الخصوم", AccountType.Liability, null, false),
            new("21", "Current liabilities", "الخصوم المتداولة", AccountType.Liability, "2", false),
            new("22", "Long-term liabilities", "الخصوم طويلة الأجل", AccountType.Liability, "2", false),

            // Owners' equity
            new("3", "Owners' equity", "حقوق الملكية", AccountType.Equity, null, false),
            new("31", "Capital", "رأس المال", AccountType.Equity, "3", true),
            new("32", "Legal reserve", "الاحتياطي القانوني", AccountType.Equity, "3", true),
            new("33", "Optional reserve", "الاحتياطي الاختياري", AccountType.Equity, "3", true),
            new("34", "Retained earnings", "الأرباح المبقاة", AccountType.Equity, "3", true),
            new("35", "Owners' current accounts", "الحسابات الجارية للملاك", AccountType.Equity, "3", true),

            // Expenses
            new("4", "Expenses", "المصروفات", AccountType.Expense, null, false),
            new("41", "Cost of sales", "تكلفة المبيعات", AccountType.Expense, "4", true),
            new("42", "General expenses", "المصروفات العمومية", AccountType.Expense, "4", true),

            // Revenues
            new("5", "Revenues", "الإيرادات", AccountType.Revenue, null, false),
            new("51", "Sales", "المبيعات", AccountType.Revenue, "5", true),
            new("52", "Gains", "المكاسب", AccountType.Revenue, "5", true),
        ]);
}
