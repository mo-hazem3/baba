using Baba.Application.Accounting;
using Baba.Application.Reporting;

namespace Baba.Api.Endpoints;

public sealed record LockPeriodRequest(DateOnly MonthStart, bool Locked);

/// <summary>Locking and unlocking months.</summary>
public static class PeriodEndpoints
{
    public static void MapPeriodEndpoints(this IEndpointRouteBuilder api)
    {
        var periods = api.MapGroup("/periods").WithTags("Periods");

        periods.MapGet("/", (int fiscalYear, PeriodService service, CancellationToken ct) => service.ListYearAsync(fiscalYear, ct))
            .WithName("ListPeriods");

        periods.MapPost("/lock", async (LockPeriodRequest request, PeriodService service, CancellationToken ct) =>
            {
                await service.SetLockedAsync(request.MonthStart, request.Locked, ct);
                return Results.NoContent();
            })
            .WithName("SetPeriodLocked");
    }
}

/// <summary>The key balances on the Summary page.</summary>
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder api) =>
        api.MapGet("/dashboard", (DashboardService service, CancellationToken ct) => service.GetAsync(ct))
            .WithName("GetDashboard").WithTags("Summary");
}
