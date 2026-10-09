using Baba.Application;
using Baba.Application.Accounting;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Infrastructure.Accounting;
using Baba.Infrastructure.CompanyFiles;
using Baba.Infrastructure.Tests.CompanyFiles;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Stands in for the PDF engine: remembers the page it was asked to print and returns a tiny fake PDF.</summary>
public sealed class CapturingRenderer : Baba.Application.Printing.IPdfRenderer
{
    public string? Html { get; private set; }
    public Baba.Application.Printing.PdfOptions? Options { get; private set; }

    public Task<byte[]> RenderAsync(string html, Baba.Application.Printing.PdfOptions options, CancellationToken cancellationToken = default)
    {
        (Html, Options) = (html, options);
        return Task.FromResult(System.Text.Encoding.ASCII.GetBytes("%PDF-fake"));
    }
}

/// <summary>A real encrypted company with the default chart, and the real accounting services running on it.</summary>
public abstract class AccountingFixture : CompanyFilesFixture
{
    protected sealed record Env(
        SqliteCompanyFiles Files,
        ChartOfAccountsService Chart,
        VoucherService Vouchers,
        PeriodService Periods,
        ILedgerQuery Ledger,
        Baba.Application.Reporting.ReportService Reports,
        IReadOnlyDictionary<string, AccountDto> ByCode,
        Baba.Application.Printing.BrandingService Branding,
        Baba.Application.Reporting.ListingService Listings,
        Baba.Application.Reporting.DashboardService Dashboard,
        Baba.Application.Printing.DocumentPrintService Documents,
        Baba.Application.Printing.ExportService Exports,
        CapturingRenderer Renderer,
        PartyService Parties,
        CostCenterService CostCenters,
        Guid Customer,
        Guid Supplier,
        Baba.Application.Banking.BankService Bank,
        FiscalYearService Years,
        ExchangeRateService Rates,
        Baba.Application.Trade.DocumentService Trade,
        Baba.Application.Trade.ProductService Products,
        Baba.Application.Trade.PriceListService PriceLists,
        Baba.Application.Trade.PricingService Pricing,
        Baba.Application.Trade.SettlementService Settlements,
        Baba.Application.Trade.RecurringService Recurring,
        Baba.Application.Printing.TradePrintService TradePrint,
        Baba.Application.Trade.TaxService Tax,
        Baba.Application.Inventory.WarehouseService Warehouses,
        Baba.Application.Inventory.StockService Stock,
        Baba.Application.Inventory.StockDocumentService StockDocs,
        Baba.Application.Assets.AssetService FixedAssets,
        Baba.Application.Payroll.EmployeeService Employees,
        Baba.Application.Payroll.PayrollService Payroll,
        Baba.Application.Claims.ClaimService Claims,
        Baba.Application.Budgets.BudgetService Budgets,
        Baba.Application.Security.UserService Users,
        Baba.Application.Security.AppSession Session,
        Baba.Application.Security.AccessService Access,
        Baba.Application.Security.ApprovalService Approvals,
        Baba.Application.Security.AuditService Audit)
    {
        public Guid Id(string code) => ByCode[code].Id;

        /// <summary>Lines on a receivable or payable account must name a customer or supplier; every other line has none.</summary>
        public Guid? PartyFor(string code) => ByCode[code].Role switch
        {
            AccountRole.Receivable => Customer,
            AccountRole.Payable => Supplier,
            _ => null,
        };
    }

    protected static readonly DateOnly Oct6 = new(2026, 10, 6);
    protected static readonly DateOnly Oct1 = new(2026, 10, 1);
    protected static readonly DateOnly Oct31 = new(2026, 10, 31);

    /// <summary>
    /// October 2026 (three-decimal dinars), plus one sale in October 2025 for the comparisons:
    /// capital in, a bank withdrawal, rent and utilities, a credit sale, a customer receipt, a cash sale, and salaries.
    /// </summary>
    protected async Task<Env> SeedMonthAsync()
    {
        var e = await NewEnvAsync();
        async Task Post(VoucherInput input) => await e.Vouchers.SaveAndPostAsync(null, input);

        await Post(Receipt(e, new DateOnly(2025, 10, 5), ("511", 400m)));                                     // last year: bank +400, sales 400
        await Post(Receipt(e, new DateOnly(2026, 10, 1), ("31", 10_000m)));                                   // capital: bank +10,000
        await Post(Journal(e, new DateOnly(2026, 10, 2), ("111", 2_000m, 0), ("112", 0, 2_000m)));           // cash withdrawal
        await Post(Payment(e, new DateOnly(2026, 10, 3), ("422", 750m), ("423", 120.5m)));                    // from cash: rent + utilities
        await Post(Journal(e, new DateOnly(2026, 10, 5), ("113", 3_500m, 0), ("511", 0, 3_500m)));            // credit sale
        await Post(Receipt(e, new DateOnly(2026, 10, 10), ("113", 1_200m)));                                  // customer pays into the bank
        await Post(Journal(e, new DateOnly(2026, 10, 12), ("111", 500m, 0), ("512", 0, 500m)));               // cash sale of services
        await Post(Payment(e, new DateOnly(2026, 10, 15), ("421", 1_000m)) with { CashAccountId = e.Id("112") }); // salaries from the bank
        return e;
    }

