using Baba.Application.Accounting;

namespace Baba.Api.Endpoints;

public sealed record SetActiveRequest(bool Active);

public sealed record MoveEntriesRequest(Guid TargetAccountId);

public sealed record MoveEntriesResult(int VouchersMoved);

/// <summary>The chart of accounts: list, add, edit (which includes moving in the tree), deactivate, delete, and move entries.</summary>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder api)
    {
        var accounts = api.MapGroup("/accounts").WithTags("Chart of accounts");

        accounts.MapGet("/", (ChartOfAccountsService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListAccounts");

        accounts.MapPost("/", (AccountInput input, ChartOfAccountsService service, CancellationToken ct) => service.CreateAsync(input, ct))
            .WithName("CreateAccount");

        accounts.MapPut("/{id:guid}", (Guid id, AccountInput input, ChartOfAccountsService service, CancellationToken ct) => service.UpdateAsync(id, input, ct))
            .WithName("UpdateAccount");

        accounts.MapPost("/{id:guid}/active", (Guid id, SetActiveRequest request, ChartOfAccountsService service, CancellationToken ct) =>
                service.SetActiveAsync(id, request.Active, ct))
            .WithName("SetAccountActive");

        accounts.MapDelete("/{id:guid}", async (Guid id, ChartOfAccountsService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteAccount");

        accounts.MapPost("/{id:guid}/move-entries", async (Guid id, MoveEntriesRequest request, ChartOfAccountsService service, CancellationToken ct) =>
                new MoveEntriesResult(await service.MoveEntriesAsync(id, request.TargetAccountId, ct)))
            .WithName("MoveAccountEntries");
    }
}
