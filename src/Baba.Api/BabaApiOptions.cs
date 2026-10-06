namespace Baba.Api;

/// <summary>How the API is hosted. The desktop launcher fills this in; the standalone program reads it from environment variables.</summary>
public sealed class BabaApiOptions
{
    /// <summary>0 picks a free port (what the desktop app does).</summary>
    public int Port { get; init; }

    /// <summary>
    /// A random secret created at each launch. Every <c>/api</c> request must carry it (header or cookie), so other
    /// programs and web pages on the same computer cannot read the company file. Null disables the check (tests and dev only).
    /// </summary>
    public string? AccessToken { get; init; }

    /// <summary>Folder with the built web app (<c>web/dist</c>), or null to serve the API only.</summary>
    public string? WebRootPath { get; init; }

    /// <summary>Extra host names accepted besides localhost, 127.0.0.1 and [::1].</summary>
    public IReadOnlyList<string> AdditionalAllowedHosts { get; init; } = [];

    /// <summary>Extra origins allowed to make changes, for example the Vite dev server during development.</summary>
    public IReadOnlyList<string> AdditionalAllowedOrigins { get; init; } = [];

    /// <summary>Where the recent files list is kept. Defaults to the user's local app data.</summary>
    public string? RecentFilesPath { get; init; }
}
