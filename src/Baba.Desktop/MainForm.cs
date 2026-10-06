using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Baba.Api;
using Baba.Application.Abstractions;
using Baba.Application.Companies;
using Baba.Application.Printing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Baba.Desktop;

/// <summary>
/// The Baba window: starts the local API on a random port with a per-launch secret and shows the web app in WebView2.
/// The app talks to the API only over HTTP, exactly as the cloud edition will.
/// </summary>
internal sealed class MainForm : Form
{
    private const string TokenCookie = "baba-token";

    private readonly string? _openPath;
    private readonly SingleInstance? _instance;
    private readonly string? _smokeTestOutput;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private WebApplication? _api;
    private Uri? _origin;
    private WebView2PdfRenderer? _pdfRenderer;

    // One browser environment shared by the window and the hidden PDF renderer (they must use the same profile folder).
    private readonly Task<CoreWebView2Environment> _environment = CoreWebView2Environment.CreateAsync(
        userDataFolder: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Baba", "WebView2"));

    public MainForm(string? openPath, SingleInstance? instance, string? smokeTestOutput)
    {
        _openPath = openPath;
        _instance = instance;
        _smokeTestOutput = smokeTestOutput;

        Text = "Baba";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 780);
        MinimumSize = new Size(1000, 640);
        Controls.Add(_webView);

        if (_instance is not null)
            _instance.FileRequested += OnFileRequested;
        Load += async (_, _) => await StartAsync();
        FormClosing += async (_, _) => await ShutDownAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            var devUrl = Environment.GetEnvironmentVariable("BABA_WEB_URL"); // Vite dev server, development only
            var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");

            var options = new BabaApiOptions
            {
                Port = int.TryParse(Environment.GetEnvironmentVariable("BABA_PORT"), out var port) ? port : 0,
                AccessToken = _token,
                WebRootPath = Directory.Exists(webRoot) ? webRoot : null,
                AdditionalAllowedOrigins = devUrl is null ? [] : [new Uri(devUrl).GetLeftPart(UriPartial.Authority)],
            };

            _pdfRenderer = new WebView2PdfRenderer(this, () => _environment);
            _api = BabaApi.Create(options, builder =>
            {
                builder.Logging.SetMinimumLevel(LogLevel.Warning); // a desktop app has no console to fill with request logs
                builder.Services.AddSingleton<IFileDialogs>(new WinFormsFileDialogs(this));
                builder.Services.AddSingleton<IPdfRenderer>(_pdfRenderer);
            });
            _api.Services.GetRequiredService<StartupRequest>().OpenPath = _openPath;
            await _api.StartAsync();
            _origin = new Uri(_api.Urls.First());

            await StartBrowserAsync();
            _webView.CoreWebView2.Navigate(_smokeTestOutput is null ? devUrl ?? _origin.ToString() : new Uri(_origin, "/api/host").ToString());
        }
        catch (Exception e)
        {
            if (_smokeTestOutput is not null)
                await FinishSmokeTestAsync(new { ok = false, error = e.ToString() });
            else
                MessageBox.Show(this, e.Message, "Baba could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task StartBrowserAsync()
    {
        await _webView.EnsureCoreWebView2Async(await _environment);

        var settings = _webView.CoreWebView2.Settings;
        settings.AreDevToolsEnabled = Debugger.IsAttached || Environment.GetEnvironmentVariable("BABA_DEVTOOLS") == "1";
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsZoomControlEnabled = false; // the app has its own text size setting
        settings.IsStatusBarEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;

        // The secret goes in a strict same-site, HTTP-only cookie, so the page never handles it.
        var cookie = _webView.CoreWebView2.CookieManager.CreateCookie(TokenCookie, _token, _origin!.Host, "/");
        cookie.SameSite = CoreWebView2CookieSameSiteKind.Strict;
        cookie.IsHttpOnly = true;
        _webView.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);

        _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
        _webView.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            if (e.Uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
                return; // a PDF the app just made: let it open in its own viewer window (print, save)

            e.Handled = true;
            OpenExternally(e.Uri);
        };
        _webView.CoreWebView2.DocumentTitleChanged += (_, _) =>
        {
            if (_smokeTestOutput is null && !string.IsNullOrWhiteSpace(_webView.CoreWebView2.DocumentTitle))
                Text = _webView.CoreWebView2.DocumentTitle;
        };
        _webView.CoreWebView2.NavigationCompleted += async (_, e) =>
        {
            if (_smokeTestOutput is null)
                return;
            if (_smokeApiResult is null)
                await RunApiSmokeTestAsync(e.IsSuccess);
            else
                await RunPageSmokeTestAsync();
        };
    }

    /// <summary>Stays inside the app: links to other sites open in the normal browser instead.</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var target = new Uri(e.Uri);
        var devUrl = Environment.GetEnvironmentVariable("BABA_WEB_URL");
        var inApp = target.Scheme is "about" or "data"
                    || target.GetLeftPart(UriPartial.Authority) == _origin!.GetLeftPart(UriPartial.Authority)
                    || (devUrl is not null && target.GetLeftPart(UriPartial.Authority) == new Uri(devUrl).GetLeftPart(UriPartial.Authority));
        if (inApp)
            return;

        e.Cancel = true;
        OpenExternally(e.Uri);
    }

