using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Application.Printing;
using Baba.Infrastructure.Accounting;
using Baba.Infrastructure.CompanyFiles;
using Baba.Infrastructure.Printing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Baba.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the company file (one open at a time) and its database access. Needs an <c>ICurrentUser</c>.</summary>
    public static IServiceCollection AddBabaInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SqliteCompanyFiles>();
        services.AddSingleton<ICompanyFiles>(sp => sp.GetRequiredService<SqliteCompanyFiles>());
        services.AddSingleton<ICompanyDbContextFactory>(sp => sp.GetRequiredService<SqliteCompanyFiles>());
        services.AddSingleton<IPrintFonts, EmbeddedPrintFonts>();
        services.AddSingleton<IBrandingStore, BrandingStore>();
        services.AddSingleton<IReportXlsxWriter, ClosedXmlReportWriter>();
        services.AddSingleton<IAccountStore, AccountStore>();
        services.AddSingleton<IVoucherStore, VoucherStore>();
        services.AddSingleton<IPeriodStore, PeriodStore>();
        services.AddSingleton<IPartyStore, PartyStore>();
        services.AddSingleton<ICurrencyRateStore, CurrencyRateStore>();
        services.AddSingleton<Baba.Application.Trade.IDocumentStore, Baba.Infrastructure.Trade.DocumentStore>();
        services.AddSingleton<Baba.Application.Printing.IQrImageMaker, Baba.Infrastructure.Printing.QrImageMaker>();
        services.AddSingleton<Baba.Application.Importing.IImportTemplates, Baba.Infrastructure.Printing.ImportTemplates>();
        services.AddSingleton<Baba.Application.Inventory.IStockStore, Baba.Infrastructure.Inventory.StockStore>();
        services.AddSingleton<Baba.Application.Assets.IAssetStore, Baba.Infrastructure.Assets.AssetStore>();
        services.AddSingleton<Baba.Application.Payroll.IPayrollStore, Baba.Infrastructure.Payroll.PayrollStore>();
        services.AddSingleton<Baba.Application.Claims.IClaimStore, Baba.Infrastructure.Claims.ClaimStore>();
        services.AddSingleton<Baba.Application.Budgets.IBudgetStore, Baba.Infrastructure.Budgets.BudgetStore>();
        services.AddSingleton<Baba.Application.Trade.IAllocationStore, Baba.Infrastructure.Trade.AllocationStore>();
        services.AddSingleton<Baba.Application.Trade.ITaxCodeStore, Baba.Infrastructure.Trade.TaxCodeStore>();
        services.AddSingleton<Baba.Application.Trade.IRecurringStore, Baba.Infrastructure.Trade.RecurringStore>();
        services.AddSingleton<Baba.Application.Trade.IProductStore, Baba.Infrastructure.Trade.ProductStore>();
        services.AddSingleton<Baba.Application.Trade.IPriceListStore, Baba.Infrastructure.Trade.PriceListStore>();
        services.AddSingleton<Baba.Application.Banking.IReconciliationStore, ReconciliationStore>();
        services.AddSingleton<Baba.Application.Importing.ITabularReader, TabularReader>();
        services.AddSingleton<ICostCenterStore, CostCenterStore>();
        services.AddSingleton<ILedgerQuery, LedgerQuery>();
        return services;
    }
}
