using Baba.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Baba.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c> to create migrations. It never touches a real company file.</summary>
internal sealed class DesignTimeCompanyDbContextFactory : IDesignTimeDbContextFactory<CompanyDbContext>
{
    public CompanyDbContext CreateDbContext(string[] args)
    {
        SqliteBootstrap.Ensure();
        var options = new DbContextOptionsBuilder<CompanyDbContext>()
            .UseSqlite("Data Source=design-time-only.baba")
            .Options;
        return new CompanyDbContext(options, new DesignTimeUser(), TimeProvider.System, new CompanyScope());
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public string UserId => "design-time";
    }
}
