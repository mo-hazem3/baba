using System.Reflection;
using Baba.Application.Printing;

namespace Baba.Infrastructure.Printing;

/// <summary>
/// The Noto Sans Arabic font (SIL Open Font License, see docs/licenses) embedded in the app and written into every
/// printout as a data URL, so a PDF looks the same on every computer and never depends on installed fonts.
/// </summary>
public sealed class EmbeddedPrintFonts : IPrintFonts
{
    private readonly Lazy<string> _css = new(BuildCss);

    public string FontFamily => "'Noto Sans Arabic', 'Segoe UI', Tahoma, Arial, sans-serif";

    public string CssFontFaces() => _css.Value;

    private static string BuildCss()
    {
        var faces = new[] { (Weight: 400, Resource: "NotoSansArabic-400.woff2"), (Weight: 700, Resource: "NotoSansArabic-700.woff2") }
            .Select(f => $$"""
                @font-face {
                  font-family: 'Noto Sans Arabic';
                  font-weight: {{f.Weight}};
                  font-style: normal;
                  src: url(data:font/woff2;base64,{{Convert.ToBase64String(Read(f.Resource))}}) format('woff2');
                }
                """);
        return string.Join("\n", faces);
    }

    private static byte[] Read(string name)
    {
        var assembly = typeof(EmbeddedPrintFonts).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
