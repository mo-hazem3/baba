using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace Baba.Architecture.Tests;

/// <summary>
/// Brief section 8.1: no country-specific code outside <c>Baba.Localization/&lt;Country&gt;/</c>.
/// If one of these fails, do not weaken the test: extend the country pack contract instead.
/// </summary>
public class CountryRuleTests
{
    private static readonly Assembly Localization = typeof(Baba.Localization.ICountryPack).Assembly;

    /// <summary>Every sub-namespace of Baba.Localization is a country (or the template), for example Baba.Localization.Eg.</summary>
    private static readonly string[] CountryNamespaces = Localization.GetTypes()
        .Select(t => t.Namespace)
        .OfType<string>()
        .Where(ns => ns.StartsWith("Baba.Localization.", StringComparison.Ordinal))
        .Distinct()
        .ToArray();

    private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

    public static TheoryData<string> CoreAssemblies => new()
    {
        "Baba.Domain",
        "Baba.Application",
        "Baba.Infrastructure",
        "Baba.Api",
        "Baba.Desktop",
    };

    [Fact]
    public void Country_namespaces_are_found()
    {
        Assert.NotEmpty(CountryNamespaces);
        Assert.Contains("Baba.Localization.Eg", CountryNamespaces);
    }

    [Theory]
    [MemberData(nameof(CoreAssemblies))]
    public void Core_never_uses_a_specific_country_pack(string assemblyName)
    {
        var result = Types.InAssembly(Load(assemblyName))
            .ShouldNot().HaveDependencyOnAny(CountryNamespaces)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"{assemblyName} uses a country pack: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Country_packs_never_use_each_other()
    {
        foreach (var country in CountryNamespaces)
        {
            var others = CountryNamespaces.Where(ns => ns != country).ToArray();
            var result = Types.InAssembly(Localization)
                .That().ResideInNamespace(country)
                .ShouldNot().HaveDependencyOnAny(others)
                .GetResult();

            Assert.True(result.IsSuccessful,
                $"{country} uses another country: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [Fact]
    public void The_shared_localization_code_does_not_use_a_specific_country()
    {
        var result = Types.InAssembly(Localization)
            .That().ResideInNamespace("Baba.Localization")
            .And().DoNotResideInNamespaceStartingWith("Baba.Localization.")
            .ShouldNot().HaveDependencyOnAny(CountryNamespaces)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Shared localization code uses a country: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // Names and codes that must never appear in code outside a country pack.
    private static readonly Regex CountryText = new(
        @"\b(Egypt|Egyptian|Saudi|Emirates|Emirati|Kuwait|Kuwaiti|Bahrain|Bahraini|Oman|Omani|Qatar|Qatari|ZATCA|FATOORA|Zakat)\b" +
        @"|\b(EGP|SAR|AED|KWD|BHD|OMR|QAR)\b" +
        @"|[""'`](EG|SA|AE|KW|BH|OM|QA)[""'`]", // quoted country codes in C# (double quotes) and TypeScript (any quote)
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] SourceExtensions = [".cs", ".ts", ".tsx", ".css"];
    private static readonly string[] IgnoredFolders = ["bin", "obj", "node_modules", "dist", ".vs"];

    [Fact]
    public void No_country_names_or_codes_appear_in_core_source_files()
    {
        var root = FindRepositoryRoot();
        var folders = new[]
        {
            "src/Baba.Domain", "src/Baba.Application", "src/Baba.Infrastructure",
            "src/Baba.Api", "src/Baba.Desktop", "web/src",
        };

        var offenders = new List<string>();
        foreach (var folder in folders)
        {
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path))
                continue;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (!SourceExtensions.Contains(Path.GetExtension(file))
                    || relative.Split(Path.DirectorySeparatorChar).Any(IgnoredFolders.Contains))
                    continue;

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (CountryText.IsMatch(lines[i]))
                        offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Country-specific text found outside Baba.Localization:\n" + string.Join("\n", offenders));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Baba.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find Baba.slnx above the test output folder.");
    }
}
