namespace Baba.Api;

/// <summary>The company file the app was started with (a double-click on a .baba file). The desktop host sets it before showing the UI.</summary>
public sealed class StartupRequest
{
    private string? _openPath;

    public string? OpenPath
    {
        get => Volatile.Read(ref _openPath);
        set => Volatile.Write(ref _openPath, value);
    }

    /// <summary>Returns the path once, then forgets it.</summary>
    public string? TakeOpenPath() => Interlocked.Exchange(ref _openPath, null);
}
