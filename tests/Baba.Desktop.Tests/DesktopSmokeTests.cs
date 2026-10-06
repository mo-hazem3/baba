using System.Diagnostics;
using System.Text.Json;

namespace Baba.Desktop.Tests;

/// <summary>Starts the real desktop app (WebView2 window + local API) and checks the pieces work together.</summary>
public class DesktopSmokeTests
{
    [Fact]
    public async Task The_window_can_reach_the_api_with_its_secret_and_nobody_else_can()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Baba.Desktop.exe");
        var result = Path.Combine(Path.GetTempPath(), $"baba-smoke-{Guid.NewGuid():N}.json");

        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, $"--smoke-test \"{result}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);

            Assert.True(File.Exists(result), "The app did not write a smoke test result.");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result));
            var root = json.RootElement;

            Assert.True(root.GetProperty("ok").GetBoolean(), root.ToString());
            Assert.Equal(401, root.GetProperty("requestWithoutTokenStatus").GetInt32());
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            File.Delete(result);
        }
    }
}
