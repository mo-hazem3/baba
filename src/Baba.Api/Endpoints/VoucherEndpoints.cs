using Baba.Application.Accounting;
using Baba.Application.Printing;
using Baba.Domain;
using Baba.Domain.Accounting;

namespace Baba.Api.Endpoints;

/// <summary>Save a voucher. <c>Id</c> is null for a new one and the voucher's id when changing it.</summary>
public sealed record SaveVoucherRequest(Guid? Id, VoucherInput Input);

/// <summary>Payment, receipt and journal vouchers: list, open, save as draft, save and post, post, delete, print.</summary>
public static class VoucherEndpoints
{
    public static void MapVoucherEndpoints(this IEndpointRouteBuilder api)
    {
        var vouchers = api.MapGroup("/vouchers").WithTags("Vouchers");

        vouchers.MapGet("/", (VoucherService service, VoucherKind? kind, VoucherStatus? status, DateOnly? from, DateOnly? to, int? limit, CancellationToken ct) =>
                service.ListAsync(new VoucherSearch(kind, status, from, to, limit ?? 2000), ct))
            .WithName("ListVouchers");

        vouchers.MapGet("/{id:guid}", async (Guid id, VoucherService service, CancellationToken ct) =>
                await service.GetAsync(id, ct) is { } voucher ? Results.Ok(voucher) : Results.NotFound())
            .Produces<VoucherDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetVoucher");

        vouchers.MapPost("/draft", (SaveVoucherRequest request, VoucherService service, CancellationToken ct) => service.SaveDraftAsync(request.Id, request.Input, ct))
            .WithName("SaveVoucherDraft");

        vouchers.MapPost("/post", (SaveVoucherRequest request, VoucherService service, CancellationToken ct) => service.SaveAndPostAsync(request.Id, request.Input, ct))
            .WithName("SaveAndPostVoucher");

        vouchers.MapPost("/{id:guid}/post", (Guid id, VoucherService service, CancellationToken ct) => service.PostAsync(id, ct))
            .WithName("PostVoucher");

        vouchers.MapDelete("/{id:guid}", async (Guid id, VoucherService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteVoucher");

        vouchers.MapGet("/{id:guid}/pdf", async (Guid id, PrintLayout? layout, DocumentPrintService printing, CancellationToken ct) =>
            {
                if (!printing.IsAvailable)
                    return Results.StatusCode(StatusCodes.Status501NotImplemented);
                return Results.File(await printing.RenderVoucherAsync(id, layout, ct), "application/pdf", "voucher.pdf");
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status501NotImplemented)
            .WithName("PrintVoucher");
    }
}
