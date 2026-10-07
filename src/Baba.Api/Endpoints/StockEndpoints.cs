using Baba.Application.Inventory;
using Baba.Domain.Inventory;

namespace Baba.Api.Endpoints;

/// <summary>Save a stock document. <c>Id</c> is null for a new one and the document's id when changing it.</summary>
public sealed record SaveStockDocumentRequest(Guid? Id, StockDocumentInput Input);

/// <summary>Warehouses, opening stock, adjustments, transfers and the stock on hand (brief section 10.4).</summary>
public static class StockEndpoints
{
    public static void MapStockEndpoints(this IEndpointRouteBuilder api)
    {
        var warehouses = api.MapGroup("/warehouses").WithTags("Warehouses");

        warehouses.MapGet("/", (WarehouseService service, CancellationToken ct) => service.ListAsync(ct)).WithName("ListWarehouses");
        warehouses.MapPost("/", (WarehouseInput input, WarehouseService service, CancellationToken ct) => service.CreateAsync(input, ct)).WithName("CreateWarehouse");
        warehouses.MapPut("/{id:guid}", (Guid id, WarehouseInput input, WarehouseService service, CancellationToken ct) => service.UpdateAsync(id, input, ct)).WithName("UpdateWarehouse");
        warehouses.MapPost("/{id:guid}/active", (Guid id, SetActiveRequest request, WarehouseService service, CancellationToken ct) => service.SetActiveAsync(id, request.Active, ct)).WithName("SetWarehouseActive");
        warehouses.MapPost("/{id:guid}/default", (Guid id, WarehouseService service, CancellationToken ct) => service.SetDefaultAsync(id, ct)).WithName("SetDefaultWarehouse");
        warehouses.MapDelete("/{id:guid}", async (Guid id, WarehouseService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteWarehouse");

        var stock = api.MapGroup("/stock").WithTags("Stock");

        stock.MapGet("/levels", (StockService service, DateOnly? asOf, CancellationToken ct) => service.LevelsAsync(asOf, ct)).WithName("GetStockLevels");

        stock.MapGet("/documents", (StockDocumentService service, StockDocumentKind? kind, CancellationToken ct) => service.ListAsync(kind, ct)).WithName("ListStockDocuments");
        stock.MapGet("/documents/{id:guid}", async (Guid id, StockDocumentService service, CancellationToken ct) =>
                await service.GetAsync(id, ct) is { } document ? Results.Ok(document) : Results.NotFound())
            .Produces<StockDocumentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetStockDocument");
        stock.MapPost("/documents", (SaveStockDocumentRequest request, StockDocumentService service, CancellationToken ct) => service.SaveAsync(request.Id, request.Input, ct)).WithName("SaveStockDocument");
        stock.MapDelete("/documents/{id:guid}", async (Guid id, StockDocumentService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteStockDocument");
    }
}
