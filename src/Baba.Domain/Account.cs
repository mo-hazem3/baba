namespace Baba.Domain;

/// <summary>A node in the chart of accounts tree (unlimited depth). Entries are posted to posting accounts only.</summary>
public sealed class Account : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public Guid? ParentId { get; set; }
    public AccountType Type { get; set; }
    public bool IsPosting { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
