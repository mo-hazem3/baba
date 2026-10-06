using Baba.Api.Contracts;
using Baba.Domain;
using Baba.Localization;

namespace Baba.Api.Endpoints;

/// <summary>Reference data for the new-company wizard: countries, currencies and modules.</summary>
public static class ReferenceEndpoints
{
    public static void MapReferenceEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/countries", (CountryPackRegistry packs) =>
                packs.All.Select(CountryDto.From).OrderBy(c => c.NameEn).ToList())
            .WithName("ListCountries").WithTags("Reference");

        api.MapGet("/currencies", () =>
                CurrencyCatalog.All.Select(c => new CurrencyDto(c.Code, c.Currency.MinorUnits, c.NameEn, c.NameAr)).ToList())
            .WithName("ListCurrencies").WithTags("Reference");

        api.MapGet("/modules", () => ModuleKeys.All.Select(k => new ModuleDto(k)).ToList())
            .WithName("ListModules").WithTags("Reference");
    }
}
