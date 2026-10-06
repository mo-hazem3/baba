namespace Baba.Desktop;

static class Program
{
    /// <summary>
    /// Usage: <c>Baba.Desktop.exe [company.baba]</c>. Opening a .baba file from Explorer starts the app with that path.
    /// <c>--smoke-test result.json</c> starts the app, checks the browser can reach the API, writes the result and exits.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        string? openPath = null;
        string? smokeTestOutput = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--smoke-test" && i + 1 < args.Length)
                smokeTestOutput = Path.GetFullPath(args[++i]);
            else if (args[i].EndsWith(".baba", StringComparison.OrdinalIgnoreCase))
                openPath = Path.GetFullPath(args[i]);
        }

        // A smoke test must be able to run while a real Baba window is open, so it skips the single-instance check.
        using var instance = smokeTestOutput is null ? SingleInstance.TryAcquire() : null;
        if (instance is null && smokeTestOutput is null)
        {
            SingleInstance.NotifyRunningInstance(openPath);
            return 0;
        }
        instance?.StartListening();

        ApplicationConfiguration.Initialize();
        // Fully qualified: inside Baba.* the bare name "Application" means the Baba.Application namespace.
        System.Windows.Forms.Application.Run(new MainForm(openPath, instance, smokeTestOutput));
        return Environment.ExitCode;
    }
}
