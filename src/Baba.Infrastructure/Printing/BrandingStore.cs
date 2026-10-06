using Baba.Application.Printing;
using Baba.Domain;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Printing;

/// <summary>Keeps the logo and stamp inside the company file (a <see cref="StoredFile"/> each) and the print template in its own row.</summary>
public sealed class BrandingStore(ICompanyDbContextFactory contexts) : IBrandingStore
{
    public async Task<BrandingFile?> GetImageAsync(BrandingImage image, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var company = await context.Companies.AsNoTracking().SingleAsync(cancellationToken);
        var fileId = image == BrandingImage.Logo ? company.LogoFileId : company.StampFileId;
        if (fileId is null)
            return null;

        var file = await context.Files.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        return file is null ? null : new BrandingFile(file.Name, file.ContentType, file.Content);
    }

    public async Task SetImageAsync(BrandingImage image, BrandingFile file, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var company = await context.Companies.SingleAsync(cancellationToken);
        var oldId = image == BrandingImage.Logo ? company.LogoFileId : company.StampFileId;

        var stored = new StoredFile { Name = file.Name, ContentType = file.ContentType, Content = file.Content };
        context.Files.Add(stored);
        if (image == BrandingImage.Logo) company.LogoFileId = stored.Id; else company.StampFileId = stored.Id;

        // The picture it replaces is removed, so the company file does not collect old logos.
        if (oldId is { } id && await context.Files.SingleOrDefaultAsync(f => f.Id == id, cancellationToken) is { } old)
            context.Files.Remove(old);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ClearImageAsync(BrandingImage image, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var company = await context.Companies.SingleAsync(cancellationToken);
        var oldId = image == BrandingImage.Logo ? company.LogoFileId : company.StampFileId;
        if (oldId is null)
            return;

        if (image == BrandingImage.Logo) company.LogoFileId = null; else company.StampFileId = null;
        if (await context.Files.SingleOrDefaultAsync(f => f.Id == oldId, cancellationToken) is { } old)
            context.Files.Remove(old);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<PrintSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.PrintSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new PrintSettings();
    }

    public async Task SaveSettingsAsync(PrintSettings settings, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.PrintSettings.SingleOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            context.PrintSettings.Add(settings);
        }
        else
        {
            context.Entry(stored).CurrentValues.SetValues(settings);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
