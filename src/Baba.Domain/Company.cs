namespace Baba.Domain;

/// <summary>The business a company file belongs to. A desktop file holds exactly one.</summary>
public sealed class Company : Entity, IAuditable
{
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";

    /// <summary>The pack code chosen at setup. The core only ever passes it back to the country pack registry.</summary>
    public string CountryCode { get; set; } = "";

    public string BaseCurrencyCode { get; set; } = "";

    /// <summary>1 to 12.</summary>
    public int FiscalYearStartMonth { get; set; } = 1;

    /// <summary>The calendar year in which the first fiscal year starts.</summary>
    public int FirstFiscalYear { get; set; }

    /// <summary>Tax and registration numbers keyed by the pack's registration rule key.</summary>
    public Dictionary<string, string> TaxNumbers { get; set; } = [];

    public string? Address { get; set; }
    public Guid? LogoFileId { get; set; }
    public Guid? StampFileId { get; set; }

    /// <summary>Optional modules switched on for this company (see <see cref="ModuleKeys"/>). Core accounting is always on.</summary>
    public List<string> EnabledModules { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
