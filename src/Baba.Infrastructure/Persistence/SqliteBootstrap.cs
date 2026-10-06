namespace Baba.Infrastructure.Persistence;

internal static class SqliteBootstrap
{
    private static readonly object Gate = new();
    private static bool _initialised;

    /// <summary>Selects the SQLCipher build of SQLite. Must run once before any connection is opened.</summary>
    public static void Ensure()
    {
        lock (Gate)
        {
            if (_initialised)
                return;
            SQLitePCL.Batteries_V2.Init();
            _initialised = true;
        }
    }
}