    protected async Task<Env> NewEnvAsync(string user = "accountant", string countryCode = "XX")
    {
        var files = NewManager(user);
        await files.CreateAsync(NewPath("Company-" + Guid.NewGuid().ToString("N")), Password, NewCompany(countryCode: countryCode));

        var accounts = new AccountStore(files);
        var chart = new ChartOfAccountsService(accounts);
        var partyStore = new PartyStore(files);
        var costCenterStore = new CostCenterStore(files);
        var reconciliationStore = new ReconciliationStore(files);
        var rateStore = new CurrencyRateStore(files);
        var allocationStore = new Trade.AllocationStore(files);
        var session = new Baba.Application.Security.AppSession();
        var securityStore = new Security.SecurityStore(files, files);
        var userService = new Baba.Application.Security.UserService(securityStore, files, new FakeUser(user), Clock);
        var access = new Baba.Application.Security.AccessService(session, userService, securityStore, files, files);
        var vouchers = new VoucherService(new VoucherStore(files), accounts, partyStore, costCenterStore, reconciliationStore, new PeriodStore(files), rateStore, allocationStore, access, securityStore, files, Clock);
        var periods = new PeriodService(new PeriodStore(files), files);
        var byCode = (await chart.ListAsync()).ToDictionary(a => a.Code);
        var ledger = new LedgerQuery(files);
        var reports = new Baba.Application.Reporting.ReportService(accounts, partyStore, costCenterStore, ledger, allocationStore, new Trade.DocumentStore(files), new Trade.TaxCodeStore(files), files);
        var parties = new PartyService(partyStore, ledger);
        var customer = await parties.CreateAsync(new PartyInput(PartyKind.Customer, "C001", "العميل الأول", "First Customer", null, null, null, null, 0, 30, null, null));
        var supplier = await parties.CreateAsync(new PartyInput(PartyKind.Supplier, "S001", "المورد الأول", "First Supplier", null, null, null, null, 0, 30, null, null));

        var documentStore = new Trade.DocumentStore(files);
        var productStore = new Trade.ProductStore(files);
        var priceListStore = new Trade.PriceListStore(files);
        var taxStore = new Trade.TaxCodeStore(files);
        var stockStore = new Inventory.StockStore(files);
        var warehouseService = new Baba.Application.Inventory.WarehouseService(stockStore, files);
        var stockService = new Baba.Application.Inventory.StockService(stockStore, productStore, accounts, documentStore, vouchers, files);
        var stockDocuments = new Baba.Application.Inventory.StockDocumentService(stockStore, productStore, accounts, stockService, files, Clock);
        var assetService = new Baba.Application.Assets.AssetService(new Assets.AssetStore(files), accounts, costCenterStore, vouchers, files, Clock);
        var payrollStore = new Payroll.PayrollStore(files);
        var employeeService = new Baba.Application.Payroll.EmployeeService(payrollStore, accounts, costCenterStore, files);
        var payrollService = new Baba.Application.Payroll.PayrollService(payrollStore, accounts, ledger, vouchers, Baba.Localization.CountryPackRegistry.Discover(), files, Clock);
        var claimService = new Baba.Application.Claims.ClaimService(new Claims.ClaimStore(files), payrollStore, accounts, costCenterStore, vouchers, files, Clock);
        var budgetService = new Baba.Application.Budgets.BudgetService(new Budgets.BudgetStore(files), accounts, costCenterStore, files);
        var tax = new Baba.Application.Trade.TaxService(taxStore, accounts, Baba.Localization.CountryPackRegistry.Discover(), files);
        var trade = new Baba.Application.Trade.DocumentService(documentStore, partyStore, accounts, productStore, taxStore, costCenterStore, rateStore, allocationStore, vouchers, access, securityStore, Baba.Localization.CountryPackRegistry.Discover(), stockService, warehouseService, files, Clock);
        var approvals = new Baba.Application.Security.ApprovalService(securityStore, vouchers, trade, access, new SessionOrFixedUser(session, user), files, Clock);

        var brandingStore = new Printing.BrandingStore(files);
        var renderer = new CapturingRenderer();
        var documents = new Baba.Application.Printing.DocumentPrintService(
            renderer, new Printing.EmbeddedPrintFonts(), files, brandingStore, vouchers, chart, Clock);
        var exports = new Baba.Application.Printing.ExportService(new Printing.ClosedXmlReportWriter(), documents, brandingStore);
        return new Env(
            files, chart, vouchers, periods, ledger, reports, byCode,
            new Baba.Application.Printing.BrandingService(brandingStore),
            new Baba.Application.Reporting.ListingService(chart, vouchers, trade, new Baba.Application.Trade.ProductService(productStore, documentStore, accounts, taxStore, stockStore), parties, tax, new Baba.Application.Trade.RecurringService(new Trade.RecurringStore(files), trade, vouchers, files, Clock), new ExchangeRateService(rateStore, files), stockService, warehouseService, stockDocuments, assetService, employeeService, payrollService, claimService, budgetService, costCenterStore, ledger, accounts, files, new Baba.Application.Banking.BankService(accounts, reconciliationStore, ledger, new Baba.Infrastructure.Printing.TabularReader(), Clock), new Baba.Application.Trade.PriceListService(priceListStore, productStore, files)),
            new Baba.Application.Reporting.DashboardService(accounts, ledger, new VoucherStore(files), files, Clock),
            documents, exports, renderer, parties, new CostCenterService(costCenterStore), customer.Id, supplier.Id,
            new Baba.Application.Banking.BankService(accounts, reconciliationStore, ledger, new Baba.Infrastructure.Printing.TabularReader(), Clock),
            new FiscalYearService(accounts, ledger, new VoucherStore(files), new PeriodStore(files), vouchers, files, Clock),
            new ExchangeRateService(rateStore, files),
            trade,
            new Baba.Application.Trade.ProductService(productStore, documentStore, accounts, taxStore, stockStore),
            new Baba.Application.Trade.PriceListService(priceListStore, productStore, files),
            new Baba.Application.Trade.PricingService(productStore, priceListStore, partyStore, files),
            new Baba.Application.Trade.SettlementService(documentStore, partyStore, accounts, allocationStore, vouchers, ledger, rateStore, files),
            new Baba.Application.Trade.RecurringService(new Trade.RecurringStore(files), trade, vouchers, files, Clock),
            new Baba.Application.Printing.TradePrintService(renderer, new Printing.EmbeddedPrintFonts(), files, brandingStore, trade, parties, Baba.Localization.CountryPackRegistry.Discover(), new Printing.QrImageMaker()),
            tax,
            warehouseService,
            stockService,
            stockDocuments,
            assetService,
            employeeService,
            payrollService,
            claimService,
            budgetService,
            userService, session, access, approvals, new Baba.Application.Security.AuditService(new Security.AuditStore(files), files));
    }

