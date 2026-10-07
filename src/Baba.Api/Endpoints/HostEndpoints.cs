using Baba.Api.Contracts;
using Baba.Application.Abstractions;

namespace Baba.Api.Endpoints;

/// <summary>Things about the app itself: what this host offers, a file to open on start, and native file dialogs (desktop only).</summary>
public static class HostEndpoints
{
    public static void MapHostEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/host", (IServiceProvider services) =>
                new HostInfo(
                    FileDialogs: services.GetService<IFileDialogs>() is not null,
                    PdfPrinting: services.GetService<Baba.Application.Printing.IPdfRenderer>() is not null,
                    Version: typeof(HostEndpoints).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"))
            .WithName("GetHostInfo").WithTags("Host");

        // The file the app was started with. Reading it clears it, so a page reload does not reopen it.
        api.MapGet("/startup", (StartupRequest startup) => new StartupInfo(startup.TakeOpenPath()))
            .WithName("GetStartup").WithTags("Host");

        api.MapPost("/dialogs/open-company-file", async (IServiceProvider services, CancellationToken cancellationToken) =>
            {
                if (services.GetService<IFileDialogs>() is not { } dialogs)
                    return Results.StatusCode(StatusCodes.Status501NotImplemented);
                return Results.Ok(new PathChoice(await dialogs.PickCompanyFileToOpenAsync(cancellationToken)));
            })
            .Produces<PathChoice>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status501NotImplemented)
            .WithName("PickCompanyFileToOpen").WithTags("Host");

        api.MapPost("/dialogs/save-company-file", async (SaveDialogRequest request, IServiceProvider services, CancellationToken cancellationToken) =>
            {
                if (services.GetService<IFileDialogs>() is not { } dialogs)
                    return Results.StatusCode(StatusCodes.Status501NotImplemented);
                return Results.Ok(new PathChoice(await dialogs.PickCompanyFileToSaveAsync(request.SuggestedFileName, cancellationToken)));
            })
            .Produces<PathChoice>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status501NotImplemented)
            .WithName("PickCompanyFileToSave").WithTags("Host");
    }
}
