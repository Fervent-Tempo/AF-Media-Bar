// 执行手动测试并拥有通知订阅；App 释放服务，工具窗口提供自己的取消令牌。
using System.Diagnostics;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Resources;
using Microsoft.Web.WebView2.Core;

namespace AFMediaBar.Classes.Services.Diagnostics;

/// <summary>固定开发者动作的执行协调器，复用生产入口而不改动生产通知策略。</summary>
public sealed class DeveloperScenarioService : IDeveloperScenarioService, IDisposable
{
    private readonly IDeveloperModeService _mode;
    private readonly Func<IDeveloperHostActions> _host;
    private IDeveloperHostActions? _resolvedHost;
    private IDeveloperHostActions Host => _resolvedHost ??= _host();
    private readonly ISystemNotificationService _notifications;
    private readonly IDeveloperConfirmationService _developerConfirmation;
    private readonly IRestartConfirmationService _confirmation;
    private readonly IApplicationRestartService _restart;
    private readonly PowerStateMonitor _power;
    private readonly MemoryPruneCoordinator _prune;
    private readonly AppLogService? _log;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _previewLifetime;
    private int _busy;
    private bool _disposed;

    public DeveloperScenarioService(IDeveloperModeService mode, Func<IDeveloperHostActions> host,
        ISystemNotificationService notifications, IDeveloperConfirmationService developerConfirmation,
        IRestartConfirmationService confirmation, IApplicationRestartService restart,
        PowerStateMonitor power, MemoryPruneCoordinator prune, AppLogService? log)
    {
        _mode = mode; _host = host; _notifications = notifications;
        _developerConfirmation = developerConfirmation; _confirmation = confirmation; _restart = restart;
        _power = power; _prune = prune; _log = log;
        _notifications.NotificationClicked += OnNotificationClicked;
        _mode.EnabledChanged += OnModeChanged;
    }

    public event Action<string, DeveloperActionResult>? ResultObserved;

    public IReadOnlyList<DeveloperLyricsHostState> GetLyricsHosts() =>
        !_disposed && _mode.IsEnabled ? Host.GetLyricsHosts() : [];

    public async Task<DeveloperActionResult> ExecuteAsync(string command, string? hostId, CancellationToken cancellationToken)
    {
        if (_disposed || !_mode.IsEnabled || cancellationToken.IsCancellationRequested)
            return new(DeveloperActionStatus.Canceled);
        var action = DeveloperActionCatalog.Find(command);
        if (action is null) return new(DeveloperActionStatus.Unavailable, Translations.Get("Developer.UnknownCommand"));
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return new(DeveloperActionStatus.Busy);
        var generation = _mode.Generation;
        var elapsed = Stopwatch.StartNew();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _mode.SessionToken, _lifetime.Token);
        DeveloperActionResult result;
        try
        {
            request.Token.ThrowIfCancellationRequested();
            result = await ExecuteCoreAsync(action, hostId, request.Token);
            if (_disposed || !_mode.IsEnabled || generation != _mode.Generation || request.IsCancellationRequested)
                result = new(DeveloperActionStatus.Canceled);
        }
        catch (OperationCanceledException) { result = new(DeveloperActionStatus.Canceled); }
        catch (Exception ex)
        {
            _log?.Warn("Developer", $"action={action.Command}, result=Failed", ex);
            result = new(DeveloperActionStatus.Failed);
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
        // 未识别的粘贴文本不进入日志；只记录白名单 ID 和执行结果。
        _log?.Info("Developer", $"action={action.Command}, host={hostId ?? "none"}, result={result.Status}, durationMs={elapsed.ElapsedMilliseconds}");
        return result;
    }

    private async Task<DeveloperActionResult> ExecuteCoreAsync(DeveloperAction action, string? hostId, CancellationToken token)
    {
        if (action.RequiresRenderer && !Host.GetLyricsHosts().Any(host => host.HostId == hostId && host.Loaded && host.HasRenderer && !host.Disabled))
            return new(DeveloperActionStatus.Unavailable, Translations.Get("Developer.NoRenderer"));
        switch (action.Command)
        {
            case "help":
                return new(DeveloperActionStatus.Completed, string.Join(Environment.NewLine,
                    DeveloperActionCatalog.Actions.Select(item => $"{item.Command} — {item.Title} / {item.ImpactText}: {item.Description}")));
            case "restart confirm":
                return ConfirmationResult(await _developerConfirmation.ShowPreviewAsync(token), preview: true);
            case "app restart":
                if (_developerConfirmation.IsPreviewActive) return new(DeveloperActionStatus.Busy);
                if (!await _confirmation.ConfirmAsync("Developer.Confirm.Restart", token)) return new(DeveloperActionStatus.Canceled);
                token.ThrowIfCancellationRequested();
                return new(await _restart.RequestRestartAsync(token) switch
                {
                    ApplicationRestartResult.Requested => DeveloperActionStatus.Accepted,
                    ApplicationRestartResult.Busy => DeveloperActionStatus.Busy,
                    ApplicationRestartResult.Canceled => DeveloperActionStatus.Canceled,
                    _ => DeveloperActionStatus.Failed
                });
            case "webview disable":
                var choice = await _developerConfirmation.ConfirmLyricsDisableAsync(token);
                if (choice != DeveloperConfirmationResult.Confirmed) return ConfirmationResult(choice, preview: false);
                token.ThrowIfCancellationRequested();
                // 对话框期间宿主可能重建，平台入口还会检查原 HostId。
                return Host.ExecuteLyricsAction(action.Command, hostId);
            case "taskbar reload": return Host.ReloadTaskbar();
            case "notify track": return Host.ShowTrackPreview();
            case "memory trim": return new(_prune.RequestTrim(MemoryTrimTrigger.ManualRequest) ? DeveloperActionStatus.Accepted : DeveloperActionStatus.Busy);
            case "logs open": _log?.OpenFolder(); return new(_log is null ? DeveloperActionStatus.Unavailable : DeveloperActionStatus.Accepted);
            case "state":
                var uiState = Host.CaptureState();
                var powerState = Translations.Format("Developer.State.Power", _power.IsDisplayOff, _power.IsSessionLocked,
                    _power.IsSuspended, _power.SupportsDisplayStateNotifications, _prune.CurrentLevel);
                var memory = await CaptureMemoryAsync(token);
                token.ThrowIfCancellationRequested();
                return new(DeveloperActionStatus.Completed, uiState + Environment.NewLine + powerState + Environment.NewLine + memory);
        }
        if (action.Command.StartsWith("webview ", StringComparison.Ordinal)) return Host.ExecuteLyricsAction(action.Command, hostId);
        return ShowNotification(action.Command);
    }

