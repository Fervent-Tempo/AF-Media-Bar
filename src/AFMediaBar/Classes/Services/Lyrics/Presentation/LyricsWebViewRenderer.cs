using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>Web 歌词视图的外观输入。/ Appearance input for the web lyrics view.</summary>
public sealed record LyricsWebStyle(
    string FontFamily,
    double FontSize,
    int FontWeight,
    double CharacterSpacingPercent,
    double LineGapPercent,
    string TextAlignment,
    string PrimaryColor,
    string SecondaryColor,
    string TranslationColor,
    string WordScanBaseColor,
    string WordScanOverlayColor,
    string TextShadow);

/// <summary>
/// WebView2 歌词适配器。它只保留每种消息的最新值，因此初始化或脚本执行较慢时不会回放过期帧。
/// WebView2 lyrics adapter. It retains only the latest value of each message kind, so stale frames are never replayed after
/// initialization or slow script execution.
/// </summary>
public sealed class LyricsWebViewRenderer(WebView2CompositionControl webView) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly WebView2CompositionControl _webView = webView ?? throw new ArgumentNullException(nameof(webView));
    private string? _pendingStyle;
    private string? _pendingLyrics;
    private bool _initializationStarted;
    private bool _documentReady;
    private bool _draining;
    private bool _disposed;

    /// <summary>Web 文档已完成导航且可以接收消息时触发。/ Raised when the web document has navigated and can receive messages.</summary>
    public event EventHandler? Ready;

    /// <summary>Web 文档是否已可呈现。/ Whether the web document is ready to present.</summary>
    public bool IsReady => _documentReady && !_disposed;

    public async Task InitializeAsync()
    {
        if (_disposed || _initializationStarted)
            return;

        _initializationStarted = true;
        try
        {
            _webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            await _webView.EnsureCoreWebView2Async();
            if (_disposed || _webView.CoreWebView2 is null)
                return;

            var settings = _webView.CoreWebView2.Settings;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.NavigateToString(BuildDocument());
        }
        catch (Exception ex)
        {
            AppLogService.Current?.Error("Lyrics", "Failed to initialize the web lyrics renderer.", ex);
        }
    }

    public void ApplyStyle(LyricsWebStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _pendingStyle = SerializeMessage("style", new
        {
            style.FontFamily,
            style.FontSize,
            style.FontWeight,
            style.CharacterSpacingPercent,
            style.LineGapPercent,
            style.TextAlignment,
            showCover = false,
            layoutScalePercent = 100,
            style.PrimaryColor,
            style.SecondaryColor,
            style.TranslationColor,
            style.WordScanBaseColor,
            style.WordScanOverlayColor,
            surfaceColor = "transparent",
            surfaceShadow = "none",
            style.TextShadow
        });
        StartDrain();
    }

    public void Present(LyricsPresentationFrame frame, bool animateTransition)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _pendingLyrics = SerializeMessage("lyrics", new
        {
            current = frame.IsVisible ? frame.Current : string.Empty,
            next = frame.IsVisible ? frame.Next : string.Empty,
            progress = frame.IsVisible ? frame.LineProgress : 0,
            currentLineIndex = frame.IsVisible ? frame.CurrentLineIndex : -1,
            trackId = frame.IsVisible ? frame.TrackId : string.Empty,
            isPureMusic = false,
            isPlaying = frame.IsVisible && frame.IsPlaying,
            wordScanProgress = frame.IsVisible ? frame.WordScanProgress : null,
            currentTranslation = frame.IsVisible ? frame.CurrentTranslation : string.Empty,
            nextTranslation = frame.IsVisible ? frame.NextTranslation : string.Empty,
            translationMode = frame.IsVisible && frame.TranslationMode,
            animateTransition = frame.IsVisible && animateTransition,
            scene = "lyrics"
        });
        StartDrain();
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_disposed)
            return;

        if (!e.IsSuccess)
        {
            AppLogService.Current?.Error("Lyrics", $"Web lyrics navigation failed: {e.WebErrorStatus}.");
            return;
        }

        _documentReady = true;
        await DrainAsync();
        Ready?.Invoke(this, EventArgs.Empty);
    }

    private void StartDrain()
    {
        if (_documentReady && !_draining && !_disposed)
            _ = DrainAsync();
    }

    private async Task DrainAsync()
    {
        if (!_documentReady || _draining || _disposed || _webView.CoreWebView2 is null)
            return;

        _draining = true;
        try
        {
            while (!_disposed)
            {
                var script = _pendingStyle;
                if (script is not null)
                    _pendingStyle = null;
                else
                {
                    script = _pendingLyrics;
                    _pendingLyrics = null;
                }

                if (script is null)
                    break;

                await _webView.CoreWebView2.ExecuteScriptAsync(script);
            }
        }
        catch (Exception ex)
        {
            AppLogService.Current?.Error("Lyrics", "Failed to update the web lyrics renderer.", ex);
        }
        finally
        {
            _draining = false;
            if ((_pendingStyle is not null || _pendingLyrics is not null) && !_disposed)
                StartDrain();
        }
    }

    private static string SerializeMessage(string type, object payload)
    {
        var json = JsonSerializer.Serialize(new { version = 1, type, payload }, JsonOptions);
        return $"window.taskbarLyrics?.receive({json});";
    }

    private static string BuildDocument()
    {
        var template = ReadResource("Web.Lyrics.index.html");
        var style = ReadResource("Web.Lyrics.style.css").Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase);
        var scripts = string.Join("\n", new[]
        {
            ReadResource("Web.Lyrics.state.js"),
            ReadResource("Web.Lyrics.spacing.js"),
            ReadResource("Web.Lyrics.presentation.js"),
            ReadResource("Web.Lyrics.app.js")
        }).Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
        return template.Replace("{{STYLE_CSS}}", style, StringComparison.Ordinal)
            .Replace("{{APP_JS}}", scripts, StringComparison.Ordinal);
    }

    private static string ReadResource(string suffix)
    {
        var assembly = typeof(LyricsWebViewRenderer).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded lyrics resource '{suffix}' was not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded lyrics resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _documentReady = false;
        _pendingStyle = null;
        _pendingLyrics = null;
        Ready = null;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.Dispose();
    }
}
