using Baba.Application.Budgets;
using Baba.Application.Claims;

namespace Baba.Api.Endpoints;

public sealed record SaveClaimRequest(Guid? Id, ClaimInput Input);

public sealed record RejectClaimRequest(string? Reason);

/// <summary>Expense claims and budgets (brief section 10.4).</summary>
public static class ClaimEndpoints
{
    public static void MapClaimEndpoints(this IEndpointRouteBuilder api)
    {
        var claims = api.MapGroup("/claims").WithTags("Expense claims");

        claims.MapGet("/", (ClaimService service, CancellationToken ct) => service.ListAsync(ct)).WithName("ListClaims");
        claims.MapGet("/{id:guid}", async (Guid id, ClaimService service, CancellationToken ct) =>
                await service.GetAsync(id, ct) is { } claim ? Results.Ok(claim) : Results.NotFound())
            .Produces<ClaimDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetClaim");
        claims.MapPost("/", (SaveClaimRequest request, ClaimService service, CancellationToken ct) => service.SaveAsync(request.Id, request.Input, ct)).WithName("SaveClaim");
        claims.MapPost("/{id:guid}/submit", (Guid id, ClaimService service, CancellationToken ct) => service.SubmitAsync(id, ct)).WithName("SubmitClaim");
        claims.MapPost("/{id:guid}/approve", (Guid id, ClaimService service, CancellationToken ct) => service.ApproveAsync(id, ct)).WithName("ApproveClaim");
        claims.MapPost("/{id:guid}/reject", (Guid id, RejectClaimRequest request, ClaimService service, CancellationToken ct) => service.RejectAsync(id, request.Reason, ct)).WithName("RejectClaim");
        claims.MapPost("/{id:guid}/unapprove", (Guid id, ClaimService service, CancellationToken ct) => service.UnapproveAsync(id, ct)).WithName("UnapproveClaim");
        claims.MapPost("/{id:guid}/pay", (Guid id, PayClaimInput input, ClaimService service, CancellationToken ct) => service.PayAsync(id, input, ct)).WithName("PayClaim");
        claims.MapPost("/{id:guid}/unpay", (Guid id, ClaimService service, CancellationToken ct) => service.UnpayAsync(id, ct)).WithName("UnpayClaim");
        claims.MapDelete("/{id:guid}", async (Guid id, ClaimService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteClaim");
    }

    public static void MapBudgetEndpoints(this IEndpointRouteBuilder api)
    {
        var budgets = api.MapGroup("/budgets").WithTags("Budgets");

        budgets.MapGet("/", (BudgetService service, int fiscalYear, Guid? costCenterId, CancellationToken ct) => service.GetAsync(fiscalYear, costCenterId, ct)).WithName("GetBudget");
        budgets.MapGet("/years", (BudgetService service, CancellationToken ct) => service.YearsAsync(ct)).WithName("ListBudgetYears");
        budgets.MapPut("/", (BudgetInput input, BudgetService service, CancellationToken ct) => service.SaveAsync(input, ct)).WithName("SaveBudget");
        budgets.MapPost("/copy", (CopyBudgetInput input, BudgetService service, CancellationToken ct) => service.CopyAsync(input, ct)).WithName("CopyBudget");
    }
}
