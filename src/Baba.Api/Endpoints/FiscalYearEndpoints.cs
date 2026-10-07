using Baba.Application.Accounting;

namespace Baba.Api.Endpoints;

/// <summary>The fiscal years of the company, closing a year at its end, and taking the closing back (brief section 10.2).</summary>
public static class FiscalYearEndpoints
{
    public static void MapFiscalYearEndpoints(this IEndpointRouteBuilder api)
    {
        var years = api.MapGroup("/fiscal-years").WithTags("Year-end");

        years.MapGet("/", (FiscalYearService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListFiscalYears");

        years.MapPost("/{year:int}/close", (int year, FiscalYearService service, CancellationToken ct) => service.CloseAsync(year, ct))
            .WithName("CloseFiscalYear");

        years.MapPost("/{year:int}/reopen", (int year, FiscalYearService service, CancellationToken ct) => service.ReopenAsync(year, ct))
            .WithName("ReopenFiscalYear");
    }
}
