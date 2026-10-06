using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Localization;

namespace Baba.Application.Reporting;

/// <summary>
/// The chart of accounts and the voucher list as report tables, so the same exports (PDF, Excel, CSV) work for them
/// as for every other report.
/// </summary>
public sealed class ListingService(ChartOfAccountsService chart, VoucherService vouchers, ICompanyFiles files)
{
    public async Task<ReportResult> ChartOfAccountsAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await chart.ListAsync(cancellationToken);
        var rows = new List<ReportRow>();

        var byParent = accounts.ToLookup(a => a.ParentId ?? Guid.Empty);
        var known = accounts.Select(a => a.Id).ToHashSet();
        void Walk(AccountDto account, int level)
        {
            rows.Add(new ReportRow(
                [
                    new(account.Code), new(account.NameEn, account.NameAr),
                    new(TypeName(account.Type).En, TypeName(account.Type).Ar),
                    account.IsPosting ? new("Postable", "قابل للترحيل") : new("Group", "مجموعة"),
                    account.IsActive ? new("Active", "نشط") : new("Inactive", "غير نشط"),
                ],
                level, account.IsPosting ? RowStyle.Normal : RowStyle.Group, new ReportLink(ReportLink.Account, account.Id)));
            foreach (var child in byParent[account.Id])
                Walk(child, level + 1);
        }

        foreach (var root in accounts.Where(a => a.ParentId is not { } p || !known.Contains(p)))
            Walk(root, 0);

        var (company, currency) = Company();
        return new ReportResult(
            "chart-of-accounts", "Chart of accounts", "دليل الحسابات", $"{accounts.Count} accounts", $"{accounts.Count} حساباً",
            company.NameEn, company.NameAr, company.BaseCurrencyCode, currency.MinorUnits,
            [
                Column("code", ColumnKind.Text, "Code", "الرمز"), Column("name", ColumnKind.Text, "Name", "الاسم"),
                Column("type", ColumnKind.Text, "Type", "النوع"), Column("kind", ColumnKind.Text, "Kind", "الصنف"),
                Column("status", ColumnKind.Text, "Status", "الحالة"),
            ],
            rows, []);
    }

    public async Task<ReportResult> VouchersAsync(VoucherSearch search, CancellationToken cancellationToken = default)
    {
        var list = await vouchers.ListAsync(search, cancellationToken);
        var (company, currency) = Company();

        var rows = list.Select(v => new ReportRow(
            [
                new(v.Number ?? "—", v.Number ?? "—"), new(Date: v.Date),
                new(KindName(v.Kind).En, KindName(v.Kind).Ar),
                v.Status == VoucherStatus.Posted ? new("Posted", "مرحّل") : new("Draft", "مسودة"),
                new(v.Reference), new(v.Memo), new(Amount: v.Total),
            ],
            0, RowStyle.Normal, new ReportLink(ReportLink.Voucher, v.Id))).ToList();

        rows.Add(new ReportRow(
            [ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, new("Total", "الإجمالي"), new(Amount: list.Sum(v => v.Total))],
            0, RowStyle.Total));

        var range = ReportLabels.Range(search.From, search.To);
        return new ReportResult(
            "vouchers", "Vouchers", "السندات", range.En, range.Ar, company.NameEn, company.NameAr, company.BaseCurrencyCode, currency.MinorUnits,
            [
                Column("number", ColumnKind.Text, "Number", "الرقم"), Column("date", ColumnKind.Date, "Date", "التاريخ"),
                Column("kind", ColumnKind.Text, "Type", "النوع"), Column("status", ColumnKind.Text, "Status", "الحالة"),
                Column("reference", ColumnKind.Text, "Reference", "المرجع"), Column("memo", ColumnKind.Text, "Notes", "ملاحظات"),
                Column("amount", ColumnKind.Amount, "Amount", "المبلغ"),
            ],
            rows, []);
    }

    private (CompanyInfo Company, Currency Currency) Company()
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        return (company, CurrencyCatalog.Find(company.BaseCurrencyCode)?.Currency ?? new Currency(company.BaseCurrencyCode, 2));
    }

    private static ReportColumn Column(string key, ColumnKind kind, string en, string ar) => new(key, kind, en, ar);

    private static (string En, string Ar) TypeName(AccountType type) => type switch
    {
        AccountType.Asset => ("Asset", "أصول"),
        AccountType.Liability => ("Liability", "خصوم"),
        AccountType.Equity => ("Equity", "حقوق ملكية"),
        AccountType.Revenue => ("Revenue", "إيرادات"),
        _ => ("Expense", "مصروفات"),
    };

    private static (string En, string Ar) KindName(VoucherKind kind) => kind switch
    {
        VoucherKind.Payment => ("Payment voucher", "سند صرف"),
        VoucherKind.Receipt => ("Receipt voucher", "سند قبض"),
        _ => ("Journal voucher", "قيد يومية"),
    };
}