    private static void OpenExternally(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https")
            Process.Start(new ProcessStartInfo(parsed.ToString()) { UseShellExecute = true });
    }

    /// <summary>A second launch (for example a double-click on a .baba file) arrives here.</summary>
    private void OnFileRequested(string path)
    {
        if (IsDisposed)
            return;

        BeginInvoke(async () =>
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();

            if (path.Length > 0 && _api is not null && _webView.CoreWebView2 is not null)
            {
                _api.Services.GetRequiredService<StartupRequest>().OpenPath = path;
                await _webView.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('baba:startup-file'))");
            }
        });
    }

    private async Task ShutDownAsync()
    {
        if (_instance is not null)
            _instance.FileRequested -= OnFileRequested;
        if (_api is null)
            return;

        var api = _api;
        _api = null;
        api.Services.GetRequiredService<ICompanyFiles>().Close(); // release the .baba file first
        await api.StopAsync();
        await api.DisposeAsync();
        _pdfRenderer?.Dispose();
    }

    // --- Smoke test: `Baba.Desktop.exe --smoke-test <result.json>` proves WebView2 + token + API + web app work end to end. ---

    private SmokeApiResult? _smokeApiResult;

    private sealed record SmokeApiResult(bool Ok, string BrowserSawApi, int RequestWithoutTokenStatus);

    /// <summary>Stage 1: the window can call the API with its cookie, and a plain client without it is refused.</summary>
    private async Task RunApiSmokeTestAsync(bool navigated)
    {
        try
        {
            var body = await _webView.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
            using var plain = new HttpClient();
            var withoutToken = await plain.GetAsync(new Uri(_origin!, "/api/host"));

            var viaBrowser = System.Text.Json.JsonSerializer.Deserialize<string>(body) ?? "";
            var ok = navigated
                     && viaBrowser.Contains("\"fileDialogs\":true", StringComparison.OrdinalIgnoreCase)
                     && withoutToken.StatusCode == System.Net.HttpStatusCode.Unauthorized;
            _smokeApiResult = new SmokeApiResult(ok, viaBrowser, (int)withoutToken.StatusCode);

            if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot")))
                _webView.CoreWebView2.Navigate(_origin!.ToString()); // stage 2 runs when this page has loaded
            else
                await FinishSmokeTestAsync(new { ok, browserSawApi = viaBrowser, requestWithoutTokenStatus = _smokeApiResult.RequestWithoutTokenStatus, webApp = "not bundled" });
        }
        catch (Exception e)
        {
            await FinishSmokeTestAsync(new { ok = false, error = e.ToString() });
        }
    }

    /// <summary>Stage 2: the bundled web app starts and shows its first screen (a page heading).</summary>
    private async Task RunPageSmokeTestAsync()
    {
        try
        {
            string heading = "";
            for (var attempt = 0; attempt < 60 && heading.Length == 0; attempt++)
            {
                var result = await _webView.CoreWebView2.ExecuteScriptAsync("document.querySelector('h1')?.innerText ?? ''");
                heading = System.Text.Json.JsonSerializer.Deserialize<string>(result) ?? "";
                if (heading.Length == 0)
                    await Task.Delay(250);
            }

            var api = _smokeApiResult!;
            var pdfs = await RenderSmokeTestPdfsAsync();
            await FinishSmokeTestAsync(new
            {
                ok = api.Ok && heading.Length > 0 && pdfs.All(p => p.Value.Ok),
                browserSawApi = api.BrowserSawApi,
                requestWithoutTokenStatus = api.RequestWithoutTokenStatus,
                webApp = heading,
                pdfs,
            });
        }
        catch (Exception e)
        {
            await FinishSmokeTestAsync(new { ok = false, error = e.ToString() });
        }
    }

    private sealed record SmokePdf(bool Ok, int Status, int Bytes, bool IsPdf, bool EmbedsArabicFont, string File);

    /// <summary>Stage 3: the app makes the Arabic, English and bilingual test pages as real PDFs, saved next to the result file.</summary>
    private async Task<Dictionary<string, SmokePdf>> RenderSmokeTestPdfsAsync()
    {
        using var client = new HttpClient { BaseAddress = _origin };
        client.DefaultRequestHeaders.Add("X-Baba-Token", _token);

        var results = new Dictionary<string, SmokePdf>();
        foreach (var layout in new[] { "Arabic", "English", "Both" })
        {
            var response = await client.PostAsJsonAsync("/api/print/test-page", new { layout });
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var file = Path.ChangeExtension(_smokeTestOutput!, $".{layout.ToLowerInvariant()}.pdf");
            await File.WriteAllBytesAsync(file, bytes);

            var isPdf = bytes.Length > 4 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "%PDF";
            var embedsFont = System.Text.Encoding.Latin1.GetString(bytes).Contains("NotoSansArabic", StringComparison.Ordinal);
            results[layout] = new SmokePdf(response.IsSuccessStatusCode && isPdf && bytes.Length > 10_000 && embedsFont,
                (int)response.StatusCode, bytes.Length, isPdf, embedsFont, file);
        }

        return results;
    }

    private async Task FinishSmokeTestAsync(object result)
    {
        await File.WriteAllTextAsync(_smokeTestOutput!, System.Text.Json.JsonSerializer.Serialize(result));
        Environment.ExitCode = ((dynamic)result).ok ? 0 : 1;
        Close();
    }
}
