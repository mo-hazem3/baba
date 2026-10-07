using Baba.Application.Accounting;
using Baba.Application.Assets;
using Baba.Application.Companies;
using Baba.Application.Inventory;
using Baba.Application.Trade;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Assets;
using Baba.Domain.Inventory;
using Baba.Domain.Trade;
using Baba.Localization;

namespace Baba.Application.Reporting;

/// <summary>
/// The lists of the app (chart of accounts, vouchers, documents, products, customers and suppliers, tax codes, recurring schedules,
/// exchange rates) as report tables, so the same exports (PDF, Excel, CSV) work for them as for every other report: exporting
/// everything to Excel is a requirement of the owner (brief section 11).
/// </summary>
public sealed class ListingService(
    ChartOfAccountsService chart,
    VoucherService vouchers,
    DocumentService documents,
    ProductService products,
    PartyService parties,
    TaxService taxes,
    RecurringService recurring,
    ExchangeRateService rates,
    StockService stock,
    WarehouseService warehouses,
    StockDocumentService stockDocuments,
    AssetService assets,
    ILedgerQuery ledger,
    IAccountStore accounts,
    ICompanyFiles files)
{
    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    private ReportResult Table(string key, (string En, string Ar) title, (string En, string Ar) subtitle, ReportColumn[] columns, List<ReportRow> rows)
    {
        var (company, currency) = Company();
        return new ReportResult(key, title.En, title.Ar, subtitle.En, subtitle.Ar, company.NameEn, company.NameAr, company.BaseCurrencyCode, currency.MinorUnits, columns, rows, []);
    }

    private static ReportCell Active(bool isActive) => isActive ? new("Active", "نشط") : new("Inactive", "غير نشط");

    private static ReportCell Number(decimal value, int decimals) => new(value.ToString("0." + new string('#', decimals), Invariant));

    /// <summary>Sales and purchase documents: quotes, orders, delivery and receipt notes, invoices, credit and debit notes.</summary>
    public async Task<ReportResult> DocumentsAsync(DocumentSearch search, CancellationToken cancellationToken = default)
    {
        var list = await documents.ListAsync(search with { Limit = 100_000 }, cancellationToken);
        var names = (await parties.ListAsync(null, cancellationToken)).ToDictionary(p => p.Id);
        var rows = list.Select(d => new ReportRow(
            [
                new(d.Number ?? "—", d.Number ?? "—"), new(Date: d.Date), d.DueDate is { } due ? new(Date: due) : ReportCell.Blank,
                names.TryGetValue(d.PartyId, out var p) ? new(p.NameEn, p.NameAr) : ReportCell.Blank,
                new(DocumentKindName(d.Kind).En, DocumentKindName(d.Kind).Ar),
                new(DocumentStatusName(d.Status).En, DocumentStatusName(d.Status).Ar),
                new(d.Reference), new(d.CurrencyCode), new(Amount: d.Total),
            ],
            0, RowStyle.Normal)).ToList();

        var range = ReportLabels.Range(search.From, search.To);
        return Table("documents", ("Sales and purchase documents", "مستندات المبيعات والمشتريات"), range,
            [
                Column("number", ColumnKind.Text, "Number", "الرقم"), Column("date", ColumnKind.Date, "Date", "التاريخ"), Column("due", ColumnKind.Date, "Due date", "تاريخ الاستحقاق"),
                Column("party", ColumnKind.Text, "Customer / supplier", "العميل / المورّد"), Column("kind", ColumnKind.Text, "Type", "النوع"),
                Column("status", ColumnKind.Text, "Status", "الحالة"), Column("reference", ColumnKind.Text, "Reference", "المرجع"),
                Column("currency", ColumnKind.Text, "Currency", "العملة"), Column("total", ColumnKind.Amount, "Total", "الإجمالي"),
            ], rows);
    }

    public async Task<ReportResult> ProductsAsync(CancellationToken cancellationToken = default)
    {
        var list = await products.ListAsync(cancellationToken);
        var codes = (await taxes.ListAsync(cancellationToken)).ToDictionary(c => c.Id);
        var rows = list.Select(p => new ReportRow(
            [
                new(p.Code), new(p.NameEn, p.NameAr), new(p.Unit), new(Amount: p.SalePrice), new(Amount: p.PurchasePrice),
                p.TaxCodeId is { } id && codes.TryGetValue(id, out var c) ? new(c.Code) : ReportCell.Blank, Active(p.IsActive),
                p.IsStockItem ? new("Yes", "نعم") : ReportCell.Blank, new(p.Barcode), p.IsStockItem ? new(Amount: p.ReorderLevel) : ReportCell.Blank,
            ],
            0, RowStyle.Normal)).ToList();
        return Table("products", ("Products and services", "الأصناف والخدمات"), ($"{list.Count} items", $"{list.Count} صنفاً"),
            [
                Column("code", ColumnKind.Text, "Code", "الرمز"), Column("name", ColumnKind.Text, "Name", "الاسم"), Column("unit", ColumnKind.Text, "Unit", "الوحدة"),
                Column("sale", ColumnKind.Amount, "Sale price", "سعر البيع"), Column("purchase", ColumnKind.Amount, "Purchase price", "سعر الشراء"),
                Column("tax", ColumnKind.Text, "Tax code", "رمز الضريبة"), Column("status", ColumnKind.Text, "Status", "الحالة"),
                Column("stock", ColumnKind.Text, "Stock item", "صنف مخزون"), Column("barcode", ColumnKind.Text, "Barcode", "الباركود"), Column("reorder", ColumnKind.Amount, "Reorder level", "حد إعادة الطلب"),
            ], rows);
    }

    public async Task<ReportResult> PartiesAsync(PartyKind? kind, CancellationToken cancellationToken = default)
    {
        var list = await parties.ListAsync(kind, cancellationToken);
        var rows = list.Select(p => new ReportRow(
            [
                new(p.Code), new(p.NameEn, p.NameAr), new(p.Kind == PartyKind.Customer ? "Customer" : "Supplier", p.Kind == PartyKind.Customer ? "عميل" : "مورّد"),
                new(p.Phone), new(p.Email), new(p.TaxNumber), new(p.PaymentTermsDays.ToString(Invariant)),
                new(Amount: p.CreditLimit), new(Amount: p.Balance), Active(p.IsActive),
            ],
            0, RowStyle.Normal, new ReportLink(ReportLink.Party, p.Id))).ToList();
        var title = kind switch
        {
            PartyKind.Customer => ("Customers", "العملاء"),
            PartyKind.Supplier => ("Suppliers", "الموردون"),
            _ => ("Customers and suppliers", "العملاء والموردون"),
        };
        return Table("parties", title, ($"{list.Count} records", $"{list.Count} سجلاً"),
            [
                Column("code", ColumnKind.Text, "Code", "الرمز"), Column("name", ColumnKind.Text, "Name", "الاسم"), Column("kind", ColumnKind.Text, "Type", "النوع"),
                Column("phone", ColumnKind.Text, "Phone", "الهاتف"), Column("email", ColumnKind.Text, "Email", "البريد الإلكتروني"), Column("tax", ColumnKind.Text, "Tax number", "الرقم الضريبي"),
                Column("terms", ColumnKind.Text, "Payment terms (days)", "مدة السداد (أيام)"), Column("limit", ColumnKind.Amount, "Credit limit", "الحد الائتماني"),
                Column("balance", ColumnKind.Amount, "Balance", "الرصيد"), Column("status", ColumnKind.Text, "Status", "الحالة"),
            ], rows);
    }

    public async Task<ReportResult> TaxCodesAsync(CancellationToken cancellationToken = default)
    {
        var list = await taxes.ListAsync(cancellationToken);
        var accounts = (await chart.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        ReportCell AccountCell(Guid? id) => id is { } x && accounts.TryGetValue(x, out var a) ? new($"{a.Code} {a.NameEn}", $"{a.Code} {a.NameAr}") : ReportCell.Blank;
        var rows = list.Select(c => new ReportRow(
            [
                new(c.Code), new(c.NameEn, c.NameAr), Number(c.Rate, 4), new(c.Treatment switch { TaxTreatment.Standard => "Standard", TaxTreatment.Zero => "Zero rate", TaxTreatment.Exempt => "Exempt", _ => "Out of scope" },
                    c.Treatment switch { TaxTreatment.Standard => "قياسية", TaxTreatment.Zero => "نسبة صفر", TaxTreatment.Exempt => "معفى", _ => "خارج النطاق" }),
                c.EffectiveFrom is { } from ? new(Date: from) : ReportCell.Blank, c.EffectiveTo is { } to ? new(Date: to) : ReportCell.Blank,
                AccountCell(c.OutputAccountId), AccountCell(c.InputAccountId), c.IsDefault ? new("Yes", "نعم") : ReportCell.Blank, Active(c.IsActive),
            ],
            0, RowStyle.Normal)).ToList();
        return Table("tax-codes", ("Tax codes", "رموز الضريبة"), ($"{list.Count} codes", $"{list.Count} رمزاً"),
            [
                Column("code", ColumnKind.Text, "Code", "الرمز"), Column("name", ColumnKind.Text, "Name", "الاسم"), Column("rate", ColumnKind.Text, "Rate %", "النسبة %"),
                Column("treatment", ColumnKind.Text, "Treatment", "المعالجة"), Column("from", ColumnKind.Date, "From", "من"), Column("to", ColumnKind.Date, "Until", "حتى"),
                Column("output", ColumnKind.Text, "Tax payable account", "حساب الضريبة المستحقة"), Column("input", ColumnKind.Text, "Tax receivable account", "حساب الضريبة المدينة"),
                Column("default", ColumnKind.Text, "Default", "الافتراضي"), Column("status", ColumnKind.Text, "Status", "الحالة"),
            ], rows);
    }

    public async Task<ReportResult> RecurringAsync(CancellationToken cancellationToken = default)
    {
        var list = await recurring.ListAsync(cancellationToken);
        var rows = list.Select(s => new ReportRow(
            [
                new(s.Name), new(s.TemplateKind), new(s.Frequency.ToString()), new(Date: s.NextRunDate), s.EndDate is { } end ? new(Date: end) : ReportCell.Blank,
                s.AutoIssue ? new("Issued", "مُصدَر") : new("Draft", "مسودة"), new(s.RunCount.ToString(Invariant)), Active(s.IsActive),
            ],
            0, RowStyle.Normal)).ToList();
        return Table("recurring", ("Recurring invoices and entries", "الفواتير والقيود المتكررة"), ($"{list.Count} schedules", $"{list.Count} جدولاً"),
            [
                Column("name", ColumnKind.Text, "Name", "الاسم"), Column("makes", ColumnKind.Text, "Makes", "ينشئ"), Column("frequency", ColumnKind.Text, "How often", "كل متى"),
                Column("next", ColumnKind.Date, "Next date", "التاريخ التالي"), Column("end", ColumnKind.Date, "Ends", "ينتهي في"), Column("mode", ColumnKind.Text, "Made as", "يُنشأ كـ"),
                Column("runs", ColumnKind.Text, "Times", "المرات"), Column("status", ColumnKind.Text, "Status", "الحالة"),
            ], rows);
    }

    public async Task<ReportResult> ExchangeRatesAsync(CancellationToken cancellationToken = default)
    {
        var list = await rates.ListAsync(null, cancellationToken);
        var rows = list.OrderBy(r => r.CurrencyCode).ThenByDescending(r => r.Date).Select(r => new ReportRow(
            [new(r.CurrencyCode), new(Date: r.Date), Number(r.Rate, 6)], 0, RowStyle.Normal)).ToList();
        return Table("exchange-rates", ("Exchange rates", "أسعار الصرف"), ($"{list.Count} rates", $"{list.Count} سعراً"),
            [Column("currency", ColumnKind.Text, "Currency", "العملة"), Column("date", ColumnKind.Date, "From", "من"), Column("rate", ColumnKind.Text, "Worth in the company's currency", "تساوي بعملة الشركة")], rows);
    }

    // ---------------------------------------------------------------- Assets

    /// <summary>
    /// The fixed and intangible asset register on a date: cost, depreciation so far and book value of every asset still held, checked
    /// against the ledger accounts the assets use.
    /// </summary>
    public async Task<ReportResult> AssetRegisterAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var everything = (await assets.PositionsAsync(asOf, cancellationToken)).ToList();
        var positions = everything.Where(p => !p.Disposed).ToList();
        var rows = positions.Select(p =>
        {
            var a = p.Asset;
            var kind = a.Kind == AssetKind.Tangible ? ("Fixed asset", "أصل ثابت") : ("Intangible", "أصل غير ملموس");
            var method = a.Method == DepreciationMethod.StraightLine ? ("Straight line", "قسط ثابت") : ("Declining balance", "قسط متناقص");
            return new ReportRow(
                [new(a.Code), new(a.NameEn, a.NameAr), new(kind.Item1, kind.Item2), new(Date: a.AcquisitionDate), new(method.Item1, method.Item2), new(Amount: a.Cost), new(Amount: p.Accumulated), new(Amount: a.Cost - p.Accumulated)],
                0, RowStyle.Normal);
        }).ToList();
        var cost = positions.Sum(p => p.Asset.Cost);
        var accumulated = positions.Sum(p => p.Accumulated);
        rows.Add(new ReportRow([ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, new(Amount: cost), new(Amount: accumulated), new(Amount: cost - accumulated)], 0, RowStyle.Total));

        // What the ledger says about the accounts the register uses (an account shared by assets is counted once).
        var totals = (await ledger.TotalsAsync(null, asOf, cancellationToken)).ToDictionary(t => t.AccountId);
        decimal Balance(IEnumerable<Guid> ids) => ids.Distinct().Sum(id => totals.TryGetValue(id, out var t) ? t.Debit - t.Credit : 0m);
        var checks = new List<ReportCheck>
        {
            new("Cost agrees with the asset accounts in the ledger", "التكلفة تساوي حسابات الأصول في دفتر الأستاذ", Balance(everything.Select(p => p.Asset.AssetAccountId)) == everything.Where(p => !p.Disposed).Sum(p => p.Asset.Cost)),
            new("Depreciation agrees with the accumulated depreciation accounts in the ledger", "الإهلاك يساوي حسابات مجمع الإهلاك في دفتر الأستاذ", -Balance(everything.Select(p => p.Asset.AccumulatedAccountId)) == accumulated),
        };

        return Table("asset-register", ("Fixed asset register", "سجل الأصول الثابتة"), ReportLabels.Range(null, asOf),
            [
                Column("code", ColumnKind.Text, "Code", "الرمز"), Column("name", ColumnKind.Text, "Name", "الاسم"), Column("kind", ColumnKind.Text, "Type", "النوع"),
                Column("acquired", ColumnKind.Date, "Acquired", "تاريخ الاقتناء"), Column("method", ColumnKind.Text, "Method", "الطريقة"),
                Column("cost", ColumnKind.Amount, "Cost", "التكلفة"), Column("accumulated", ColumnKind.Amount, "Depreciation", "الإهلاك المتراكم"), Column("book", ColumnKind.Amount, "Book value", "القيمة الدفترية"),
            ], rows) with { Checks = checks };
    }

    // ---------------------------------------------------------------- Stock

    /// <summary>
    /// What is on hand on a date and what it is worth, product by product (brief sections 10.4 and 11). Without a warehouse chosen, the whole
    /// company: then the report also checks that the value equals the stock accounts of the ledger.
    /// </summary>
    public async Task<ReportResult> StockValuationAsync(DateOnly asOf, Guid? warehouseId, CancellationToken cancellationToken = default)
    {
        var levels = (await stock.LevelsAsync(asOf, cancellationToken)).Where(l => warehouseId is null || l.WarehouseId == warehouseId).ToList();
        var productList = (await products.ListAsync(cancellationToken)).ToDictionary(p => p.Id);

        var rows = levels.GroupBy(l => l.ProductId)
            .Where(g => productList.ContainsKey(g.Key))
            .OrderBy(g => productList[g.Key].Code, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var p = productList[g.Key];
                var quantity = g.Sum(l => l.Quantity);
                var value = g.Sum(l => l.Value);
                return new ReportRow(
                    [new(p.Code), new(p.NameEn, p.NameAr), new(p.Unit), new(Amount: quantity), quantity == 0 ? ReportCell.Blank : new(Amount: Math.Round(value / quantity, 4)), new(Amount: value)],
                    0, RowStyle.Normal);
            }).ToList();

        var total = levels.Sum(l => l.Value);
        rows.Add(new ReportRow([ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, new("Total", "الإجمالي"), new(Amount: total)], 0, RowStyle.Total));

        var checks = new List<ReportCheck>();
        if (warehouseId is null)
        {
            // The stock accounts of the ledger: the company's, and any a product has of its own.
            var chart = await accounts.ListAsync(cancellationToken);
            var stockAccounts = chart.Where(a => a.Role == AccountRole.Inventory).Select(a => a.Id).Concat(productList.Values.Select(p => p.InventoryAccountId).OfType<Guid>()).ToHashSet();
            var ledgerValue = (await ledger.TotalsAsync(null, asOf, cancellationToken)).Where(t => stockAccounts.Contains(t.AccountId)).Sum(t => t.Debit - t.Credit);
            checks.Add(new ReportCheck("Stock value equals the stock accounts in the ledger", "قيمة المخزون تساوي حسابات المخزون في دفتر الأستاذ", ledgerValue == total));
        }

        return Table("stock-valuation", ("Stock valuation", "تقييم المخزون"), ReportLabels.Range(null, asOf),
            [
                Column("code", ColumnKind.Text, "Code", "الرمز"), Column("name", ColumnKind.Text, "Name", "الاسم"), Column("unit", ColumnKind.Text, "Unit", "الوحدة"),
                Column("quantity", ColumnKind.Amount, "Quantity", "الكمية"), Column("cost", ColumnKind.Amount, "Average cost", "متوسط التكلفة"), Column("value", ColumnKind.Amount, "Value", "القيمة"),
            ], rows) with { Checks = checks };
    }

    /// <summary>Every movement of stock in a period, with the document that made it.</summary>
    public async Task<ReportResult> StockMovementsAsync(DateOnly? from, DateOnly? to, Guid? productId, Guid? warehouseId, CancellationToken cancellationToken = default)
    {
        var productList = (await products.ListAsync(cancellationToken)).ToDictionary(p => p.Id);
        var warehouseList = (await warehouses.ListAsync(cancellationToken)).ToDictionary(w => w.Id);
        var numbers = (await documents.ListAsync(new DocumentSearch(Limit: 100_000), cancellationToken)).ToDictionary(d => d.Id, d => d.Number ?? "");
        foreach (var d in await stockDocuments.ListAsync(null, cancellationToken))
            numbers[d.Id] = d.Number;

        var rows = (await stockMovementsSource(cancellationToken))
            .Where(m => (from is null || m.Date >= from) && (to is null || m.Date <= to) && (productId is null || m.ProductId == productId) && (warehouseId is null || m.WarehouseId == warehouseId))
            .OrderBy(m => m.Date).ThenBy(m => m.SourceCreatedAt).ThenBy(m => m.Id)
            .Where(m => productList.ContainsKey(m.ProductId))
            .Select(m =>
            {
                var p = productList[m.ProductId];
                var w = warehouseList.GetValueOrDefault(m.WarehouseId);
                var kind = MovementKindName(m.Kind);
                return new ReportRow(
                    [
                        new(Date: m.Date), new(numbers.GetValueOrDefault(m.DocumentId ?? m.StockDocumentId ?? Guid.Empty) ?? ""), new(kind.En, kind.Ar), new(p.Code), new(p.NameEn, p.NameAr),
                        w is null ? ReportCell.Blank : new(w.NameEn, w.NameAr),
                        m.QuantityScaled > 0 ? new(Amount: m.Quantity) : ReportCell.Blank, m.QuantityScaled < 0 ? new(Amount: -m.Quantity) : ReportCell.Blank, new(Amount: m.Value),
                    ],
                    0, RowStyle.Normal);
            }).ToList();

        return Table("stock-movements", ("Stock movements", "حركة المخزون"), ReportLabels.Range(from, to),
            [
                Column("date", ColumnKind.Date, "Date", "التاريخ"), Column("document", ColumnKind.Text, "Document", "المستند"), Column("type", ColumnKind.Text, "Type", "النوع"),
                Column("code", ColumnKind.Text, "Code", "الرمز"), Column("product", ColumnKind.Text, "Product", "الصنف"), Column("warehouse", ColumnKind.Text, "Warehouse", "المستودع"),
                Column("in", ColumnKind.Amount, "In", "وارد"), Column("out", ColumnKind.Amount, "Out", "صادر"), Column("value", ColumnKind.Amount, "Value", "القيمة"),
            ], rows);
    }

    private Task<IReadOnlyList<StockMovement>> stockMovementsSource(CancellationToken cancellationToken) => stock.MovementsAsync(cancellationToken);

    /// <summary>Products that have fallen to their reorder level.</summary>
    public async Task<ReportResult> StockReorderAsync(CancellationToken cancellationToken = default)
    {
        var productList = (await products.ListAsync(cancellationToken)).ToDictionary(p => p.Id);
        var low = await stock.LowStockAsync(cancellationToken);
        var rows = low.Select(item =>
        {
            var p = productList[item.ProductId];
            return new ReportRow([new(p.Code), new(p.NameEn, p.NameAr), new(p.Unit), new(Amount: item.OnHand), new(Amount: item.ReorderLevel), new(Amount: item.ReorderLevel - item.OnHand)], 0, RowStyle.Normal);
        }).ToList();
        return Table("stock-reorder", ("Products to reorder", "أصناف تحتاج إعادة طلب"), ($"{low.Count} products", $"{low.Count} صنفاً"),
            [
                Column("code", ColumnKind.Text, "Code", "الرمز"), Column("name", ColumnKind.Text, "Name", "الاسم"), Column("unit", ColumnKind.Text, "Unit", "الوحدة"),
                Column("onhand", ColumnKind.Amount, "On hand", "المتوفر"), Column("level", ColumnKind.Amount, "Reorder level", "حد إعادة الطلب"), Column("short", ColumnKind.Amount, "Below the level by", "أقل من الحد بمقدار"),
            ], rows);
    }

    public async Task<ReportResult> WarehousesAsync(CancellationToken cancellationToken = default)
    {
        var list = await warehouses.ListAsync(cancellationToken);
        var rows = list.Select(w => new ReportRow([new(w.Code), new(w.NameEn, w.NameAr), w.IsDefault ? new("Yes", "نعم") : ReportCell.Blank, Active(w.IsActive)], 0, RowStyle.Normal)).ToList();
        return Table("warehouses", ("Warehouses", "المستودعات"), ($"{list.Count} warehouses", $"{list.Count} مستودعاً"),
            [Column("code", ColumnKind.Text, "Code", "الرمز"), Column("name", ColumnKind.Text, "Name", "الاسم"), Column("default", ColumnKind.Text, "Default", "الافتراضي"), Column("status", ColumnKind.Text, "Status", "الحالة")], rows);
    }

    public async Task<ReportResult> StockDocumentsAsync(StockDocumentKind? kind, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var list = (await stockDocuments.ListAsync(kind, cancellationToken)).Where(d => (from is null || d.Date >= from) && (to is null || d.Date <= to)).ToList();
        var rows = list.Select(d =>
        {
            var name = d.Kind switch { StockDocumentKind.Opening => ("Opening stock", "مخزون افتتاحي"), StockDocumentKind.Adjustment => ("Adjustment", "تسوية مخزون"), _ => ("Transfer", "تحويل مخزون") };
            return new ReportRow([new(d.Number), new(Date: d.Date), new(name.Item1, name.Item2), new(d.Memo), new(d.Lines.Count.ToString(Invariant)), new(Amount: d.Value)], 0, RowStyle.Normal);
        }).ToList();
        return Table("stock-documents", ("Stock documents", "مستندات المخزون"), ReportLabels.Range(from, to),
            [
                Column("number", ColumnKind.Text, "Number", "الرقم"), Column("date", ColumnKind.Date, "Date", "التاريخ"), Column("type", ColumnKind.Text, "Type", "النوع"),
                Column("memo", ColumnKind.Text, "Notes", "ملاحظات"), Column("lines", ColumnKind.Text, "Lines", "الأسطر"), Column("value", ColumnKind.Amount, "Value", "القيمة"),
            ], rows);
    }

    private static (string En, string Ar) MovementKindName(StockMovementKind kind) => kind switch
    {
        StockMovementKind.Purchase => ("Purchase", "شراء"),
        StockMovementKind.PurchaseReturn => ("Purchase return", "مرتجع مشتريات"),
        StockMovementKind.Sale => ("Sale", "بيع"),
        StockMovementKind.SaleReturn => ("Sales return", "مرتجع مبيعات"),
        StockMovementKind.Opening => ("Opening stock", "مخزون افتتاحي"),
        StockMovementKind.Adjustment => ("Adjustment", "تسوية"),
        StockMovementKind.TransferOut => ("Transfer out", "تحويل صادر"),
        _ => ("Transfer in", "تحويل وارد"),
    };

    private static (string En, string Ar) DocumentKindName(DocumentKind kind) => kind switch
    {
        DocumentKind.Quote => ("Quote", "عرض سعر"),
        DocumentKind.SalesOrder => ("Sales order", "أمر بيع"),
        DocumentKind.DeliveryNote => ("Delivery note", "إذن تسليم"),
        DocumentKind.SalesInvoice => ("Sales invoice", "فاتورة مبيعات"),
        DocumentKind.SalesCreditNote => ("Credit note", "إشعار دائن"),
        DocumentKind.PurchaseOrder => ("Purchase order", "أمر شراء"),
        DocumentKind.GoodsReceipt => ("Goods receipt", "إذن استلام"),
        DocumentKind.PurchaseInvoice => ("Purchase invoice", "فاتورة مشتريات"),
        _ => ("Debit note", "إشعار مدين"),
    };

    private static (string En, string Ar) DocumentStatusName(DocumentStatus status) => status switch
    {
        DocumentStatus.Draft => ("Draft", "مسودة"),
        DocumentStatus.Issued => ("Issued", "صادر"),
        _ => ("Converted", "تم تحويله"),
    };

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
        VoucherKind.Transfer => ("Transfer voucher", "سند تحويل"),
        VoucherKind.Opening => ("Opening balances", "أرصدة افتتاحية"),
        VoucherKind.Closing => ("Closing entry", "قيد إقفال"),
        VoucherKind.SalesInvoice => ("Sales invoice", "فاتورة مبيعات"),
        VoucherKind.SalesCreditNote => ("Sales credit note", "إشعار دائن"),
        VoucherKind.PurchaseInvoice => ("Purchase invoice", "فاتورة مشتريات"),
        VoucherKind.PurchaseDebitNote => ("Purchase debit note", "إشعار مدين"),
        VoucherKind.FxSettlement => ("Exchange difference", "فروق عملة"),
        _ => ("Journal voucher", "قيد يومية"),
    };
}
