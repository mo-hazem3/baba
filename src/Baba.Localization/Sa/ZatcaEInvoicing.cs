using System.Globalization;
using System.Text;

namespace Baba.Localization.Sa;

/// <summary>
/// The e-invoicing of Saudi Arabia (ZATCA, "Fatoora"). What is here is the QR code printed on a tax invoice, which the authority specifies
/// in "Guide to Developed FATOORA Compliant QR Code" (Nov 2021): five fields in Tag-Length-Value form, each tag and length one byte, each
/// value UTF-8, no padding, the whole thing in base64. The tags are 1 seller name, 2 VAT registration number, 3 time stamp (ISO 8601,
/// UTC), 4 invoice total with VAT, 5 VAT total.
///
/// NOT here yet: the integration phase (signed UBL 2.1 XML, the invoice hash chain, onboarding for a cryptographic stamp, and clearance
/// or reporting through ZATCA's API). It needs the authority's onboarding credentials and sandbox, which cannot be checked from here, so
/// it is not claimed to work. See docs/countries/sa.md.
/// </summary>
public sealed class ZatcaEInvoicing : IEInvoicingProvider
{
    public string NameEn => "ZATCA e-invoicing (Fatoora)";
    public string NameAr => "الفوترة الإلكترونية (فاتورة) - هيئة الزكاة والضريبة والجمارك";

    public string SellerTaxNumberKey => "vat-number";

    public string? BuildQrCode(EInvoiceFacts facts)
    {
        var seller = facts.SellerNameAr.Length > 0 ? facts.SellerNameAr : facts.SellerNameEn;
        if (seller.Length == 0 || facts.SellerTaxNumber.Length == 0)
            return null;

        var bytes = new List<byte>();
        Append(bytes, 1, seller);
        Append(bytes, 2, facts.SellerTaxNumber);
        Append(bytes, 3, facts.IssuedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        Append(bytes, 4, Money(facts.TotalWithTax));
        Append(bytes, 5, Money(facts.TaxTotal));
        return Convert.ToBase64String([.. bytes]);
    }

    private static string Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static void Append(List<byte> bytes, byte tag, string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        if (utf8.Length > byte.MaxValue)
            throw new ArgumentException($"Field {tag} is longer than 255 bytes, which the QR code format cannot hold.", nameof(value));

        bytes.Add(tag);
        bytes.Add((byte)utf8.Length);
        bytes.AddRange(utf8);
    }
}
