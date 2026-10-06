using Baba.Application.Companies;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Baba.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the company file (one open at a time) and its database access. Needs an <c>ICurrentUser</c>.</summary>
    public static IServiceCollection AddBabaInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SqliteCompanyFiles>();
        services.AddSingleton<ICompanyFiles>(sp => sp.GetRequiredService<SqliteCompanyFiles>());
        services.AddSingleton<ICompanyDbContextFactory>(sp => sp.GetRequiredService<SqliteCompanyFiles>());
        return services;
    }
}