    private DeveloperActionResult ShowNotification(string command)
    {
        var (title, body, target) = command switch
        {
            "notify update" => ("Update.Notification.Title", "Developer.Notification.Update", ShellNotificationTarget.Application),
            "notify taskbar moved" => ("Taskbar.Placement.Notification.Title", "Taskbar.Placement.Notification.Moved", ShellNotificationTarget.None),
            "notify taskbar hidden" => ("Taskbar.Placement.Notification.Title", "Taskbar.Placement.Notification.Hidden", ShellNotificationTarget.None),
            "notify background" => ("Startup.TranslucentTb.Title", "Startup.TranslucentTb.Content", ShellNotificationTarget.TaskbarBackground),
            "notify lyrics" => ("Lyrics.Recovery.Notification.Title", "Developer.Notification.Lyrics", ShellNotificationTarget.DeveloperLyricsPreview),
            "notify restart busy" => ("Restart.Dialog.Title", "Restart.Busy", ShellNotificationTarget.None),
            "notify restart failed" => ("Restart.Dialog.Title", "Restart.Failed", ShellNotificationTarget.None),
            _ => (string.Empty, string.Empty, ShellNotificationTarget.None)
        };
        if (title.Length == 0) return new(DeveloperActionStatus.Unavailable);
        var accepted = _notifications.TryShowNotification(Translations.Get("Developer.Notification.Prefix") + " " + Translations.Get(title), Translations.Get(body), target);
        if (accepted && target == ShellNotificationTarget.DeveloperLyricsPreview)
        {
            _previewLifetime?.Cancel(); _previewLifetime?.Dispose();
            _previewLifetime = CancellationTokenSource.CreateLinkedTokenSource(_mode.SessionToken);
        }
        return new(accepted ? DeveloperActionStatus.Accepted : DeveloperActionStatus.Failed, Translations.Get("Developer.Notification.Submitted"));
    }

    private static DeveloperActionResult ConfirmationResult(DeveloperConfirmationResult result, bool preview) => result switch
    {
        DeveloperConfirmationResult.Busy => new(DeveloperActionStatus.Busy),
        DeveloperConfirmationResult.Confirmed => new(DeveloperActionStatus.Completed, preview ? Translations.Get("Developer.PreviewConfirmed") : null),
        _ => new(DeveloperActionStatus.Canceled)
    };

    private static Task<string> CaptureMemoryAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        try
        {
            string runtime;
            try { runtime = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch { runtime = Translations.Get("Developer.Result.Unavailable"); }
            using var process = Process.GetCurrentProcess();
            return $"WebView2 Runtime: {runtime}" + Environment.NewLine + Translations.Format("Developer.State.Memory", process.WorkingSet64 / 1048576d,
                process.PrivateMemorySize64 / 1048576d, GC.GetTotalMemory(false) / 1048576d, process.HandleCount, process.Threads.Count);
        }
        catch { return Translations.Get("Developer.State.MemoryUnavailable"); }
    }, token);

    private async void OnNotificationClicked(ShellNotificationTarget target)
    {
        if (_disposed || !_mode.IsEnabled || target != ShellNotificationTarget.DeveloperLyricsPreview || _previewLifetime is null) return;
        // 此入口只能预览；真实歌词通知仍由生产协调器处理。
        var generation = _mode.Generation;
        var token = _previewLifetime.Token;
        var result = await ExecuteAsync("restart confirm", null, token);
        if (!_disposed && _mode.IsEnabled && !token.IsCancellationRequested && generation == _mode.Generation)
            ResultObserved?.Invoke("restart confirm", result);
    }

    private void OnModeChanged(object? sender, EventArgs e)
    {
        if (!_mode.IsEnabled) ClosePreviews();
    }

    public void ClosePreviews()
    {
        _previewLifetime?.Cancel(); _previewLifetime?.Dispose(); _previewLifetime = null;
        if (_resolvedHost is not null)
        {
            try { _resolvedHost?.ClosePreviews(); }
            catch (Exception ex) { _log?.Warn("Developer", "关闭测试预览失败", ex); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        ClosePreviews();
        _notifications.NotificationClicked -= OnNotificationClicked;
        _mode.EnabledChanged -= OnModeChanged;
        _lifetime.Dispose();
    }
}
