using System.Reflection;
using NetArchTest.Rules;

namespace Baba.Architecture.Tests;

/// <summary>Brief sections 3 and 4: the domain has no framework dependencies and layers only point inward.</summary>
public class LayeringTests
{
    private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

    [Fact]
    public void Domain_has_no_framework_dependencies()
    {
        var result = Types.InAssembly(Load("Baba.Domain"))
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Data.Sqlite",
                "System.Windows.Forms",
                "Baba.Application",
                "Baba.Infrastructure",
                "Baba.Api",
                "Baba.Desktop")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_does_not_depend_on_outer_layers()
    {
        var result = Types.InAssembly(Load("Baba.Application"))
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "System.Windows.Forms",
                "Baba.Infrastructure",
                "Baba.Api",
                "Baba.Desktop")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api_or_desktop()
    {
        var result = Types.InAssembly(Load("Baba.Infrastructure"))
            .ShouldNot().HaveDependencyOnAny("Baba.Api", "Baba.Desktop")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Money_is_never_stored_as_a_floating_point_number()
    {
        // Brief section 5: C# decimal everywhere in the domain, never double/float.
        var offenders = Load("Baba.Domain").GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(double) || p.PropertyType == typeof(float))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.Empty(offenders);
    }
}
