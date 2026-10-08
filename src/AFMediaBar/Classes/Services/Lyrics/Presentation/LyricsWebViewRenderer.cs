using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>Web 歌词视图的外观输入。/ Appearance input for the web lyrics view.</summary>
public sealed record LyricsWebStyle(
    string FontFamily,
    string LatinFontFamily,
    string CjkFontFamily,
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
    private LyricsWebStyle? _lastStyle;
    private string? _pendingStyle;
    private string? _pendingLyrics;
    private bool _initializationStarted;
    private bool _documentReady;
    private bool _draining;
    private bool _deliveryPaused;
    private bool _disposed;

    /// <summary>Web 文档已完成导航且可以接收消息时触发。/ Raised when the web document has navigated and can receive messages.</summary>
    public event EventHandler? Ready;

    /// <summary>导航或脚本执行失败时触发，由控件所有者决定是否重建。/ Raised when navigation or script execution fails; the control owner decides whether to rebuild.</summary>
    public event EventHandler? Failed;

    /// <summary>Web 文档是否已可呈现。/ Whether the web document is ready to present.</summary>
    public bool IsReady => _documentReady && !_disposed;

    public async Task<bool> InitializeAsync()
    {
        if (_disposed || _initializationStarted)
            return false;

        _initializationStarted = true;
        try
        {
            _webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            // WebView2目录统一指到本应用其余本地数据（日志、缓存、更新包）所在的 %LOCALAPPDATA%\AFMediaBar 下。
            if (_webView.CreationProperties is null)
            {
                _webView.CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "AFMediaBar")
                };
            }

            await _webView.EnsureCoreWebView2Async();
            if (_disposed || _webView.CoreWebView2 is null)
                return false;

            var settings = _webView.CoreWebView2.Settings;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.NavigateToString(BuildDocument());
            return true;
        }
        catch (Exception ex)
        {
            AppLogService.Current?.Error("Lyrics", "Failed to initialize the web lyrics renderer.", ex);
            return false;
        }
    }

    public void ApplyStyle(LyricsWebStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (_disposed || Equals(_lastStyle, style))
            return;

        // Snapshot updates can reapply appearance without changing it; skip the WebView2
        // round trip and CSS/layout work until an actual style value changes.
        _lastStyle = style;
        _pendingStyle = SerializeMessage("style", new
        {
            style.FontFamily,
            style.LatinFontFamily,
            style.CjkFontFamily,
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

    /// <summary>Hold only the newest pending messages while the taskbar cannot be seen. / 任务栏不可见时只保留最新待发送消息。</summary>
    public void PauseDelivery() => _deliveryPaused = true;

    /// <summary>Resume delivery, sending style before the latest lyrics frame. / 恢复消息投递，先发送样式再发送最新歌词帧。</summary>
    public void ResumeDelivery()
    {
        if (_disposed)
            return;
        _deliveryPaused = false;
        StartDrain();
    }

    /// <summary>Best-effort renderer suspension after WPF has made the WebView invisible. / WPF 已隐藏视图后的尽力挂起。</summary>
    public async Task<bool> TrySuspendAsync()
    {
        if (_disposed || !_documentReady || _webView.IsVisible || _webView.CoreWebView2 is null)
            return false;

        try
        {
            return await _webView.CoreWebView2.TrySuspendAsync();
        }
        catch (Exception ex)
        {
            AppLogService.Current?.Warn("Lyrics", $"Web lyrics suspension failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Resume a suspended renderer before making its WPF control visible. / 显示 WPF 控件前恢复挂起的渲染器。</summary>
    public void Resume()
    {
        if (_disposed || _webView.CoreWebView2 is null)
            return;

        try
        {
            _webView.CoreWebView2.Resume();
        }
        catch (Exception ex)
        {
            AppLogService.Current?.Warn("Lyrics", $"Web lyrics resume failed: {ex.Message}");
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_disposed)
            return;

        try
        {
            if (!e.IsSuccess)
            {
                AppLogService.Current?.Error("Lyrics", $"Web lyrics navigation failed: {e.WebErrorStatus}.");
                Failed?.Invoke(this, EventArgs.Empty);
                return;
            }

            _documentReady = true;
            await DrainAsync();
            if (!_disposed && _documentReady)
                Ready?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLogService.Current?.Error("Lyrics", "Web lyrics navigation callback failed.", ex);
            if (!_disposed)
                Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StartDrain()
    {
        if (_documentReady && !_draining && !_deliveryPaused && !_disposed)
            _ = DrainAsync();
    }

    private async Task DrainAsync()
    {
        if (!_documentReady || _draining || _deliveryPaused || _disposed || _webView.CoreWebView2 is null)
            return;

        _draining = true;
        try
        {
            while (!_disposed && !_deliveryPaused)
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
            _documentReady = false;
            _pendingStyle = null;
            _pendingLyrics = null;
            if (!_disposed)
                Failed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _draining = false;
            if ((_pendingStyle is not null || _pendingLyrics is not null) && !_deliveryPaused && !_disposed)
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
        Failed = null;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.Dispose();
    }
}
