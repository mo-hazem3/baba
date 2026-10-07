using System.Text;
using Baba.Localization.Sa;

namespace Baba.Localization.Tests.Sa;

/// <summary>The QR code is checked against the worked example in ZATCA's "Guide to Developed FATOORA Compliant QR Code" (Nov 2021).</summary>
public class ZatcaEInvoicingTests
{
    private readonly ZatcaEInvoicing _provider = new();

    private static EInvoiceFacts Bobs(string? nameAr = null) => new(
        "Bobs Records", nameAr ?? "", "310122393500003", new DateTimeOffset(2022, 4, 25, 15, 30, 0, TimeSpan.Zero), 1000m, 150m);

    private static string Hex(string base64) => Convert.ToHexString(Convert.FromBase64String(base64)).ToLowerInvariant();

    private static List<(int Tag, string Value)> Decode(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        var fields = new List<(int, string)>();
        for (var i = 0; i < bytes.Length;)
        {
            var tag = bytes[i];
            var length = bytes[i + 1];
            fields.Add((tag, Encoding.UTF8.GetString(bytes, i + 2, length)));
            i += 2 + length;
        }

        return fields;
    }

    [Fact]
    public void The_qr_code_matches_the_authoritys_own_example_byte_for_byte()
    {
        var qr = _provider.BuildQrCode(Bobs());

        // The hex of the five fields as printed in the guide: seller name, VAT number, time stamp, total with VAT, VAT total.
        Assert.Equal(
            "010c426f627320526563" + "6f726473"
            + "020f333130313232333933353030303033"
            + "0314323032322d30342d32355431353a33303a30305a"
            + "0407313030302e3030"
            + "05063135302e3030",
            Hex(qr!));
    }

    [Fact]
    public void A_total_is_written_with_two_decimals_and_the_time_in_utc()
    {
        var facts = Bobs() with { IssuedAt = new DateTimeOffset(2026, 10, 7, 18, 5, 9, TimeSpan.FromHours(3)), TotalWithTax = 1150.5m, TaxTotal = 150.005m };

        var fields = Decode(_provider.BuildQrCode(facts)!);

        Assert.Equal("2026-10-07T15:05:09Z", fields.Single(f => f.Tag == 3).Value);
        Assert.Equal("1150.50", fields.Single(f => f.Tag == 4).Value);
        Assert.Equal("150.01", fields.Single(f => f.Tag == 5).Value);
    }

    [Fact]
    public void An_arabic_seller_name_is_encoded_as_utf8_and_its_length_is_in_bytes()
    {
        var name = "الجواهري العربي";

        var fields = Decode(_provider.BuildQrCode(Bobs(name))!);

        Assert.Equal(name, fields.Single(f => f.Tag == 1).Value);
        var bytes = Convert.FromBase64String(_provider.BuildQrCode(Bobs(name))!);
        Assert.Equal(Encoding.UTF8.GetByteCount(name), bytes[1]); // 27 bytes for 15 characters
        Assert.NotEqual(name.Length, bytes[1]);
    }

    [Fact]
    public void Without_a_seller_name_or_tax_number_there_is_no_qr_code_and_the_saudi_pack_reports_the_capability()
    {
        Assert.Null(_provider.BuildQrCode(Bobs() with { SellerNameEn = "", SellerNameAr = "" }));
        Assert.Null(_provider.BuildQrCode(Bobs() with { SellerTaxNumber = "" }));

        var pack = new SaudiArabiaPack();
        Assert.True(pack.Capabilities.HasEInvoicing);
        Assert.True(pack.DocumentRules.SubmittedDocumentsAreImmutable);
        Assert.Equal("vat-number", pack.EInvoicing!.SellerTaxNumberKey);
        Assert.Contains(pack.TaxRegistration, r => r.Key == _provider.SellerTaxNumberKey);
    }
}
