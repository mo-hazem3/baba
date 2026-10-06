using Baba.Application;
using Baba.Application.Accounting;
using Baba.Application.Printing;
using Baba.Application.Reporting;
using Baba.Domain;

namespace Baba.Api.Endpoints;

/// <summary>The inputs a report may take. Each report uses the ones it needs: date range, "as of" date, account, comparison.</summary>
public sealed record ReportQuery(DateOnly? From, DateOnly? To, DateOnly? AsOf, Guid? AccountId, Comparison Comparison = Comparison.None);

/// <summary>
/// The reports (trial balance, profit and loss, balance sheet, statement of account, general ledger, journal) and the two
/// lists (chart of accounts, vouchers), on screen or exported as PDF, Excel or CSV.
/// </summary>
public static class ReportEndpoints
{
    public static readonly string[] Keys =
    [
        "trial-balance", "profit-and-loss", "balance-sheet", "statement-of-account", "general-ledger", "journal", "chart-of-accounts", "vouchers",
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
            case "profit-and-loss": return await reports.ProfitAndLossAsync(q.From, q.To, q.Comparison, ct);
            case "balance-sheet": return await reports.BalanceSheetAsync(q.AsOf ?? q.To ?? today, q.Comparison, ct);
            case "general-ledger": return await reports.GeneralLedgerAsync(q.From, q.To, ct);
            case "journal": return await reports.JournalAsync(q.From, q.To, ct);
            case "chart-of-accounts": return await listings.ChartOfAccountsAsync(ct);
            case "vouchers": return await listings.VouchersAsync(new VoucherSearch(From: q.From, To: q.To), ct);
            case "statement-of-account":
                if (q.AccountId is not { } accountId)
                    throw new ValidationException([new ValidationIssue("accountId", "report.account-required")]);
                return await reports.StatementOfAccountAsync(accountId, q.From, q.To, ct);
            default: return null;
        }
    }
}
