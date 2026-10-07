using Baba.Application.Assets;

namespace Baba.Api.Endpoints;

public sealed record RunDepreciationRequest(DateOnly Through);

/// <summary>The fixed and intangible asset register, depreciation runs and disposals (brief section 10.4).</summary>
public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this IEndpointRouteBuilder api)
    {
        var assets = api.MapGroup("/assets").WithTags("Assets");

        assets.MapGet("/", (AssetService service, CancellationToken ct) => service.ListAsync(ct)).WithName("ListAssets");
        assets.MapGet("/{id:guid}", async (Guid id, AssetService service, CancellationToken ct) =>
                await service.GetAsync(id, ct) is { } asset ? Results.Ok(asset) : Results.NotFound())
            .Produces<AssetDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetAsset");
        assets.MapPost("/", (AssetInput input, AssetService service, CancellationToken ct) => service.CreateAsync(input, ct)).WithName("CreateAsset");
        assets.MapPut("/{id:guid}", (Guid id, AssetInput input, AssetService service, CancellationToken ct) => service.UpdateAsync(id, input, ct)).WithName("UpdateAsset");
        assets.MapDelete("/{id:guid}", async (Guid id, AssetService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteAsset");

        assets.MapPost("/{id:guid}/dispose", (Guid id, DisposeInput input, AssetService service, CancellationToken ct) => service.DisposeAsync(id, input, ct)).WithName("DisposeAsset");
        assets.MapPost("/{id:guid}/undispose", (Guid id, AssetService service, CancellationToken ct) => service.UndoDisposalAsync(id, ct)).WithName("UndoAssetDisposal");

        assets.MapPost("/depreciation/run", (RunDepreciationRequest request, AssetService service, CancellationToken ct) => service.RunDepreciationAsync(request.Through, ct)).WithName("RunDepreciation");
        assets.MapPost("/depreciation/run-due", (AssetService service, CancellationToken ct) => service.RunDueAsync(ct)).WithName("RunDueDepreciation");
        assets.MapPost("/depreciation/undo-last", async (AssetService service, CancellationToken ct) =>
            {
                await service.UndoLastDepreciationAsync(ct);
                return Results.NoContent();
            })
            .WithName("UndoLastDepreciation");
    }
}
