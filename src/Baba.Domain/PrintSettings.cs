namespace Baba.Domain;

/// <summary>Which language(s) a printout uses. Document language is separate from screen language (brief section 6).</summary>
public enum PrintLayout
{
    Arabic,
    English,

    /// <summary>Arabic and English together on the same page (common in the Gulf, required for some e-invoices).</summary>
    Both,
}

/// <summary>Which of the two images a company can upload.</summary>
public enum BrandingImage
{
    Logo,
    Stamp,
}

/// <summary>
/// The company's print template (brief section 10.1): what appears on voucher printouts and reports. One row per company;
/// without a row, the defaults below apply.
/// </summary>
public sealed class PrintSettings : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }

    public bool ShowCompanyName { get; set; } = true;
    public bool ShowLogo { get; set; } = true;
    public bool ShowStamp { get; set; } = true;
    public bool ShowSignatures { get; set; } = true;
    public bool ShowAmountInWords { get; set; } = true;

    /// <summary>A line or two under the company name (address, phone, tax number), in each language.</summary>
    public string? HeaderTextEn { get; set; }

    public string? HeaderTextAr { get; set; }
    public string? FooterTextEn { get; set; }
    public string? FooterTextAr { get; set; }

    /// <summary>The language(s) printouts start with. Each print can still choose another.</summary>
    public PrintLayout DefaultLayout { get; set; } = PrintLayout.Both;

    /// <summary>Print numbers with Arabic-Indic digits (١٢٣) in Arabic printouts instead of 123.</summary>
    public bool ArabicIndicDigits { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
