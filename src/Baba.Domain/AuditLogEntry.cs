namespace Baba.Domain;

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
}

/// <summary>Who changed what, and when, with before/after values as JSON. Written automatically on every save.</summary>
public sealed class AuditLogEntry : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public DateTime At { get; set; }
    public string UserId { get; set; } = "";
    public AuditAction Action { get; set; }
    public string EntityName { get; set; } = "";
    public Guid EntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}
