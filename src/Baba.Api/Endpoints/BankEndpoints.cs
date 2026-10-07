using Baba.Application.Banking;
using Baba.Application.Importing;

namespace Baba.Api.Endpoints;

/// <summary>Bank and cash accounts, bank reconciliation and bank statement import (brief section 10.2).</summary>
public static class BankEndpoints
{
    public static void MapBankEndpoints(this IEndpointRouteBuilder api)
    {
        var bank = api.MapGroup("/bank").WithTags("Bank and cash");

        bank.MapGet("/accounts", (BankService service, CancellationToken ct) => service.ListAccountsAsync(ct))
            .WithName("ListBankAccounts");

        bank.MapGet("/accounts/{accountId:guid}/reconciliation", (Guid accountId, DateOnly statementDate, BankService service, CancellationToken ct) =>
                service.GetReconciliationAsync(accountId, statementDate, ct))
            .WithName("GetReconciliation");

        bank.MapPost("/accounts/{accountId:guid}/reconciliation", (Guid accountId, CompleteReconciliationInput input, BankService service, CancellationToken ct) =>
                service.CompleteAsync(accountId, input, ct))
            .WithName("CompleteReconciliation");

        bank.MapGet("/accounts/{accountId:guid}/reconciliations", (Guid accountId, BankService service, CancellationToken ct) => service.HistoryAsync(accountId, ct))
            .WithName("ListReconciliations");

        bank.MapDelete("/accounts/{accountId:guid}/reconciliation", async (Guid accountId, BankService service, CancellationToken ct) =>
            {
                await service.UndoLastAsync(accountId, ct);
                return Results.NoContent();
            })
            .WithName("UndoLastReconciliation");

        // The file is sent as the request body (raw bytes), with its name in a header, like the logo and stamp.
        bank.MapPut("/accounts/{accountId:guid}/statement", async (Guid accountId, HttpRequest request, BankService service, CancellationToken ct) =>
            {
                using var buffer = new MemoryStream();
                await request.Body.CopyToAsync(buffer, ct);
                return await service.ImportStatementAsync(accountId, request.Headers["X-File-Name"].ToString(), buffer.ToArray(), ct);
            })
            .Produces<ImportResult>()
            .WithName("ImportBankStatement");

        bank.MapDelete("/accounts/{accountId:guid}/statement", async (Guid accountId, BankService service, CancellationToken ct) =>
                new ClearedStatement(await service.ClearStatementAsync(accountId, ct)))
            .WithName("ClearBankStatement");
    }
}

public sealed record ClearedStatement(int Removed);
