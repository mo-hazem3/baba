using Baba.Application;
using Baba.Application.Printing;
using Baba.Domain;

namespace Baba.Api.Endpoints;

/// <summary>The company's print template and its logo and stamp pictures.</summary>
public static class BrandingEndpoints
{
    public static void MapBrandingEndpoints(this IEndpointRouteBuilder api)
    {
        var branding = api.MapGroup("/branding").WithTags("Printing");

        branding.MapGet("/", (BrandingService service, CancellationToken ct) => service.GetSettingsAsync(ct))
            .WithName("GetPrintSettings");

        branding.MapPut("/", (PrintSettingsInput input, BrandingService service, CancellationToken ct) => service.SaveSettingsAsync(input, ct))
            .WithName("SavePrintSettings");

        // The picture itself: "logo" or "stamp".
        branding.MapGet("/{image}", async (string image, BrandingService service, CancellationToken ct) =>
            {
                if (!Enum.TryParse<BrandingImage>(image, ignoreCase: true, out var kind))
                    return Results.NotFound();
                return await service.GetImageAsync(kind, ct) is { } file ? Results.File(file.Content, file.ContentType) : Results.NotFound();
            })
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetBrandingImage");

        // Send the picture's bytes as the request body (PNG or JPEG, at most 1 MB). The file name may come in the X-File-Name header.
        branding.MapPut("/{image}", async (string image, HttpRequest request, BrandingService service, CancellationToken ct) =>
            {
                if (!Enum.TryParse<BrandingImage>(image, ignoreCase: true, out var kind))
                    return Results.NotFound();

                using var buffer = new MemoryStream();
                var limit = BrandingService.MaxImageBytes + 1L; // read one byte more than allowed, to know it is too big without reading it all
                var chunk = new byte[16 * 1024];
                int read;
                while (buffer.Length < limit && (read = await request.Body.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length)), ct)) > 0)
                    buffer.Write(chunk, 0, read);

                await service.SetImageAsync(kind, request.Headers["X-File-Name"].FirstOrDefault() ?? kind.ToString(), buffer.ToArray(), ct);
                return Results.NoContent();
            })
            .WithName("SetBrandingImage");

        branding.MapDelete("/{image}", async (string image, BrandingService service, CancellationToken ct) =>
            {
                if (!Enum.TryParse<BrandingImage>(image, ignoreCase: true, out var kind))
                    return Results.NotFound();
                await service.ClearImageAsync(kind, ct);
                return Results.NoContent();
            })
            .WithName("ClearBrandingImage");
    }
}
