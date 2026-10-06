using Baba.Application.Printing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Baba.Desktop;

/// <summary>
/// Makes PDFs with the browser engine inside WebView2, in a hidden window. The same engine draws the screen,
/// so Arabic letter joining, right-to-left layout and bilingual pages print exactly as designed in HTML and CSS.
/// </summary>
internal sealed class WebView2PdfRenderer(Form owner, Func<Task<CoreWebView2Environment>> environment) : IPdfRenderer, IDisposable
{
    private const double MillimetersPerInch = 25.4;

    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private Form? _host;
    private WebView2? _view;

    public async Task<byte[]> RenderAsync(string html, PdfOptions options, CancellationToken cancellationToken = default)
    {
        await _oneAtATime.WaitAsync(cancellationToken);
        try
        {
            var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.BeginInvoke(async () =>
            {
                try
                {
                    completion.SetResult(await RenderOnUiThreadAsync(html, options));
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
            });
            return await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task<byte[]> RenderOnUiThreadAsync(string html, PdfOptions options)
    {
        var view = await EnsureViewAsync();
        var core = view.CoreWebView2;

        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult(e.IsSuccess);
        core.NavigationCompleted += OnCompleted;
        try
        {
            core.NavigateToString(html);
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30)))
                throw new InvalidOperationException("The page could not be prepared for printing.");
        }
        finally
        {
            core.NavigationCompleted -= OnCompleted;
        }

        // Wait until the embedded fonts are ready, or the PDF would be drawn with fallback letters.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await core.ExecuteScriptAsync("document.fonts.status") == "\"loaded\"")
                break;
            await Task.Delay(50);
        }

        var settings = core.Environment.CreatePrintSettings();
        settings.Orientation = options.Landscape ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
        settings.PageWidth = (options.Landscape ? 297 : 210) / MillimetersPerInch; // A4
        settings.PageHeight = (options.Landscape ? 210 : 297) / MillimetersPerInch;
        var margin = options.MarginMillimeters / MillimetersPerInch;
        (settings.MarginTop, settings.MarginBottom, settings.MarginLeft, settings.MarginRight) = (margin, margin, margin, margin);
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;

        await using var stream = await core.PrintToPdfStreamAsync(settings);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    /// <summary>A tiny browser window kept off screen. It is created on first use and reused afterwards.</summary>
    private async Task<WebView2> EnsureViewAsync()
    {
        if (_view is not null)
            return _view;

        _host = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(900, 700),
        };
        var view = new WebView2 { Dock = DockStyle.Fill };
        _host.Controls.Add(view);
        _host.Show(); // must be shown for the engine to lay pages out, but it stays off screen
        await view.EnsureCoreWebView2Async(await environment());
        view.CoreWebView2.Settings.AreDevToolsEnabled = false;
        view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _view = view;
        return view;
    }

    public void Dispose()
    {
        _host?.Dispose();
        _oneAtATime.Dispose();
    }
}
