using Baba.Domain;
using Baba.Application.Printing;

namespace Baba.Api.Endpoints;

public sealed record TestPageRequest(PrintLayout Layout);

/// <summary>Printing. The PDF is made by the host's renderer (the desktop app has one); other hosts answer 501.</summary>
public static class PrintEndpoints
{
    public static void MapPrintEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/print/test-page", async (TestPageRequest request, PrintService print, CancellationToken cancellationToken) =>
            {
                if (!print.IsAvailable)
                    return Results.StatusCode(StatusCodes.Status501NotImplemented);

                var pdf = await print.RenderTestPageAsync(request.Layout, cancellationToken);
                return Results.File(pdf, "application/pdf", "baba-test-page.pdf");
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status501NotImplemented)
            .WithName("PrintTestPage").WithTags("Printing");
    }
}
