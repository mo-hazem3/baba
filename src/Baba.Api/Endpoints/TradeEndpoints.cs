using Baba.Application.Accounting;
using Baba.Application.Trade;
using Baba.Domain.Trade;

namespace Baba.Api.Endpoints;

/// <summary>Save a document. <c>Id</c> is null for a new one and the document's id when changing it.</summary>
public sealed record SaveDocumentRequest(Guid? Id, DocumentInput Input);

public sealed record ConvertDocumentRequest(DocumentKind Target);

/// <summary>Sales and purchase documents, products and price lists (brief section 10.3).</summary>
public static class TradeEndpoints
{
    public static void MapTradeEndpoints(this IEndpointRouteBuilder api)
    {
        var documents = api.MapGroup("/documents").WithTags("Documents");

        documents.MapGet("/", (DocumentService service, DocumentKind? kind, DocumentStatus? status, Guid? partyId, DateOnly? from, DateOnly? to, int? limit, CancellationToken ct) =>
                service.ListAsync(new DocumentSearch(kind, status, partyId, from, to, limit ?? 2000), ct))
            .WithName("ListDocuments");

        documents.MapGet("/{id:guid}", async (Guid id, DocumentService service, CancellationToken ct) =>
                await service.GetAsync(id, ct) is { } document ? Results.Ok(document) : Results.NotFound())
            .Produces<DocumentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetDocument");

        documents.MapPost("/draft", (SaveDocumentRequest request, DocumentService service, CancellationToken ct) => service.SaveDraftAsync(request.Id, request.Input, ct))
            .WithName("SaveDocumentDraft");

        documents.MapPost("/issue", (SaveDocumentRequest request, DocumentService service, CancellationToken ct) => service.IssueAsync(request.Id, request.Input, ct))
            .WithName("IssueDocument");

        documents.MapPost("/{id:guid}/issue", (Guid id, DocumentService service, CancellationToken ct) => service.IssueSavedAsync(id, ct))
            .WithName("IssueSavedDocument");

        documents.MapPost("/{id:guid}/convert", (Guid id, ConvertDocumentRequest request, DocumentService service, CancellationToken ct) => service.ConvertAsync(id, request.Target, ct))
            .WithName("ConvertDocument");

        documents.MapDelete("/{id:guid}", async (Guid id, DocumentService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteDocument");

        var productsGroup = api.MapGroup("/products").WithTags("Products");

        productsGroup.MapGet("/", (ProductService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListProducts");
        productsGroup.MapPost("/", (ProductInput input, ProductService service, CancellationToken ct) => service.CreateAsync(input, ct))
            .WithName("CreateProduct");
        productsGroup.MapPut("/{id:guid}", (Guid id, ProductInput input, ProductService service, CancellationToken ct) => service.UpdateAsync(id, input, ct))
            .WithName("UpdateProduct");
        productsGroup.MapPost("/{id:guid}/active", (Guid id, SetActiveRequest request, ProductService service, CancellationToken ct) => service.SetActiveAsync(id, request.Active, ct))
            .WithName("SetProductActive");
        productsGroup.MapDelete("/{id:guid}", async (Guid id, ProductService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteProduct");

        var lists = api.MapGroup("/price-lists").WithTags("Price lists");

        lists.MapGet("/", (PriceListService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListPriceLists");
        lists.MapPost("/", (PriceListInput input, PriceListService service, CancellationToken ct) => service.CreateAsync(input, ct))
            .WithName("CreatePriceList");
        lists.MapPut("/{id:guid}", (Guid id, PriceListInput input, PriceListService service, CancellationToken ct) => service.UpdateAsync(id, input, ct))
            .WithName("UpdatePriceList");
        lists.MapPost("/{id:guid}/active", (Guid id, SetActiveRequest request, PriceListService service, CancellationToken ct) => service.SetActiveAsync(id, request.Active, ct))
            .WithName("SetPriceListActive");
        lists.MapDelete("/{id:guid}", async (Guid id, PriceListService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeletePriceList");

        api.MapGet("/pricing/price", (PricingService service, Guid productId, Guid? partyId, string? currency, decimal? rate, bool? sale, CancellationToken ct) =>
                service.PriceAsync(productId, partyId, currency, rate ?? 1m, sale ?? true, ct))
            .WithTags("Products")
            .WithName("GetProductPrice");
    }
}
