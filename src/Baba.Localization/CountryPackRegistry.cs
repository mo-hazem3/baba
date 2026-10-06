using System.Reflection;

namespace Baba.Localization;

/// <summary>Keeps a pack out of <see cref="CountryPackRegistry"/> (used by the template pack).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ExcludeFromDiscoveryAttribute : Attribute;

/// <summary>
/// Finds every country pack in this assembly. Adding a country means adding a folder with a pack class;
/// removing one means deleting its folder. Nothing else needs to change.
/// </summary>
public sealed class CountryPackRegistry
{
    private readonly Dictionary<string, ICountryPack> _packs;

    public CountryPackRegistry(IEnumerable<ICountryPack> packs)
    {
        _packs = new Dictionary<string, ICountryPack>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        {
            if (!_packs.TryAdd(pack.Identity.Code, pack))
                throw new InvalidOperationException($"Two country packs use the code '{pack.Identity.Code}'.");
        }
    }

    public IReadOnlyCollection<ICountryPack> All => _packs.Values;

    public ICountryPack? Find(string code) => _packs.GetValueOrDefault(code);

    public ICountryPack Get(string code) =>
        Find(code) ?? throw new KeyNotFoundException($"No country pack is installed for '{code}'.");

    /// <summary>Creates the registry from every pack class found in the Localization assembly.</summary>
    public static CountryPackRegistry Discover()
    {
        var packs = typeof(ICountryPack).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(ICountryPack).IsAssignableFrom(t)
                        && t.GetCustomAttribute<ExcludeFromDiscoveryAttribute>() is null)
            .Select(t => (ICountryPack)Activator.CreateInstance(t)!);

        return new CountryPackRegistry(packs);
    }
}
