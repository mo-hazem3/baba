using System.Text.Json;
using Baba.Application.Companies;

namespace Baba.Infrastructure.RecentFiles;

/// <summary>Keeps the recent files list in a small JSON file in the user's app data. A missing or damaged file means an empty list.</summary>
public sealed class JsonRecentFiles(string filePath, TimeProvider clock, int maximum = 10) : IRecentFiles
{
    private readonly object _gate = new();

    public IReadOnlyList<RecentFile> List()
    {
        lock (_gate)
            return Load();
    }

    public void Add(string path)
    {
        path = Path.GetFullPath(path);
        lock (_gate)
        {
            var items = Load().Where(r => !SamePath(r.Path, path)).ToList();
            items.Insert(0, new RecentFile(path, clock.GetUtcNow().UtcDateTime));
            Save(items.Take(maximum).ToList());
        }
    }

    public void Remove(string path)
    {
        path = Path.GetFullPath(path);
        lock (_gate)
            Save(Load().Where(r => !SamePath(r.Path, path)).ToList());
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private List<RecentFile> Load()
    {
        try
        {
            if (!File.Exists(filePath))
                return [];
            return JsonSerializer.Deserialize<List<RecentFile>>(File.ReadAllText(filePath)) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Save(List<RecentFile> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            var temp = filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(items));
            File.Move(temp, filePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The list is a convenience; failing to save it must never get in the way of opening a company.
        }
    }
}
