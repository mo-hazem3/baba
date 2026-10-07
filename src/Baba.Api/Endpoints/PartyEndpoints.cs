using Baba.Application.Accounting;
using Baba.Domain.Accounting;

namespace Baba.Api.Endpoints;

/// <summary>Customers and suppliers, and cost centers: list, add, edit, switch off, delete.</summary>
public static class PartyEndpoints
{
    public static void MapPartyEndpoints(this IEndpointRouteBuilder api)
    {
        var parties = api.MapGroup("/parties").WithTags("Customers and suppliers");

        parties.MapGet("/", (PartyService service, PartyKind? kind, CancellationToken ct) => service.ListAsync(kind, ct))
            .WithName("ListParties");

        parties.MapPost("/", (PartyInput input, PartyService service, CancellationToken ct) => service.CreateAsync(input, ct))
            .WithName("CreateParty");

        parties.MapPut("/{id:guid}", (Guid id, PartyInput input, PartyService service, CancellationToken ct) => service.UpdateAsync(id, input, ct))
            .WithName("UpdateParty");

        parties.MapPost("/{id:guid}/active", (Guid id, SetActiveRequest request, PartyService service, CancellationToken ct) =>
                service.SetActiveAsync(id, request.Active, ct))
            .WithName("SetPartyActive");

        parties.MapDelete("/{id:guid}", async (Guid id, PartyService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteParty");

        var costCenters = api.MapGroup("/cost-centers").WithTags("Cost centers");

        costCenters.MapGet("/", (CostCenterService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListCostCenters");

        costCenters.MapPost("/", (CostCenterInput input, CostCenterService service, CancellationToken ct) => service.CreateAsync(input, ct))
            .WithName("CreateCostCenter");

        costCenters.MapPut("/{id:guid}", (Guid id, CostCenterInput input, CostCenterService service, CancellationToken ct) => service.UpdateAsync(id, input, ct))
            .WithName("UpdateCostCenter");

        costCenters.MapPost("/{id:guid}/active", (Guid id, SetActiveRequest request, CostCenterService service, CancellationToken ct) =>
                service.SetActiveAsync(id, request.Active, ct))
            .WithName("SetCostCenterActive");

        costCenters.MapDelete("/{id:guid}", async (Guid id, CostCenterService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteCostCenter");
    }
}
