using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Application.Inventory;
using Baba.Application.Trade;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Inventory;
using Baba.Localization;

namespace Baba.Application.Importing;

/// <summary>
/// Importing products, exchange rates, journal entries and opening balances from a CSV or Excel file (Excel import and export are a
/// requirement of the owner). Like every import it is all or nothing: every row is checked first, every wrong row is named by its number
/// and a code the screen translates, and if anything was wrong nothing was changed. Names of columns may be English or Arabic.
/// </summary>
public sealed class ListImportService(
    ITabularReader reader,
    IAccountStore accounts,
    IPartyStore parties,
    ICostCenterStore costCenters,
    TaxService taxes,
    ProductService products,
    ExchangeRateService rates,
    VoucherService vouchers,
    StockDocumentService stockDocuments,
    WarehouseService warehouses,
    ICompanyFiles files)
{
    // ---------------------------------------------------------------- Products

    /// <summary>Columns: code, name (English), name (Arabic), unit, sale price, purchase price, revenue account, expense account, tax code, stock item (yes or no), barcode, reorder level. Only one name is needed.</summary>
    public async Task<ImportResult> ImportProductsAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        if (!TryRead(fileName, content, out var rows))
            return Fail("import.unreadable");

        var header = FindHeader(rows, out var headerIndex, "name", "name en", "english name", "name (english)", "الاسم", "الاسم بالعربية", "الاسم (بالعربية)", "الاسم (بالإنجليزية)", "الاسم بالانجليزية", "الاسم (بالانجليزية)");
        if (header is null)
            return Fail("products.name-column-missing");

        int code = ImportParsing.FindColumn(header, "code", "item code", "الرمز");
        int nameEn = ImportParsing.FindColumn(header, "name", "name en", "english name", "name (english)", "الاسم بالانجليزية", "الاسم (بالانجليزية)", "الاسم (بالإنجليزية)");
        int nameAr = ImportParsing.FindColumn(header, "name ar", "arabic name", "name (arabic)", "الاسم", "الاسم بالعربية", "الاسم (بالعربية)");
        int unit = ImportParsing.FindColumn(header, "unit", "الوحدة");
        int sale = ImportParsing.FindColumn(header, "sale price", "sales price", "price", "selling price", "سعر البيع", "السعر");
        int purchase = ImportParsing.FindColumn(header, "purchase price", "cost", "buying price", "سعر الشراء", "التكلفة");
        int salesAccount = ImportParsing.FindColumn(header, "revenue account", "sales account", "income account", "حساب الايراد", "حساب الإيراد", "حساب المبيعات");
        int purchaseAccount = ImportParsing.FindColumn(header, "expense account", "purchase account", "حساب المصروف", "حساب المشتريات");
        int taxColumn = ImportParsing.FindColumn(header, "tax code", "tax", "vat code", "رمز الضريبة", "الضريبة");
        int stockColumn = ImportParsing.FindColumn(header, "stock item", "stock", "inventory item", "صنف مخزون", "مخزون");
        int barcodeColumn = ImportParsing.FindColumn(header, "barcode", "الباركود");
        int reorderColumn = ImportParsing.FindColumn(header, "reorder level", "reorder", "min stock", "حد إعادة الطلب", "حدّ إعادة الطلب");

        var chart = (await accounts.ListAsync(cancellationToken)).Where(a => a.IsPosting).ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
        var codes = (await taxes.ListAsync(cancellationToken)).ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase); // asking makes sure the country's codes exist
        var existing = (await products.ListAsync(cancellationToken)).Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new List<ProductInput>();
        var issues = new List<ImportIssue>();

        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;

            var rowIssues = issues.Count;
            var line = i + 1;
            var productCode = ImportParsing.Cell(row, code);
            var english = ImportParsing.Cell(row, nameEn);
            var arabic = ImportParsing.Cell(row, nameAr);
            if (english.Length == 0 && arabic.Length == 0)
                issues.Add(new(line, "product.name-required"));

            if (productCode.Length == 0)
                productCode = NextProductCode(existing);
            if (!existing.Add(productCode))
                issues.Add(new(line, "product.code-duplicate"));

            decimal Price(int column)
            {
                var text = ImportParsing.Cell(row, column);
                if (text.Length == 0)
                    return 0;
                if (ImportParsing.ParseAmount(text) is not { } value)
                {
                    issues.Add(new(line, "product.price-invalid"));
                    return 0;
                }

                if (value < 0)
                    issues.Add(new(line, "product.price-negative"));
                return value;
            }

            Guid? Account(int column)
            {
                var text = ImportParsing.Cell(row, column);
                if (text.Length == 0)
                    return null;
                if (chart.TryGetValue(text, out var account))
                    return account.Id;
                issues.Add(new(line, "product.account-unknown"));
                return null;
            }

            var salePrice = Price(sale);
            var purchasePrice = Price(purchase);
            var salesAccountId = Account(salesAccount);
            var purchaseAccountId = Account(purchaseAccount);
            Guid? taxCodeId = null;
            var taxText = ImportParsing.Cell(row, taxColumn);
            if (taxText.Length > 0)
            {
                if (codes.TryGetValue(taxText, out var tax))
                    taxCodeId = tax.Id;
                else
                    issues.Add(new(line, "product.tax-code-unknown"));
            }

            var stockText = ImportParsing.Cell(row, stockColumn).ToLowerInvariant();
            var isStockItem = stockText is "yes" or "y" or "true" or "1" or "نعم";
            var reorderText = ImportParsing.Cell(row, reorderColumn);
            decimal reorderLevel = 0;
            if (reorderText.Length > 0)
            {
                if (ImportParsing.ParseAmount(reorderText) is { } level)
                    reorderLevel = level;
                else
                    issues.Add(new(line, "product.reorder-invalid"));
            }

            if (issues.Count == rowIssues)
                pending.Add(new ProductInput(
                    productCode, arabic, english, Clean(ImportParsing.Cell(row, unit)), salePrice, purchasePrice, salesAccountId, purchaseAccountId, taxCodeId,
                    isStockItem, Clean(ImportParsing.Cell(row, barcodeColumn)), reorderLevel));
        }

        if (issues.Count > 0)
            return new ImportResult(0, 0, issues);
        if (pending.Count == 0)
            return Fail("import.empty");

        var created = new List<Guid>();
        try
        {
            foreach (var input in pending)
                created.Add((await products.CreateAsync(input, cancellationToken)).Id);
        }
        catch (ValidationException e)
        {
            foreach (var id in created)
                await products.DeleteAsync(id, CancellationToken.None); // nothing half-imported is left behind
            return new ImportResult(0, 0, [.. e.Issues.Select(x => new ImportIssue(0, x.Code))]);
        }

        return new ImportResult(created.Count, 0, []);
    }

    private static string NextProductCode(IEnumerable<string> existing)
    {
        var highest = existing
            .Select(c => int.TryParse(new string(c.SkipWhile(ch => !char.IsDigit(ch)).ToArray()), out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        return $"P{highest + 1:000}";
    }

    // ---------------------------------------------------------------- Opening stock

    /// <summary>
    /// Columns: product code (or barcode), warehouse code (empty for the default warehouse), quantity, cost per unit. The whole file is one
    /// opening-stock document dated the day before the books start; the products must be stock items.
    /// </summary>
    public async Task<ImportResult> ImportOpeningStockAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        if (!TryRead(fileName, content, out var rows))
            return Fail("import.unreadable");

        var header = FindHeader(rows, out var headerIndex, "product code", "product", "item code", "code", "barcode", "رمز الصنف", "الصنف", "الرمز", "الباركود");
        if (header is null)
            return Fail("opening-stock.product-column-missing");

        int productColumn = ImportParsing.FindColumn(header, "product code", "product", "item code", "code", "رمز الصنف", "الصنف", "الرمز");
        int barcodeColumn = ImportParsing.FindColumn(header, "barcode", "الباركود");
        int warehouseColumn = ImportParsing.FindColumn(header, "warehouse code", "warehouse", "رمز المستودع", "المستودع");
        int quantityColumn = ImportParsing.FindColumn(header, "quantity", "qty", "الكمية");
        int costColumn = ImportParsing.FindColumn(header, "unit cost", "cost", "cost per unit", "تكلفة الوحدة", "التكلفة");

        var productList = await products.ListAsync(cancellationToken);
        var byCode = productList.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        var byBarcode = productList.Where(p => p.Barcode is not null).ToDictionary(p => p.Barcode!, StringComparer.OrdinalIgnoreCase);
        var warehouseList = (await warehouses.ListAsync(cancellationToken)).Where(w => w.IsActive).ToList();
        var defaultWarehouse = warehouseList.FirstOrDefault(w => w.IsDefault) ?? warehouseList.FirstOrDefault();

        var pending = new List<StockLineInput>();
        var issues = new List<ImportIssue>();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;

            var rowIssues = issues.Count;
            var line = i + 1;
            var codeText = ImportParsing.Cell(row, productColumn);
            var barcodeText = ImportParsing.Cell(row, barcodeColumn);
            ProductDto? product = null;
            if (!(codeText.Length > 0 && byCode.TryGetValue(codeText, out product)) && !(barcodeText.Length > 0 && byBarcode.TryGetValue(barcodeText, out product)))
                issues.Add(new(line, "opening-stock.product-unknown"));
            else if (!product!.IsStockItem)
                issues.Add(new(line, "opening-stock.product-not-stock"));

            var warehouseText = ImportParsing.Cell(row, warehouseColumn);
            var warehouse = warehouseText.Length == 0 ? defaultWarehouse : warehouseList.FirstOrDefault(w => string.Equals(w.Code, warehouseText, StringComparison.OrdinalIgnoreCase));
            if (warehouse is null)
                issues.Add(new(line, "opening-stock.warehouse-unknown"));

            if (ImportParsing.ParseAmount(ImportParsing.Cell(row, quantityColumn)) is not > 0)
                issues.Add(new(line, "opening-stock.quantity-invalid"));

            decimal? cost = null;
            var costText = ImportParsing.Cell(row, costColumn);
            if (costText.Length > 0)
            {
                if (ImportParsing.ParseAmount(costText) is { } parsedCost and >= 0)
                    cost = parsedCost;
                else
                    issues.Add(new(line, "opening-stock.cost-invalid"));
            }

            if (issues.Count == rowIssues)
                pending.Add(new StockLineInput(product!.Id, warehouse!.Id, null, ImportParsing.ParseAmount(ImportParsing.Cell(row, quantityColumn))!.Value, cost));
        }

        if (issues.Count > 0)
            return new ImportResult(0, 0, issues);
        if (pending.Count == 0)
            return Fail("import.empty");

        var day = new DateOnly(company.FirstFiscalYear, company.FiscalYearStartMonth, 1).AddDays(-1);
        try
        {
            await stockDocuments.SaveAsync(null, new StockDocumentInput(StockDocumentKind.Opening, day, null, null, pending), cancellationToken);
        }
        catch (ValidationException e)
        {
            return new ImportResult(0, 0, [.. e.Issues.Select(x => new ImportIssue(0, x.Code))]);
        }

        return new ImportResult(pending.Count, 0, []);
    }

    // ---------------------------------------------------------------- Exchange rates

    /// <summary>Columns: currency, date, rate (what one unit of the currency is worth in the company's currency). A rate for a date that already has one replaces it.</summary>
    public async Task<ImportResult> ImportExchangeRatesAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        if (!TryRead(fileName, content, out var rows))
            return Fail("import.unreadable");

        var header = FindHeader(rows, out var headerIndex, "currency", "currency code", "العملة", "رمز العملة");
        if (header is null)
            return Fail("rates.currency-column-missing");

        int currencyColumn = ImportParsing.FindColumn(header, "currency", "currency code", "العملة", "رمز العملة");
        int dateColumn = ImportParsing.FindColumn(header, "date", "from", "التاريخ", "من");
        int rateColumn = ImportParsing.FindColumn(header, "rate", "exchange rate", "worth", "السعر", "سعر الصرف");
        var baseCode = (files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.")).BaseCurrencyCode;

        var pending = new List<CurrencyRateInput>();
        var issues = new List<ImportIssue>();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;

            var rowIssues = issues.Count;
            var currency = ImportParsing.Cell(row, currencyColumn).ToUpperInvariant();
            if (CurrencyCatalog.Find(currency) is null)
                issues.Add(new(i + 1, "rate.currency-unknown"));
            else if (currency == baseCode)
                issues.Add(new(i + 1, "rate.currency-is-base"));

            var date = ImportParsing.ParseDate(ImportParsing.Cell(row, dateColumn));
            if (date is null)
                issues.Add(new(i + 1, "rate.date-required"));

            var rate = ImportParsing.ParseAmount(ImportParsing.Cell(row, rateColumn));
            if (rate is not > 0)
                issues.Add(new(i + 1, "rate.rate-invalid"));

            if (issues.Count == rowIssues)
                pending.Add(new CurrencyRateInput(currency, date!.Value, rate!.Value));
        }

        if (issues.Count > 0)
            return new ImportResult(0, 0, issues);
        if (pending.Count == 0)
            return Fail("import.empty");

        foreach (var input in pending)
            await rates.SetAsync(input, cancellationToken);
        return new ImportResult(pending.Count, 0, []);
    }

    // ---------------------------------------------------------------- Journal entries and opening balances

    private sealed record Entry(string Key, int FirstRow, DateOnly? Date, List<(int Row, VoucherLineInput Line)> Lines);

    /// <summary>
    /// Columns: entry (the number that groups lines into one entry), date, account, debit, credit, description, customer or supplier code,
    /// cost center code. Every entry must balance. The entries are posted as journal vouchers.
    /// </summary>
    public async Task<ImportResult> ImportJournalAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        var (entries, issues) = await ReadEntriesAsync(fileName, content, singleEntry: false, cancellationToken);
        if (issues.Count > 0)
            return new ImportResult(0, 0, issues);

        var posted = new List<Guid>();
        foreach (var entry in entries)
        {
            try
            {
                var voucher = await vouchers.SaveAndPostAsync(
                    null, new VoucherInput(VoucherKind.Journal, entry.Date!.Value, null, entry.Key.Length == 0 ? null : entry.Key, null, [.. entry.Lines.Select(l => l.Line)]), cancellationToken);
                posted.Add(voucher.Id);
            }
            catch (ValidationException e)
            {
                foreach (var id in posted)
                    await vouchers.DeleteAsync(id, CancellationToken.None); // all or nothing: take back the ones already posted
                return new ImportResult(0, 0, [.. e.Issues.Select(x => new ImportIssue(entry.FirstRow, x.Code))]);
            }
        }

        return new ImportResult(posted.Count, 0, []);
    }

    /// <summary>
    /// Columns: account, debit, credit (or a signed balance in debit), customer or supplier code, cost center code. The whole file is
    /// the company's one opening-balances voucher, dated the day before the books start; it must balance.
    /// </summary>
    public async Task<ImportResult> ImportOpeningBalancesAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var (entries, issues) = await ReadEntriesAsync(fileName, content, singleEntry: true, cancellationToken);
        if (issues.Count > 0)
            return new ImportResult(0, 0, issues);

        var entry = entries[0];
        var day = new DateOnly(company.FirstFiscalYear, company.FiscalYearStartMonth, 1).AddDays(-1);
        try
        {
            await vouchers.SaveAndPostAsync(null, new VoucherInput(VoucherKind.Opening, day, null, null, null, [.. entry.Lines.Select(l => l.Line)]), cancellationToken);
        }
        catch (ValidationException e)
        {
            return new ImportResult(0, 0, [.. e.Issues.Select(x => new ImportIssue(entry.Lines[0].Row, x.Code))]);
        }

        return new ImportResult(entry.Lines.Count, 0, []);
    }

    private async Task<(List<Entry> Entries, List<ImportIssue> Issues)> ReadEntriesAsync(string fileName, byte[] content, bool singleEntry, CancellationToken cancellationToken)
    {
        var entries = new List<Entry>();
        var issues = new List<ImportIssue>();
        if (!TryRead(fileName, content, out var rows))
            return (entries, [new ImportIssue(0, "import.unreadable")]);

        var header = FindHeader(rows, out var headerIndex, "account", "account code", "الحساب", "رمز الحساب");
        if (header is null)
            return (entries, [new ImportIssue(0, "journal.account-column-missing")]);

        int entryColumn = ImportParsing.FindColumn(header, "entry", "entry no", "entry number", "voucher", "journal", "number", "no", "رقم القيد", "القيد", "رقم");
        int dateColumn = ImportParsing.FindColumn(header, "date", "التاريخ");
        int accountColumn = ImportParsing.FindColumn(header, "account", "account code", "الحساب", "رمز الحساب");
        int debitColumn = ImportParsing.FindColumn(header, "debit", "مدين");
        int creditColumn = ImportParsing.FindColumn(header, "credit", "دائن");
        int descriptionColumn = ImportParsing.FindColumn(header, "description", "memo", "narration", "notes", "البيان", "الوصف", "ملاحظات");
        int partyColumn = ImportParsing.FindColumn(header, "customer", "supplier", "party", "customer / supplier", "customer or supplier", "العميل", "المورد", "العميل / المورد");
        int costCenterColumn = ImportParsing.FindColumn(header, "cost center", "project", "مركز التكلفة", "المشروع");
        if (!singleEntry && entryColumn < 0)
            return (entries, [new ImportIssue(0, "journal.entry-column-missing")]);

        var chart = (await accounts.ListAsync(cancellationToken)).Where(a => a.IsPosting).ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
        var partyList = (await parties.ListAsync(cancellationToken)).ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        var centers = (await costCenters.ListAsync(cancellationToken)).ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

        Entry? current = null;
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;

            var line = i + 1;
            var key = singleEntry ? "" : ImportParsing.Cell(row, entryColumn);
            if (current is null || (!singleEntry && key.Length > 0 && !string.Equals(key, current.Key, StringComparison.OrdinalIgnoreCase)))
            {
                current = new Entry(key, line, null, []);
                entries.Add(current);
            }

            // The entry's date is the first date given on any of its rows.
            var dateText = ImportParsing.Cell(row, dateColumn);
            if (current.Date is null && dateText.Length > 0)
            {
                if (ImportParsing.ParseDate(dateText) is { } date)
                {
                    entries[^1] = current = current with { Date = date };
                }
                else
                {
                    issues.Add(new(line, "date.invalid"));
                }
            }

            var rowIssues = issues.Count;
            if (!chart.TryGetValue(ImportParsing.Cell(row, accountColumn), out var account))
                issues.Add(new(line, "line.account-unknown"));

            decimal Amount(int column)
            {
                var text = ImportParsing.Cell(row, column);
                if (text.Length == 0)
                    return 0;
                if (ImportParsing.ParseAmount(text) is { } value)
                    return value;
                issues.Add(new(line, "line.amount-invalid"));
                return 0;
            }

            var debit = Amount(debitColumn);
            var credit = Amount(creditColumn);
            if (debit < 0 || credit < 0)
                issues.Add(new(line, "line.amount-negative"));
            else if (debit > 0 && credit > 0)
                issues.Add(new(line, "line.amount-both-sides"));
            else if (debit == 0 && credit == 0)
                issues.Add(new(line, "line.amount-required"));

            Guid? partyId = null;
            var partyText = ImportParsing.Cell(row, partyColumn);
            if (partyText.Length > 0)
            {
                if (partyList.TryGetValue(partyText, out var party))
                    partyId = party.Id;
                else
                    issues.Add(new(line, "line.party-unknown"));
            }

            Guid? centerId = null;
            var centerText = ImportParsing.Cell(row, costCenterColumn);
            if (centerText.Length > 0)
            {
                if (centers.TryGetValue(centerText, out var center))
                    centerId = center.Id;
                else
                    issues.Add(new(line, "line.cost-center-unknown"));
            }

            if (issues.Count == rowIssues)
                current.Lines.Add((line, new VoucherLineInput(null, account!.Id, Clean(ImportParsing.Cell(row, descriptionColumn)), debit, credit, partyId, centerId)));
        }

        if (issues.Count == 0 && entries.Count == 0)
            issues.Add(new ImportIssue(0, "import.empty"));

        foreach (var entry in entries)
        {
            if (singleEntry && entry.Date is null)
                continue; // dated by the books' start
            if (!singleEntry && entry.Date is null)
                issues.Add(new(entry.FirstRow, "date.required"));
            if (entry.Lines.Sum(l => l.Line.Debit) != entry.Lines.Sum(l => l.Line.Credit))
                issues.Add(new(entry.FirstRow, "balance.unbalanced"));
        }

        return (entries, issues);
    }

    // ---------------------------------------------------------------- Shared

    private bool TryRead(string fileName, byte[] content, out IReadOnlyList<IReadOnlyList<string>> rows)
    {
        try
        {
            rows = reader.Read(fileName, content);
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or NotSupportedException or IOException)
        {
            rows = [];
            return false;
        }
    }

    private static IReadOnlyList<string>? FindHeader(IReadOnlyList<IReadOnlyList<string>> rows, out int index, params string[] requiredAnyOf)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (ImportParsing.FindColumn(rows[i], requiredAnyOf) >= 0)
            {
                index = i;
                return rows[i];
            }
        }

        index = -1;
        return null;
    }

    private static ImportResult Fail(string code) => new(0, 0, [new ImportIssue(0, code)]);

    private static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
