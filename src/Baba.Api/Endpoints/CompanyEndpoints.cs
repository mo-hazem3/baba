using Baba.Api.Contracts;
using Baba.Application.Companies;

namespace Baba.Api.Endpoints;

/// <summary>Create, open, close and back up the company file, plus the start screen's recent files.</summary>
public static class CompanyEndpoints
{
    public static void MapCompanyEndpoints(this IEndpointRouteBuilder api)
    {
        var company = api.MapGroup("/company").WithTags("Company");

        // 200 with the open company, or 204 when none is open.
        company.MapGet("/", (CompanyService service) =>
                service.Current is { } info ? Results.Ok(info) : Results.NoContent())
            .WithName("GetCurrentCompany");

        company.MapPost("/create", async (CreateCompanyRequest request, CompanyService service, IRecentFiles recent, CancellationToken cancellationToken) =>
            {
                var info = await service.CreateAsync(request.Path, request.Password, request.Company, cancellationToken);
                recent.Add(info.FilePath);
                return info;
            })
            .WithName("CreateCompany");

        company.MapPost("/open", async (OpenCompanyRequest request, CompanyService service, IRecentFiles recent, CancellationToken cancellationToken) =>
            {
                var info = await service.OpenAsync(request.Path, request.Password, cancellationToken);
                recent.Add(info.FilePath);
                return info;
            })
            .WithName("OpenCompany");

        company.MapPost("/close", (CompanyService service) =>
            {
                service.Close();
                return Results.NoContent();
            })
            .WithName("CloseCompany");

        company.MapPost("/backup", async (BackupRequest request, CompanyService service, CancellationToken cancellationToken) =>
            {
                await service.BackupAsync(request.DestinationPath, cancellationToken);
                return Results.NoContent();
            })
            .WithName("BackupCompany");

        var recentFiles = api.MapGroup("/recent-files").WithTags("Recent files");

        recentFiles.MapGet("/", (IRecentFiles recent) =>
                recent.List().Select(r => new RecentFileDto(r.Path, System.IO.Path.GetFileNameWithoutExtension(r.Path), r.LastOpenedAt, File.Exists(r.Path))).ToList())
            .WithName("ListRecentFiles");

        recentFiles.MapPost("/remove", (RemoveRecentFileRequest request, IRecentFiles recent) =>
            {
                recent.Remove(request.Path);
                return Results.NoContent();
            })
            .WithName("RemoveRecentFile");
    }
}
