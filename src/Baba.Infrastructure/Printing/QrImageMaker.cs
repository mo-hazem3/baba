using Baba.Application.Printing;
using QRCoder;

namespace Baba.Infrastructure.Printing;

/// <summary>Draws QR codes as SVG, so they stay sharp on paper at any size.</summary>
public sealed class QrImageMaker : IQrImageMaker
{
    public string SvgDataUrl(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(4);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
    }
}
