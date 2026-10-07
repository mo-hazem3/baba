using Baba.Domain;

namespace Baba.Localization;

/// <summary>
/// The generic starting chart (brief section 10.1): enough accounts to start recording straight away. Country packs can use it
/// as is, extend it (for example with their VAT accounts), or replace it with their own template.
/// Codes: 1 assets, 2 liabilities, 3 equity, 4 expenses, 5 revenue.
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
            new("111", "Cash on hand", "النقدية بالصندوق", AccountType.Asset, "11", true, AccountRole.CashOrBank),
            new("112", "Bank account", "الحساب البنكي", AccountType.Asset, "11", true, AccountRole.CashOrBank),
            new("113", "Accounts receivable", "العملاء (الذمم المدينة)", AccountType.Asset, "11", true, AccountRole.Receivable),
            new("114", "Inventory", "المخزون", AccountType.Asset, "11", true, AccountRole.Inventory),
            new("115", "Prepaid expenses and advances", "مصروفات مدفوعة مقدمًا وسلف", AccountType.Asset, "11", true),
            new("116", "Taxes receivable", "ضرائب مدينة قابلة للاسترداد", AccountType.Asset, "11", true, AccountRole.TaxReceivable),
            new("12", "Fixed assets", "الأصول الثابتة", AccountType.Asset, "1", false),
            new("121", "Furniture and equipment", "الأثاث والمعدات", AccountType.Asset, "12", true),
            new("122", "Vehicles", "السيارات", AccountType.Asset, "12", true),
            new("123", "Accumulated depreciation", "مجمع الإهلاك", AccountType.Asset, "12", true, AccountRole.AccumulatedDepreciation),
            new("13", "Intangible assets", "الأصول غير الملموسة", AccountType.Asset, "1", false),
            new("131", "Software and licences", "البرامج والتراخيص", AccountType.Asset, "13", true),

            // Liabilities
            new("2", "Liabilities", "الخصوم", AccountType.Liability, null, false),
            new("21", "Current liabilities", "الخصوم المتداولة", AccountType.Liability, "2", false),
            new("211", "Accounts payable", "الموردون (الذمم الدائنة)", AccountType.Liability, "21", true, AccountRole.Payable),
            new("212", "Accrued expenses", "مصروفات مستحقة", AccountType.Liability, "21", true),
            new("213", "Salaries payable", "رواتب مستحقة", AccountType.Liability, "21", true, AccountRole.SalariesPayable),
            new("215", "Social insurance payable", "التأمينات الاجتماعية المستحقة", AccountType.Liability, "21", true, AccountRole.SocialInsurancePayable),
            new("214", "Taxes payable", "ضرائب مستحقة", AccountType.Liability, "21", true, AccountRole.TaxPayable),
            new("22", "Long-term liabilities", "الخصوم طويلة الأجل", AccountType.Liability, "2", false),
            new("221", "Long-term loans", "قروض طويلة الأجل", AccountType.Liability, "22", true),
            new("222", "End-of-service provision", "مخصص مكافأة نهاية الخدمة", AccountType.Liability, "22", true, AccountRole.EndOfServiceProvision),

            // Owners' equity
            new("3", "Owners' equity", "حقوق الملكية", AccountType.Equity, null, false),
            new("31", "Capital", "رأس المال", AccountType.Equity, "3", true),
            new("32", "Legal reserve", "الاحتياطي القانوني", AccountType.Equity, "3", true),
            new("33", "Optional reserve", "الاحتياطي الاختياري", AccountType.Equity, "3", true),
            new("34", "Retained earnings", "الأرباح المبقاة", AccountType.Equity, "3", true, AccountRole.RetainedEarnings),
            new("35", "Owners' current accounts", "الحسابات الجارية للملاك", AccountType.Equity, "3", true),

            // Expenses
            new("4", "Expenses", "المصروفات", AccountType.Expense, null, false),
            new("41", "Cost of sales", "تكلفة المبيعات", AccountType.Expense, "4", false),
            new("411", "Cost of goods sold", "تكلفة البضاعة المباعة", AccountType.Expense, "41", true, AccountRole.CostOfSales),
            new("42", "General expenses", "المصروفات العمومية", AccountType.Expense, "4", false),
            new("421", "Salaries and wages", "الرواتب والأجور", AccountType.Expense, "42", true, AccountRole.SalaryExpense),
            new("422", "Rent", "الإيجار", AccountType.Expense, "42", true),
            new("423", "Utilities", "المرافق (كهرباء ومياه واتصالات)", AccountType.Expense, "42", true),
            new("424", "Office supplies", "أدوات مكتبية", AccountType.Expense, "42", true),
            new("425", "Bank charges", "رسوم بنكية", AccountType.Expense, "42", true),
            new("426", "Depreciation expense", "مصروف الإهلاك", AccountType.Expense, "42", true, AccountRole.DepreciationExpense),
            new("427", "Other expenses", "مصروفات أخرى", AccountType.Expense, "42", true),
            new("429", "Inventory gains and losses", "فروقات جرد المخزون", AccountType.Expense, "42", true, AccountRole.InventoryAdjustment),
            new("430", "Gain or loss on disposal of assets", "أرباح وخسائر بيع الأصول", AccountType.Expense, "42", true, AccountRole.AssetDisposal),
            new("431", "Employer social insurance", "حصة صاحب العمل في التأمينات الاجتماعية", AccountType.Expense, "42", true, AccountRole.SocialInsuranceExpense),
            new("432", "End-of-service expense", "مصروف مكافأة نهاية الخدمة", AccountType.Expense, "42", true, AccountRole.EndOfServiceExpense),
            new("428", "Exchange differences", "فروق العملة", AccountType.Expense, "42", true, AccountRole.ExchangeDifference),

            // Revenues
            new("5", "Revenues", "الإيرادات", AccountType.Revenue, null, false),
            new("51", "Sales", "المبيعات", AccountType.Revenue, "5", false),
            new("511", "Product sales", "مبيعات البضائع", AccountType.Revenue, "51", true),
            new("512", "Service revenue", "إيرادات الخدمات", AccountType.Revenue, "51", true),
            new("52", "Gains and other income", "المكاسب والإيرادات الأخرى", AccountType.Revenue, "5", true),
        ]);
}
