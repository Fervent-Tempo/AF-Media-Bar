// 一个宠物网页的资源所有者；消息最多一条在途、一条最新待发，Dispose 拒绝迟到结果。
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AFMediaBar.Components;

internal sealed class PetWebRenderer(PetWebEnvironment environment) : IPetWebRenderer
{
    private readonly WebView2CompositionControl _view = new()
    {
        MinWidth = 1,
        MinHeight = 1,
        IsHitTestVisible = false,
        Focusable = false,
        DefaultBackgroundColor = System.Drawing.Color.Transparent,
        AllowExternalDrop = false
    };
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private CoreWebView2? _core;
    private PetWebState? _pending;
    private bool _documentReady;
    private bool _ready;
    private bool _disposed;
    private bool _failed;
    private bool _active = true;
    private bool _resetEvents = true;
    private long _messageId;
    private long? _inflight;
    private int _lifetimeVersion;
    private string? _documentDataUri;
    private ulong? _documentNavigationId;
    private readonly DispatcherTimer _readyTimeout = new() { Interval = TimeSpan.FromSeconds(15) };

    public FrameworkElement View => _view;
    public event EventHandler? Ready;
    public event EventHandler? Failed;

    public async Task InitializeAsync()
    {
        _readyTimeout.Tick += OnReadyTimeout;
        _readyTimeout.Start();
        var sharedEnvironment = await environment.GetAsync();
        if (_disposed) return;
        await _view.EnsureCoreWebView2Async(sharedEnvironment);
        if (_disposed || _view.CoreWebView2 is not { } core) return;
        _core = core;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.WebMessageReceived += OnMessage;
        core.ProcessFailed += OnProcessFailed;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindow;
        core.PermissionRequested += OnPermission;
        core.DownloadStarting += OnDownload;
        _view.NavigationCompleted += OnNavigationCompleted;
        var document = BuildDocument();
        // 合成控件当前会把 NavigateToString 转为 data URI；只允许这份内嵌文档的精确内容。
        _documentDataUri = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(document));
        _view.NavigateToString(document);
        if (!_active) _ = SuspendAsync(core, ++_lifetimeVersion);
    }

    public void Present(PetWebState state)
    {
        if (_disposed || _failed) return;
        _pending = state;
        Drain();
    }

    private void Drain()
    {
        if (!_documentReady || _disposed || _failed || _inflight is not null ||
            _pending is not { } state || _core is null) return;
        _pending = null;
        var id = ++_messageId;
        _inflight = id;
        try
        {
            var initial = !_ready || _resetEvents;
            _resetEvents = false;
            _core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "state", id, initial, state }, _jsonOptions));
        }
        catch (Exception error) when (error is InvalidOperationException or COMException) { ReportFailure(error); }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_disposed || _failed) return;
        try
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            var root = message.RootElement;
            if (!root.TryGetProperty("type", out var type)) return;
            if (type.GetString() == "ready")
            {
                _documentReady = true;
                Drain();
            }
            else if (type.GetString() == "ack" && root.TryGetProperty("id", out var id) &&
                id.TryGetInt64(out var number) && _inflight == number)
            {
                _inflight = null;
                if (_pending is not null) Drain();
                else if (!_ready)
                {
                    _ready = true;
                    _readyTimeout.Stop();
                    Ready?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (JsonException error) { ReportFailure(error); }
    }

    public void SetActive(bool active)
    {
        if (_disposed) return;
        if (_active == active) return;
        _active = active;
        var version = ++_lifetimeVersion;
        try
        {
            if (active)
            {
                if (!_ready) _readyTimeout.Start();
                _core?.Resume();
                _view.Visibility = Visibility.Visible;
                Drain();
            }
            else
            {
                _readyTimeout.Stop();
                _resetEvents = true;
                _view.Visibility = Visibility.Hidden;
                if (_core is { } core) _ = SuspendAsync(core, version);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or COMException) { ReportFailure(error); }
    }

    private async Task SuspendAsync(CoreWebView2 core, int version)
    {
        try
        {
            // Visibility.Hidden 先传到原生控制器，再请求挂起；恢复可抢先于此异步操作完成。
            await _view.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            if (_disposed || !ReferenceEquals(core, _core) || version != _lifetimeVersion || _active) return;
            await core.TrySuspendAsync();
            if (!_disposed && ReferenceEquals(core, _core) && _active) core.Resume();
        }
        catch (Exception error) when (error is InvalidOperationException or COMException or TaskCanceledException)
        {
            if (!_disposed) AppLogService.Current?.Warn("Pet", $"挂起宠物浏览器失败: {error.Message}");
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (args.Uri != "about:blank" && args.Uri != _documentDataUri)
        {
            AppLogService.Current?.Warn("Pet", "阻止宠物外部导航");
            args.Cancel = true;
        }
        else _documentNavigationId = args.NavigationId;
    }
    private void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs args) => args.Handled = true;
    private void OnPermission(object? sender, CoreWebView2PermissionRequestedEventArgs args) => args.State = CoreWebView2PermissionState.Deny;
    private void OnDownload(object? sender, CoreWebView2DownloadStartingEventArgs args) => args.Cancel = true;
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.NavigationId == _documentNavigationId && !args.IsSuccess)
            ReportFailure(new InvalidOperationException($"Navigation {args.NavigationId}: {args.WebErrorStatus}"));
    }
    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args) =>
        ReportFailure(new InvalidOperationException($"Process failed: {args.ProcessFailedKind}"));
    private void OnReadyTimeout(object? sender, EventArgs args) => ReportFailure(new TimeoutException("Pet page did not become ready."));

    private void ReportFailure(Exception? error)
    {
        if (_disposed || _failed) return;
        _failed = true;
        _readyTimeout.Stop();
        AppLogService.Current?.Warn("Pet", $"宠物网页不可用: {error?.Message ?? "导航或进程故障"}");
        _view.Dispatcher.BeginInvoke(() => { if (!_disposed) Failed?.Invoke(this, EventArgs.Empty); });
    }

    internal static string BuildDocument()
    {
        string Read(string suffix)
        {
            var assembly = typeof(PetWebRenderer).Assembly;
            using var stream = assembly.GetManifestResourceStream("AFMediaBar.Web.Pet." + suffix)
                ?? throw new InvalidOperationException("Missing embedded pet resource: " + suffix);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        return Read("index.html")
            .Replace("{{BODY_PATH}}", PetVisualDefinition.BodyPath, StringComparison.Ordinal)
            .Replace("{{STYLE}}", Read("style.css"), StringComparison.Ordinal)
            .Replace("{{GEOMETRY}}", Read("geometry.js"), StringComparison.Ordinal)
            .Replace("{{MOTION}}", Read("motion.js"), StringComparison.Ordinal)
            .Replace("{{EFFECTS}}", Read("effects.js"), StringComparison.Ordinal)
            .Replace("{{PARTICLES}}", string.Concat(Enumerable.Range(0, 16).Select(index =>
                $"<rect id=\"particle-{index}\" x=\"-1\" y=\"-2\" width=\"2\" height=\"4\" rx=\"0.6\"/>")), StringComparison.Ordinal)
            .Replace("{{SCRIPT}}", Read("pet.js"), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_lifetimeVersion;
        _pending = null;
        _readyTimeout.Stop();
        _readyTimeout.Tick -= OnReadyTimeout;
        _view.NavigationCompleted -= OnNavigationCompleted;
        try
        {
            if (_core is { } core)
            {
                core.WebMessageReceived -= OnMessage;
                core.ProcessFailed -= OnProcessFailed;
                core.NavigationStarting -= OnNavigationStarting;
                core.NewWindowRequested -= OnNewWindow;
                core.PermissionRequested -= OnPermission;
                core.DownloadStarting -= OnDownload;
            }
        }
        catch (Exception error) when (error is InvalidOperationException or COMException)
        {
            AppLogService.Current?.Warn("Pet", $"宠物浏览器退订失败: {error.Message}");
        }
        finally
        {
            _core = null;
            Ready = null;
            Failed = null;
            try { _view.Dispose(); }
            catch (Exception error) when (error is InvalidOperationException or COMException || WebView2GraphicsFaultPolicy.IsGraphicsFault(error))
            {
                AppLogService.Current?.Warn("Pet", $"宠物浏览器释放失败: {error.Message}");
            }
        }
    }
}
