using Baba.Application.Accounting;

namespace Baba.Api.Endpoints;

/// <summary>The exchange-rate table: list, set the rate of a currency for a date, delete, and look up the rate on a date.</summary>
public static class ExchangeRateEndpoints
{
    public static void MapExchangeRateEndpoints(this IEndpointRouteBuilder api)
    {
        var rates = api.MapGroup("/exchange-rates").WithTags("Exchange rates");

        rates.MapGet("/", (ExchangeRateService service, string? currency, CancellationToken ct) => service.ListAsync(currency, ct))
            .WithName("ListExchangeRates");

        rates.MapPut("/", (CurrencyRateInput input, ExchangeRateService service, CancellationToken ct) => service.SetAsync(input, ct))
            .WithName("SetExchangeRate");

        rates.MapDelete("/{id:guid}", async (Guid id, ExchangeRateService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteExchangeRate");

        rates.MapGet("/on", (ExchangeRateService service, string currency, DateOnly date, CancellationToken ct) => service.RateOnAsync(currency, date, ct))
            .WithName("GetExchangeRateOn");
    }
}
