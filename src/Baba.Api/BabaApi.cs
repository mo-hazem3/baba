using System.Text.Json.Serialization;
using Baba.Api.Endpoints;
using Baba.Api.Errors;
using Baba.Api.Security;
using Baba.Application.Abstractions;
using Baba.Application.Accounting;
using Baba.Application.Reporting;
using Baba.Application.Companies;
using Baba.Application.Printing;
using Baba.Infrastructure;
using Baba.Infrastructure.RecentFiles;
using Baba.Localization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Baba.Api;

/// <summary>Builds the Baba web host. Used by the desktop launcher, the standalone program and the tests.</summary>
public static class BabaApi
{
    /// <param name="options">How to host it (port, token, web files).</param>
    /// <param name="configure">
    /// Runs before the defaults are added, so a host can register its own <see cref="ICurrentUser"/>,
    /// <see cref="IFileDialogs"/> or <see cref="IRecentFiles"/>, or swap the server (tests use an in-memory one).
    /// </param>
    public static WebApplication Create(BabaApiOptions options, Action<WebApplicationBuilder>? configure = null, string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = options.WebRootPath,
        });

        // Loopback only: this API is for the app on this computer.
        builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");

        var services = builder.Services;
        services.AddSingleton(options);
        services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            // Numbers must be JSON numbers (not "12" in quotes): keeps the OpenAPI types and the generated client exact.
            json.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        services.AddOpenApi();
        services.AddSingleton(CountryPackRegistry.Discover());
        services.AddSingleton<CompanyService>();
        services.AddSingleton<StartupRequest>();
        services.AddBabaInfrastructure();
        services.AddSingleton<ChartOfAccountsService>();
        services.AddSingleton<PartyService>();
        services.AddSingleton<Baba.Application.Trade.DocumentService>();
        services.AddSingleton<Baba.Application.Trade.ProductService>();
        services.AddSingleton<Baba.Application.Trade.PriceListService>();
        services.AddSingleton<Baba.Application.Trade.PricingService>();
        services.AddSingleton<Baba.Application.Trade.SettlementService>();
        services.AddSingleton<Baba.Application.Trade.RecurringService>();
        services.AddSingleton<Baba.Application.Trade.TaxService>();
        services.AddSingleton<ExchangeRateService>();
        services.AddSingleton<Baba.Application.Importing.ImportService>();
        services.AddSingleton<Baba.Application.Importing.ListImportService>();
        services.AddSingleton<FiscalYearService>();
        services.AddSingleton<Baba.Application.Banking.BankService>();
        services.AddSingleton<CostCenterService>();
        services.AddSingleton<VoucherService>();
        services.AddSingleton<PeriodService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<ListingService>();
        services.AddSingleton<DashboardService>();
        services.AddSingleton<BrandingService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton(sp => new DocumentPrintService(
            sp.GetService<IPdfRenderer>(), sp.GetRequiredService<IPrintFonts>(), sp.GetRequiredService<ICompanyFiles>(),
            sp.GetRequiredService<IBrandingStore>(), sp.GetRequiredService<VoucherService>(), sp.GetRequiredService<ChartOfAccountsService>(),
            sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton(sp => new TradePrintService(
            sp.GetService<IPdfRenderer>(), sp.GetRequiredService<IPrintFonts>(), sp.GetRequiredService<ICompanyFiles>(),
            sp.GetRequiredService<IBrandingStore>(), sp.GetRequiredService<Baba.Application.Trade.DocumentService>(), sp.GetRequiredService<PartyService>(), sp.GetRequiredService<CountryPackRegistry>(), sp.GetRequiredService<IQrImageMaker>()));

        // The PDF renderer is optional: only hosts that can make PDFs (the desktop app) register one.
        services.AddSingleton(sp => new PrintService(
            sp.GetService<IPdfRenderer>(), sp.GetRequiredService<IPrintFonts>(), sp.GetRequiredService<ICompanyFiles>(), sp.GetRequiredService<TimeProvider>()));

        configure?.Invoke(builder);

        services.TryAddSingleton<ICurrentUser, LocalUser>();
        services.TryAddSingleton<IRecentFiles>(sp => new JsonRecentFiles(
            options.RecentFilesPath ?? DefaultRecentFilesPath(),
            sp.GetRequiredService<TimeProvider>()));

        var app = builder.Build();

        app.UseMiddleware<HostFilterMiddleware>();
        app.UseMiddleware<ApiErrorMiddleware>();
        app.UseMiddleware<AccessTokenMiddleware>();

        var hasWebApp = app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists;
        if (hasWebApp)
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
        }

        var api = app.MapGroup("/api");
        api.MapHostEndpoints();
        api.MapReferenceEndpoints();
        api.MapCompanyEndpoints();
        api.MapPrintEndpoints();
        api.MapAccountEndpoints();
        api.MapVoucherEndpoints();
        api.MapPartyEndpoints();
        api.MapBankEndpoints();
        api.MapTradeEndpoints();
        api.MapExchangeRateEndpoints();
        api.MapImportEndpoints();
        api.MapFiscalYearEndpoints();
        api.MapPeriodEndpoints();
        api.MapReportEndpoints();
        api.MapBrandingEndpoints();
        api.MapDashboardEndpoints();
        app.MapOpenApi("/api/openapi/{documentName}.json");

        if (hasWebApp)
            app.MapFallbackToFile("index.html");

        return app;
    }

    private static string DefaultRecentFilesPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Baba", "recent-files.json");
}
