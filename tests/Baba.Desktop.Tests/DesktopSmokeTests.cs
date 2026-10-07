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

            // The test projects run side by side on a shared build machine, so starting WebView2 and making the PDFs can take a while.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true); // never leave the window running
                throw new TimeoutException("The desktop app did not finish its smoke test within 4 minutes.");
            }

            Assert.True(File.Exists(result), "The app did not write a smoke test result.");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result));
            var root = json.RootElement;

            Assert.True(root.GetProperty("ok").GetBoolean(), root.ToString());
            Assert.Equal(401, root.GetProperty("requestWithoutTokenStatus").GetInt32());
            // A change made by the page itself (cookie + Origin header) is accepted: this is how the real app talks to the API.
            Assert.Equal(204, root.GetProperty("postFromPageStatus").GetInt32());
            // When the web app is bundled (web/dist was built), its first screen must have appeared in the window; the result
            // says "not bundled" otherwise (CI builds the backend before the web app), and everything else is still checked.
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("webApp").GetString()), root.ToString());

            // The app makes real PDFs of the Arabic, English and bilingual test pages, with the Arabic font embedded.
            foreach (var layout in new[] { "Arabic", "English", "Both" })
            {
                var pdf = root.GetProperty("pdfs").GetProperty(layout);
                Assert.True(pdf.GetProperty("Ok").GetBoolean(), $"{layout}: {pdf}");
                Assert.True(pdf.GetProperty("IsPdf").GetBoolean(), layout);
                Assert.True(pdf.GetProperty("EmbedsArabicFont").GetBoolean(), layout);
            }

            // A file made by the page (an Excel or CSV export) reaches the host's download handler and is saved.
            var download = root.GetProperty("download");
            Assert.True(download.GetProperty("Ok").GetBoolean(), download.ToString());

            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(result)!, Path.GetFileNameWithoutExtension(result) + ".*"))
                File.Delete(file);
        }
    }
}
