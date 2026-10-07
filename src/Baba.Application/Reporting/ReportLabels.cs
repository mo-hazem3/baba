namespace Baba.Application.Reporting;

/// <summary>The words printed in reports, in both languages. (Screens use the web app's translation files; printouts are made here.)</summary>
internal static class ReportLabels
{
    public static (string En, string Ar) Code => ("Code", "الرمز");
    public static (string En, string Ar) Account => ("Account", "الحساب");
    public static (string En, string Ar) Date => ("Date", "التاريخ");
    public static (string En, string Ar) Voucher => ("Voucher", "السند");
    public static (string En, string Ar) Description => ("Description", "البيان");
    public static (string En, string Ar) Debit => ("Debit", "مدين");
    public static (string En, string Ar) Credit => ("Credit", "دائن");
    public static (string En, string Ar) Balance => ("Balance", "الرصيد");
    public static (string En, string Ar) Amount => ("Amount", "المبلغ");
    public static (string En, string Ar) PreviousYear => ("Previous year", "العام السابق");

    public static (string En, string Ar) Opening => ("Opening balance", "رصيد أول المدة");
    public static (string En, string Ar) Closing => ("Closing balance", "رصيد آخر المدة");
    public static (string En, string Ar) Movement => ("Movement", "حركة الفترة");
    public static (string En, string Ar) Totals => ("Totals", "الإجماليات");

    public static (string En, string Ar) TrialBalance => ("Trial balance", "ميزان المراجعة");
    public static (string En, string Ar) ProfitAndLoss => ("Profit and loss", "قائمة الدخل");
    public static (string En, string Ar) BalanceSheet => ("Balance sheet", "الميزانية العمومية");
    public static (string En, string Ar) StatementOfAccount => ("Statement of account", "كشف حساب");
    public static (string En, string Ar) GeneralLedger => ("General ledger", "دفتر الأستاذ العام");
    public static (string En, string Ar) Journal => ("Journal", "دفتر اليومية");

    public static (string En, string Ar) PartyStatement => ("Statement", "كشف حساب");
    public static (string En, string Ar) AgingReceivable => ("Customers aging", "أعمار ديون العملاء");
    public static (string En, string Ar) AgingPayable => ("Suppliers aging", "أعمار ديون الموردين");
    public static (string En, string Ar) CostCenters => ("Cost centers", "مراكز التكلفة");
    public static (string En, string Ar) Customer => ("Customer", "العميل");
    public static (string En, string Ar) Supplier => ("Supplier", "المورد");
    public static (string En, string Ar) NotDue => ("Not due", "غير مستحق");
    public static (string En, string Ar) Overdue1To30 => ("1–30 days", "١–٣٠ يومًا");
    public static (string En, string Ar) Overdue31To60 => ("31–60 days", "٣١–٦٠ يومًا");
    public static (string En, string Ar) Overdue61To90 => ("61–90 days", "٦١–٩٠ يومًا");
    public static (string En, string Ar) OverdueOver90 => ("Over 90 days", "أكثر من ٩٠ يومًا");
    public static (string En, string Ar) Total => ("Total", "الإجمالي");
    public static (string En, string Ar) CreditLimit => ("Credit limit", "الحد الائتماني");
    public static (string En, string Ar) WithinCreditLimits => ("No customer is over its credit limit", "لا يتجاوز أي عميل حده الائتماني");
    public static (string En, string Ar) Profit => ("Profit (loss)", "الربح (الخسارة)");

    public static (string En, string Ar) Revenues => ("Revenues", "الإيرادات");
    public static (string En, string Ar) Expenses => ("Expenses", "المصروفات");
    public static (string En, string Ar) TotalRevenues => ("Total revenues", "إجمالي الإيرادات");
    public static (string En, string Ar) TotalExpenses => ("Total expenses", "إجمالي المصروفات");
    public static (string En, string Ar) NetProfit => ("Net profit (loss)", "صافي الربح (الخسارة)");

    public static (string En, string Ar) Assets => ("Assets", "الأصول");
    public static (string En, string Ar) Liabilities => ("Liabilities", "الخصوم");
    public static (string En, string Ar) Equity => ("Owners' equity", "حقوق الملكية");
    public static (string En, string Ar) TotalAssets => ("Total assets", "إجمالي الأصول");
    public static (string En, string Ar) TotalLiabilities => ("Total liabilities", "إجمالي الخصوم");
    public static (string En, string Ar) TotalEquity => ("Total owners' equity", "إجمالي حقوق الملكية");
    public static (string En, string Ar) TotalLiabilitiesAndEquity => ("Total liabilities and equity", "إجمالي الخصوم وحقوق الملكية");
    public static (string En, string Ar) ProfitNotClosed => ("Net profit (loss) not yet closed to retained earnings", "صافي الربح (الخسارة) غير المرحّل إلى الأرباح المبقاة");

    public static (string En, string Ar) DebitsEqualCredits => ("Total debits equal total credits", "إجمالي المدين يساوي إجمالي الدائن");
    public static (string En, string Ar) AssetsEqualLiabilitiesAndEquity => ("Total assets equal total liabilities and equity", "إجمالي الأصول يساوي إجمالي الخصوم وحقوق الملكية");

    /// <summary>"From 01/01/2026 to 31/10/2026", "Until 31/10/2026", "All dates".</summary>
    public static (string En, string Ar) Range(DateOnly? from, DateOnly? to)
    {
        static string Show(DateOnly d) => d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        return (from, to) switch
        {
            ({ } f, { } t) => ($"From {Show(f)} to {Show(t)}", $"من {Show(f)} إلى {Show(t)}"),
            ({ } f, null) => ($"From {Show(f)}", $"من {Show(f)}"),
            (null, { } t) => ($"Until {Show(t)}", $"حتى {Show(t)}"),
            _ => ("All dates", "كل التواريخ"),
        };
    }

    public static (string En, string Ar) AsOf(DateOnly date)
    {
        var text = date.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        return ($"As of {text}", $"حتى تاريخ {text}");
    }
}
