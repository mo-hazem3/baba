using Baba.Domain;

namespace Baba.Application.Printing;

/// <summary>An uploaded logo or stamp image.</summary>
public sealed record BrandingFile(string Name, string ContentType, byte[] Content);

/// <summary>The company's logo and stamp images and its print template. Implemented by Infrastructure.</summary>
public interface IBrandingStore
{
    Task<BrandingFile?> GetImageAsync(BrandingImage image, CancellationToken cancellationToken = default);
    Task SetImageAsync(BrandingImage image, BrandingFile file, CancellationToken cancellationToken = default);
    Task ClearImageAsync(BrandingImage image, CancellationToken cancellationToken = default);

    /// <summary>The company's print template, or the defaults when it has never been saved.</summary>
    Task<PrintSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(PrintSettings settings, CancellationToken cancellationToken = default);
}

public sealed record PrintSettingsInput(
    bool ShowCompanyName,
    bool ShowLogo,
    bool ShowStamp,
    bool ShowSignatures,
    bool ShowAmountInWords,
    string? HeaderTextEn,
    string? HeaderTextAr,
    string? FooterTextEn,
    string? FooterTextAr,
    PrintLayout DefaultLayout,
    bool ArabicIndicDigits);

public sealed record PrintSettingsDto(
    bool ShowCompanyName,
    bool ShowLogo,
    bool ShowStamp,
    bool ShowSignatures,
    bool ShowAmountInWords,
    string? HeaderTextEn,
    string? HeaderTextAr,
    string? FooterTextEn,
    string? FooterTextAr,
    PrintLayout DefaultLayout,
    bool ArabicIndicDigits,
    bool HasLogo,
    bool HasStamp);

/// <summary>Logo, stamp and the print template (brief section 10.1): what appears on voucher printouts and reports.</summary>
public sealed class BrandingService(IBrandingStore store)
{
    /// <summary>Images stay small: they are stored inside the company file and written into every printout.</summary>
    public const int MaxImageBytes = 1024 * 1024;

    private static readonly Dictionary<string, byte[]> Signatures = new()
    {
        ["image/png"] = [0x89, 0x50, 0x4E, 0x47],
        ["image/jpeg"] = [0xFF, 0xD8, 0xFF],
    };

    public Task<BrandingFile?> GetImageAsync(BrandingImage image, CancellationToken cancellationToken = default) =>
        store.GetImageAsync(image, cancellationToken);

    /// <summary>Saves a PNG or JPEG of at most 1 MB. The file's own bytes are checked, not just the name it came with.</summary>
    public async Task SetImageAsync(BrandingImage image, string name, byte[] content, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();
        var contentType = DetectContentType(content);
        if (content.Length == 0)
            issues.Add(new("image", "image.required"));
        else if (content.Length > MaxImageBytes)
            issues.Add(new("image", "image.too-large"));
        else if (contentType is null)
            issues.Add(new("image", "image.unsupported-type")); // only real PNG and JPEG pictures (an SVG could carry scripts)
        if (issues.Count > 0)
            throw new ValidationException(issues);

        var safeName = Path.GetFileName(string.IsNullOrWhiteSpace(name) ? image.ToString() : name);
        await store.SetImageAsync(image, new BrandingFile(safeName, contentType!, content), cancellationToken);
    }

    public Task ClearImageAsync(BrandingImage image, CancellationToken cancellationToken = default) =>
        store.ClearImageAsync(image, cancellationToken);

    public async Task<PrintSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await store.GetSettingsAsync(cancellationToken);
        return ToDto(settings,
            hasLogo: await store.GetImageAsync(BrandingImage.Logo, cancellationToken) is not null,
            hasStamp: await store.GetImageAsync(BrandingImage.Stamp, cancellationToken) is not null);
    }

    public async Task<PrintSettingsDto> SaveSettingsAsync(PrintSettingsInput input, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();
        if (!Enum.IsDefined(input.DefaultLayout))
            issues.Add(new("defaultLayout", "print.layout-unknown"));
        foreach (var (field, text) in new[] { ("headerTextEn", input.HeaderTextEn), ("headerTextAr", input.HeaderTextAr), ("footerTextEn", input.FooterTextEn), ("footerTextAr", input.FooterTextAr) })
        {
            if (text is { Length: > 500 })
                issues.Add(new(field, "print.text-too-long"));
        }
        if (issues.Count > 0)
            throw new ValidationException(issues);

        var settings = await store.GetSettingsAsync(cancellationToken);
        settings.ShowCompanyName = input.ShowCompanyName;
        settings.ShowLogo = input.ShowLogo;
        settings.ShowStamp = input.ShowStamp;
        settings.ShowSignatures = input.ShowSignatures;
        settings.ShowAmountInWords = input.ShowAmountInWords;
        settings.HeaderTextEn = Clean(input.HeaderTextEn);
        settings.HeaderTextAr = Clean(input.HeaderTextAr);
        settings.FooterTextEn = Clean(input.FooterTextEn);
        settings.FooterTextAr = Clean(input.FooterTextAr);
        settings.DefaultLayout = input.DefaultLayout;
        settings.ArabicIndicDigits = input.ArabicIndicDigits;
        await store.SaveSettingsAsync(settings, cancellationToken);

        return await GetSettingsAsync(cancellationToken);
    }

    /// <summary>"image/png" or "image/jpeg" if the bytes really are one, otherwise null.</summary>
    public static string? DetectContentType(byte[] content) =>
        Signatures.FirstOrDefault(s => content.Length > s.Value.Length && content.AsSpan(0, s.Value.Length).SequenceEqual(s.Value)).Key;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static PrintSettingsDto ToDto(PrintSettings s, bool hasLogo, bool hasStamp) => new(
        s.ShowCompanyName, s.ShowLogo, s.ShowStamp, s.ShowSignatures, s.ShowAmountInWords,
        s.HeaderTextEn, s.HeaderTextAr, s.FooterTextEn, s.FooterTextAr, s.DefaultLayout, s.ArabicIndicDigits, hasLogo, hasStamp);
}
