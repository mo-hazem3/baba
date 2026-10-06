namespace Baba.Domain;

/// <summary>A logo, stamp, signature or attachment. On desktop the bytes live inside the company file itself.</summary>
public sealed class StoredFile : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
