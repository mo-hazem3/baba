namespace Baba.Infrastructure.Persistence;

/// <summary>Holds the id of the company in the open file. Every query is filtered by it (cloud-ready: every table has a CompanyId).</summary>
public sealed class CompanyScope
{
    public Guid CompanyId { get; set; }
}
