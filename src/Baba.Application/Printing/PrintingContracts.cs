namespace Baba.Application.Printing;

public sealed record PdfOptions(double MarginMillimeters = 14, bool Landscape = false);

/// <summary>
/// Turns an HTML page into a PDF. Desktop uses the WebView2 browser engine; the cloud edition will use headless
/// Chromium behind the same interface (docs/adr/0004-pdf-via-browser-engine.md).
/// </summary>
public interface IPdfRenderer
{
    Task<byte[]> RenderAsync(string html, PdfOptions options, CancellationToken cancellationToken = default);
}

/// <summary>The @font-face rules (with the font data inside) every printout needs, so a PDF never depends on installed fonts.</summary>
public interface IPrintFonts
{
    string CssFontFaces();

    /// <summary>The font-family list to use in print CSS, Arabic font first.</summary>
    string FontFamily { get; }
}
