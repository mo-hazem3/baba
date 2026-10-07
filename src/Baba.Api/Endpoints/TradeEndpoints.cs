using Baba.Application.Accounting;
using Baba.Application.Printing;
using Baba.Application.Trade;
using Baba.Domain;
using Baba.Domain.Trade;

namespace Baba.Api.Endpoints;

/// <summary>Save a document. <c>Id</c> is null for a new one and the document's id when changing it.</summary>
public sealed record SaveDocumentRequest(Guid? Id, DocumentInput Input);

public sealed record ConvertDocumentRequest(DocumentKind Target);

/// <summary>Make a schedule from a saved document or voucher. The schedule keeps a copy of it as it is now.</summary>
public sealed record CreateRecurringRequest(Guid SourceId, RecurringInput Input);

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

        documents.MapGet("/{id:guid}/pdf", async (Guid id, PrintLayout? layout, TradePrintService printing, CancellationToken ct) =>
            {
                if (!printing.IsAvailable)
                    return Results.StatusCode(StatusCodes.Status501NotImplemented);
                return Results.File(await printing.RenderAsync(id, layout, ct), "application/pdf", "document.pdf");
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status501NotImplemented)
            .WithName("PrintDocument");

        var settlements = api.MapGroup("/settlements").WithTags("Settlements");

        settlements.MapGet("/outstanding", (SettlementService service, Guid? partyId, CancellationToken ct) => service.OutstandingAsync(partyId, ct))
            .WithName("ListOutstandingInvoices");
        settlements.MapGet("/overdue", (SettlementService service, TimeProvider clock, CancellationToken ct) =>
                service.OverdueAsync(DateOnly.FromDateTime(clock.GetLocalNow().DateTime), ct))
            .WithName("GetOverdue");
        settlements.MapPost("/", (SettlementInput input, SettlementService service, CancellationToken ct) => service.SettleAsync(input, ct))
            .WithName("SettleInvoices");

        var taxes = api.MapGroup("/tax-codes").WithTags("Tax codes");

        taxes.MapGet("/", (TaxService service, CancellationToken ct) => service.ListAsync(ct)).WithName("ListTaxCodes");
        taxes.MapPost("/", (TaxCodeInput input, TaxService service, CancellationToken ct) => service.CreateAsync(input, ct)).WithName("CreateTaxCode");
        taxes.MapPut("/{id:guid}", (Guid id, TaxCodeInput input, TaxService service, CancellationToken ct) => service.UpdateAsync(id, input, ct)).WithName("UpdateTaxCode");
        taxes.MapPost("/{id:guid}/active", (Guid id, SetActiveRequest request, TaxService service, CancellationToken ct) => service.SetActiveAsync(id, request.Active, ct)).WithName("SetTaxCodeActive");
        taxes.MapPost("/{id:guid}/default", (Guid id, TaxService service, CancellationToken ct) => service.SetDefaultAsync(id, ct)).WithName("SetDefaultTaxCode");
        taxes.MapDelete("/{id:guid}", async (Guid id, TaxService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteTaxCode");

        var recurring = api.MapGroup("/recurring").WithTags("Recurring");

        recurring.MapGet("/", (RecurringService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListRecurring");
        recurring.MapPost("/from-document", (CreateRecurringRequest request, RecurringService service, CancellationToken ct) => service.CreateFromDocumentAsync(request.SourceId, request.Input, ct))
            .WithName("CreateRecurringFromDocument");
        recurring.MapPost("/from-voucher", (CreateRecurringRequest request, RecurringService service, CancellationToken ct) => service.CreateFromVoucherAsync(request.SourceId, request.Input, ct))
            .WithName("CreateRecurringFromVoucher");
        recurring.MapPut("/{id:guid}", (Guid id, RecurringInput input, RecurringService service, CancellationToken ct) => service.UpdateAsync(id, input, ct))
            .WithName("UpdateRecurring");
        recurring.MapPost("/{id:guid}/active", (Guid id, SetActiveRequest request, RecurringService service, CancellationToken ct) => service.SetActiveAsync(id, request.Active, ct))
            .WithName("SetRecurringActive");
        recurring.MapDelete("/{id:guid}", async (Guid id, RecurringService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteRecurring");
        recurring.MapPost("/run-due", (RecurringService service, CancellationToken ct) => service.RunDueAsync(null, ct))
            .WithName("RunDueRecurring");

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
