using Baba.Application;
using Baba.Application.Accounting;
using Baba.Application.Printing;
using Baba.Application.Reporting;
using Baba.Application.Trade;
using Baba.Domain.Inventory;
using Baba.Domain.Trade;
using Baba.Domain;
using Baba.Domain.Accounting;

namespace Baba.Api.Endpoints;

/// <summary>The inputs a report may take. Each report uses the ones it needs: date range, "as of" date, account, comparison.</summary>
public sealed record ReportQuery(
    DateOnly? From, DateOnly? To, DateOnly? AsOf, Guid? AccountId, Comparison Comparison = Comparison.None,
    VoucherKind? Kind = null, VoucherStatus? Status = null, Guid? PartyId = null, Guid? CostCenterId = null,
    DocumentKind? DocumentKind = null, DocumentStatus? DocumentStatus = null, PartyKind? PartyKind = null,
    Guid? ProductId = null, Guid? WarehouseId = null, StockDocumentKind? StockDocumentKind = null);

/// <summary>
/// The reports (trial balance, profit and loss, balance sheet, statement of account, general ledger, journal) and the two
/// lists (chart of accounts, vouchers), on screen or exported as PDF, Excel or CSV.
/// </summary>
public static class ReportEndpoints
{
    public static readonly string[] Keys =
    [
        "trial-balance", "profit-and-loss", "balance-sheet", "statement-of-account", "general-ledger", "journal", "chart-of-accounts", "vouchers",
        "party-statement", "aging-receivable", "aging-payable", "cost-centers", "tax-return",
        "documents", "products", "parties", "tax-codes", "recurring", "exchange-rates",
        "stock-valuation", "stock-movements", "stock-reorder", "warehouses", "stock-documents",
        "asset-register",
    ];

    public static void MapReportEndpoints(this IEndpointRouteBuilder api)
    {
        var reports = api.MapGroup("/reports").WithTags("Reports");

        reports.MapGet("/{key}", async (string key, [AsParameters] ReportQuery query, ReportService service, ListingService listings, TimeProvider clock, CancellationToken ct) =>
                await BuildAsync(key, query, service, listings, clock, ct) is { } report ? Results.Ok(report) : Results.NotFound())
            .Produces<ReportResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetReport");

        reports.MapGet("/{key}/export", async (
                string key, ExportFormat format, PrintLayout? layout, [AsParameters] ReportQuery query,
                ReportService service, ListingService listings, ExportService exports, DocumentPrintService printing, TimeProvider clock, CancellationToken ct) =>
            {
                if (format == ExportFormat.Pdf && !printing.IsAvailable)
                    return Results.StatusCode(StatusCodes.Status501NotImplemented);
                if (await BuildAsync(key, query, service, listings, clock, ct) is not { } report)
                    return Results.NotFound();

                var file = await exports.ExportAsync(report, format, layout, ct);
                return Results.File(file.Content, file.ContentType, file.FileName);
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status501NotImplemented)
            .WithName("ExportReport");
    }

    private static async Task<ReportResult?> BuildAsync(
        string key, ReportQuery q, ReportService reports, ListingService listings, TimeProvider clock, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        switch (key)
        {
            case "trial-balance": return await reports.TrialBalanceAsync(q.From, q.To, ct);
            case "profit-and-loss":
                return q.CostCenterId is { } costCenterId
                    ? await reports.ProfitAndLossAsync(costCenterId, q.From, q.To, q.Comparison, ct)
                    : await reports.ProfitAndLossAsync(q.From, q.To, q.Comparison, ct);
            case "balance-sheet": return await reports.BalanceSheetAsync(q.AsOf ?? q.To ?? today, q.Comparison, ct);
            case "general-ledger": return await reports.GeneralLedgerAsync(q.From, q.To, ct);
            case "journal": return await reports.JournalAsync(q.From, q.To, ct);
            case "chart-of-accounts": return await listings.ChartOfAccountsAsync(ct);
            case "vouchers": return await listings.VouchersAsync(new VoucherSearch(q.Kind, q.Status, q.From, q.To), ct);
            case "documents": return await listings.DocumentsAsync(new DocumentSearch(q.DocumentKind, q.DocumentStatus, q.PartyId, q.From, q.To), ct);
            case "products": return await listings.ProductsAsync(ct);
            case "parties": return await listings.PartiesAsync(q.PartyKind, ct);
            case "tax-codes": return await listings.TaxCodesAsync(ct);
            case "recurring": return await listings.RecurringAsync(ct);
            case "exchange-rates": return await listings.ExchangeRatesAsync(ct);
            case "stock-valuation": return await listings.StockValuationAsync(q.AsOf ?? q.To ?? today, q.WarehouseId, ct);
            case "stock-movements": return await listings.StockMovementsAsync(q.From, q.To, q.ProductId, q.WarehouseId, ct);
            case "stock-reorder": return await listings.StockReorderAsync(ct);
            case "warehouses": return await listings.WarehousesAsync(ct);
            case "asset-register": return await listings.AssetRegisterAsync(q.AsOf ?? q.To ?? today, ct);
            case "stock-documents": return await listings.StockDocumentsAsync(q.StockDocumentKind, q.From, q.To, ct);
            case "aging-receivable": return await reports.AgingAsync(PartyKind.Customer, q.AsOf ?? q.To ?? today, ct);
            case "aging-payable": return await reports.AgingAsync(PartyKind.Supplier, q.AsOf ?? q.To ?? today, ct);
            case "tax-return": return await reports.TaxReturnAsync(q.From, q.To, ct);
            case "cost-centers": return await reports.CostCenterSummaryAsync(q.From, q.To, ct);
            case "party-statement":
                if (q.PartyId is not { } partyId)
                    throw new ValidationException([new ValidationIssue("partyId", "report.party-required")]);
                return await reports.PartyStatementAsync(partyId, q.From, q.To, ct);
            case "statement-of-account":
                if (q.AccountId is not { } accountId)
                    throw new ValidationException([new ValidationIssue("accountId", "report.account-required")]);
                return await reports.StatementOfAccountAsync(accountId, q.From, q.To, ct);
            default: return null;
        }
    }
}
