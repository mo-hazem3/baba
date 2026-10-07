using Baba.Application.Importing;
using Baba.Domain.Accounting;

namespace Baba.Api.Endpoints;

/// <summary>
/// Importing a chart of accounts or a list of customers or suppliers from a CSV or Excel file (brief section 10.2). The file is sent as
/// the request body, with its name in the <c>X-File-Name</c> header, like the logo and stamp.
/// </summary>
public static class ImportEndpoints
{
    public static void MapImportEndpoints(this IEndpointRouteBuilder api)
    {
        var import = api.MapGroup("/import").WithTags("Import");

        import.MapPut("/accounts", async (HttpRequest request, ImportService service, CancellationToken ct) =>
                await service.ImportAccountsAsync(request.Headers["X-File-Name"].ToString(), await ReadBodyAsync(request, ct), ct))
            .Produces<ImportResult>()
            .WithName("ImportAccounts");

        import.MapPut("/parties/{kind}", async (PartyKind kind, HttpRequest request, ImportService service, CancellationToken ct) =>
                await service.ImportPartiesAsync(kind, request.Headers["X-File-Name"].ToString(), await ReadBodyAsync(request, ct), ct))
            .Produces<ImportResult>()
            .WithName("ImportParties");

        import.MapGet("/templates/{key}", (string key, IImportTemplates templates) =>
                templates.Build(key) is { } file ? Results.File(file.Content, file.ContentType, file.FileName) : Results.NotFound())
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetImportTemplate");

        import.MapPut("/products", async (HttpRequest request, ListImportService service, CancellationToken ct) =>
                await service.ImportProductsAsync(request.Headers["X-File-Name"].ToString(), await ReadBodyAsync(request, ct), ct))
            .Produces<ImportResult>()
            .WithName("ImportProducts");

        import.MapPut("/opening-stock", async (HttpRequest request, ListImportService service, CancellationToken ct) =>
                await service.ImportOpeningStockAsync(request.Headers["X-File-Name"].ToString(), await ReadBodyAsync(request, ct), ct))
            .Produces<ImportResult>()
            .WithName("ImportOpeningStock");

        import.MapPut("/exchange-rates", async (HttpRequest request, ListImportService service, CancellationToken ct) =>
                await service.ImportExchangeRatesAsync(request.Headers["X-File-Name"].ToString(), await ReadBodyAsync(request, ct), ct))
            .Produces<ImportResult>()
            .WithName("ImportExchangeRates");

        import.MapPut("/journal", async (HttpRequest request, ListImportService service, CancellationToken ct) =>
                await service.ImportJournalAsync(request.Headers["X-File-Name"].ToString(), await ReadBodyAsync(request, ct), ct))
            .Produces<ImportResult>()
            .WithName("ImportJournal");

        import.MapPut("/opening-balances", async (HttpRequest request, ListImportService service, CancellationToken ct) =>
                await service.ImportOpeningBalancesAsync(request.Headers["X-File-Name"].ToString(), await ReadBodyAsync(request, ct), ct))
            .Produces<ImportResult>()
            .WithName("ImportOpeningBalances");
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
