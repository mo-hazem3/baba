using Baba.Infrastructure.Persistence;

namespace Baba.Infrastructure.CompanyFiles;

/// <summary>Creates a short-lived context on the open company file. Dispose it when the unit of work is done.</summary>
public interface ICompanyDbContextFactory
{
    /// <exception cref="Baba.Application.Companies.CompanyFileException">No company is open.</exception>
    CompanyDbContext Create();
}
