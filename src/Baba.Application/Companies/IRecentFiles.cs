namespace Baba.Application.Companies;

public sealed record RecentFile(string Path, DateTime LastOpenedAt);

/// <summary>The start screen's list of recently used company files, newest first.</summary>
public interface IRecentFiles
{
    IReadOnlyList<RecentFile> List();

    /// <summary>Moves the file to the top of the list (adding it if needed).</summary>
    void Add(string path);

    void Remove(string path);
}