    /// <summary>Who is acting: the signed-in person once someone signed in, otherwise the fixed test user.</summary>
    private sealed class SessionOrFixedUser(Baba.Application.Security.AppSession session, string fallback) : Baba.Application.Abstractions.ICurrentUser
    {
        public string UserId => session.User?.UserName ?? fallback;
    }

    protected static VoucherInput Payment(Env e, DateOnly date, params (string Code, decimal Amount)[] lines) => new(
        VoucherKind.Payment, date, e.Id("111"), "CHQ-1", "payment memo",
        lines.Select(l => new VoucherLineInput(null, e.Id(l.Code), null, l.Amount, 0, e.PartyFor(l.Code))).ToList());

    protected static VoucherInput Receipt(Env e, DateOnly date, params (string Code, decimal Amount)[] lines) => new(
        VoucherKind.Receipt, date, e.Id("112"), null, "receipt memo",
        lines.Select(l => new VoucherLineInput(null, e.Id(l.Code), null, 0, l.Amount, e.PartyFor(l.Code))).ToList());

    protected static VoucherInput Journal(Env e, DateOnly date, params (string Code, decimal Debit, decimal Credit)[] lines) => new(
        VoucherKind.Journal, date, null, null, "journal memo",
        lines.Select(l => new VoucherLineInput(null, e.Id(l.Code), null, l.Debit, l.Credit, e.PartyFor(l.Code))).ToList());

    protected static async Task<ValidationException> RefusedAsync(Func<Task> action) => await Assert.ThrowsAsync<ValidationException>(action);

    protected static IEnumerable<string> Codes(ValidationException e) => e.Issues.Select(i => i.Code);

    /// <summary>The balance of an account (debits minus credits) up to a date, from the ledger.</summary>
    protected static async Task<decimal> BalanceAsync(Env e, string code, DateOnly? to = null)
    {
        var total = (await e.Ledger.TotalsAsync(null, to)).FirstOrDefault(t => t.AccountId == e.Id(code));
        return total is null ? 0m : total.Debit - total.Credit;
    }
}
