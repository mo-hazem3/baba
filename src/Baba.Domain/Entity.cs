namespace Baba.Domain;

/// <summary>Every row has a GUID v7 key (sortable by time). Human-readable numbers are separate fields.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>Belongs to one company. Desktop files hold a single company, but every table still carries the id (cloud-ready).</summary>
public interface ICompanyScoped
{
    Guid CompanyId { get; set; }
}

/// <summary>Data that is derived from other data (such as ledger entries made by posting). It is not written to the audit log row by row.</summary>
public interface INotAudited;

/// <summary>Timestamps are UTC. The user ids are opaque text so cloud accounts can use any id format later.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    string CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}
