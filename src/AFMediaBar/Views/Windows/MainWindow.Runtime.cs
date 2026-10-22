// 运行模式与宿主生命周期由协调器提交，旧窗口不会在新窗口可用前销毁。
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Views.Windows;

public partial class MainWindow
{
    private readonly BarRuntimeCoordinator _runtime;
    private readonly Func<string, ITaskbarWindowHostActions, TaskbarWindow> _taskbarFactory;
    private readonly Func<string, DisplayMonitorInfo, FloatingBarWindow> _floatingFactory;
    private readonly List<FloatingBarWindow> _floatingWindows = [];
    private string? _runtimeSignature;
    private string? _pendingRuntimeSignature;
    private int _runtimeRequest;
    private bool _runtimeUpdateQueued;
    private MemoryPruneLevel _runtimePruneLevel;

    private void RuntimeSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_isClosing || _runtimeUpdateQueued) return;
        _runtimeUpdateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _runtimeUpdateQueued = false;
            if (_isClosing) return;
            QueueRuntimeRefresh();
            if (_runtime.RunningMode == WindowMode.FloatingBar)
            {
                var settings = (_floatingWindows.FirstOrDefault()?.EffectiveSettings);
                _audioMonitorService.ApplyPresentationCaptureSource(settings?.SpectrumComponent.CaptureSource);
                _fullPanels.ApplyPresentationSettings(settings);
            }
        }, DispatcherPriority.Background);
    }

    private void RuntimePruneChanged(object? sender, MemoryPruneLevelChangedEventArgs args)
    {
        var wasOff = _runtimePruneLevel >= MemoryPruneLevel.DisplayOff;
        _runtimePruneLevel = args.Level;
        if (wasOff && args.Level < MemoryPruneLevel.DisplayOff && RequestedMode == WindowMode.FloatingBar)
            Dispatcher.BeginInvoke(() => QueueRuntimeRefresh(force: true));
    }

    private WindowMode RequestedMode => SettingsManager.Current.WindowMode == WindowMode.FloatingBar ? WindowMode.FloatingBar : WindowMode.Taskbar;

    private IReadOnlyList<(string Requested, DisplayMonitorInfo Actual)> FloatingTargets()
    {
        var monitors = _displayMonitorService.GetMonitors();
        var requested = SettingsManager.Current.FloatingBar.MonitorDeviceIds;
        if (requested.Count == 0) requested = monitors.Where(m => m.IsPrimary).Select(m => m.DeviceId).ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<(string, DisplayMonitorInfo)>();
        // Prefer an explicitly connected target before adding fallbacks, so a missing target cannot steal its host.
        foreach (var id in requested.OrderBy(id => monitors.Any(m => string.Equals(m.DeviceId, id, StringComparison.OrdinalIgnoreCase)) ? 0 : 1))
        {
            var actual = _displayMonitorService.ResolveFixedMonitor(id);
            if (actual is not null && seen.Add(actual.DeviceId)) targets.Add((id, actual));
        }
        return targets;
    }

    private string RuntimeSignature(WindowMode mode) => mode == WindowMode.Taskbar
        ? mode + "|" + GetExperimentalHostSignature()
        : mode + "|" + string.Join("|", FloatingTargets().Select(t =>
            $"{t.Requested}:{t.Actual.DeviceId}:{t.Actual.MonitorArea}:{t.Actual.WorkArea}:{t.Actual.DpiX}:{t.Actual.DpiY}"));

    private void QueueRuntimeRefresh(bool force = false)
    {
        if (_isClosing) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => QueueRuntimeRefresh(force)); return; }
        var mode = RequestedMode;
        var signature = RuntimeSignature(mode);
        if (!force && (signature == _runtimeSignature || signature == _pendingRuntimeSignature)) return;
        _pendingRuntimeSignature = signature;
        var request = ++_runtimeRequest;
        _ = RefreshRuntimeAsync(mode, signature, request);
    }

    private async Task RefreshRuntimeAsync(WindowMode mode, string signature, int request)
    {
        try
        {
            CloseFullPanelWindows();
            var success = await _runtime.SwitchAsync(mode, token => PrepareHostsAsync(mode, token), force: true);
            if (_isClosing || request != _runtimeRequest) return;
            _pendingRuntimeSignature = null;
            _taskbarWindows.Clear();
            _taskbarWindows.AddRange(_runtime.Hosts.OfType<TaskbarWindow>());
            _floatingWindows.Clear();
            _floatingWindows.AddRange(_runtime.Hosts.OfType<FloatingBarWindow>());
            if (success)
            {
                _runtimeSignature = signature;
                _audioMonitorService.ApplyPresentationCaptureSource(mode == WindowMode.FloatingBar
                    ? _floatingWindows.FirstOrDefault()?.EffectiveSettings.SpectrumComponent.CaptureSource : null);
                var snapshot = _mediaSessionService.CurrentSnapshot ?? MediaSnapshot.Disconnected;
                MediaSessionService_OnSnapshotChanged(this, snapshot);
                MediaSessionService_OnSessionsChanged(_mediaSessionService.CurrentSessionOptions);
                TryNotifySmallTaskbar();
            }
            else if (_runtime.LastFailure is not null)
            {
                // 切换失败时设置仍反映保留的真实宿主。
                SettingsManager.Current.WindowMode = _runtime.RunningMode;
            }
        }
        catch (Exception ex) { _pendingRuntimeSignature = null; _logService.Error("DisplayMode", "更新媒体栏宿主失败", ex); }
    }

    private Task<IReadOnlyList<IBarRuntimeHost>> PrepareHostsAsync(WindowMode mode, CancellationToken cancellationToken)
    {
        var hosts = new List<IBarRuntimeHost>();
        try
        {
            if (mode == WindowMode.FloatingBar)
            {
                var targets = FloatingTargets();
                if (targets.Count == 0) throw new InvalidOperationException("No display is available.");
                foreach (var target in targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var window = _floatingFactory(target.Requested, target.Actual);
                    hosts.Add(window);
                    var previous = _floatingWindows.FirstOrDefault(w => string.Equals(w.TargetMonitorDeviceId, target.Requested, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(w.Monitor.DeviceId, target.Actual.DeviceId, StringComparison.OrdinalIgnoreCase));
                    if (previous is not null)
                    {
                        var previousDpi = Math.Max(1, previous.Monitor.DpiX) / 96d;
                        var nextDpi = Math.Max(1, target.Actual.DpiX) / 96d;
                        var relative = (previous.CenterPx - previous.Monitor.WorkArea.TopLeft) / previousDpi;
                        window.RestoreCenter(target.Actual.WorkArea.TopLeft + relative * nextDpi, previous.DockEdge);
                    }
                    window.ApplySnapshot(_mediaSessionService.CurrentSnapshot ?? MediaSnapshot.Disconnected);
                    window.OpenFullPanelRequested += FloatingFullPanelRequested;
                    window.PlacementTransitionStarted += (_, _) => { if (_floatingWindows.Contains(window)) CloseFullPanelWindows(); };
                    window.PlayerMenuOpenRequested += (host, point) =>
                    {
                        if (_isClosing || !_floatingWindows.Contains(host)) return;
                        TrayMenu.ApplyQuickLaunchEntries(host.EffectiveSettings.QuickLaunch.Entries ?? []);
                        var menu = host.IsPerformanceMenuRequested ? _memoryMenu : host.IsBatteryMenuRequested ? _batteryMenu : TrayMenu;
                        var hold = host.KeepEdgeExpanded();
                        RoutedEventHandler? closed = null;
                        closed = (_, _) => { menu.Closed -= closed; hold.Dispose(); };
                        menu.Closed += closed;
                        try { ContextMenuHelper.OpenAtScreenAnchor(menu, this, point); }
                        catch { menu.Closed -= closed; hold.Dispose(); throw; }
                    };
                }
            }
            else
            {
                var seen = new HashSet<nint>();
                foreach (var target in ResolveTaskbarTargetDeviceIds())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var handle = _taskBarService.GetSelectedTaskbarHandle(target, out _);
                    if (handle == nint.Zero || !seen.Add(handle)) continue;
                    var window = CreateTaskbarWindow(target);
                    hosts.Add(window);
                    window.ApplySnapshot(_mediaSessionService.CurrentSnapshot ?? MediaSnapshot.Disconnected);
                }
                if (seen.Count == 0) throw new InvalidOperationException("Taskbar is unavailable.");
            }
            return Task.FromResult<IReadOnlyList<IBarRuntimeHost>>(hosts);
        }
        catch { foreach (var host in hosts) host.Dispose(); throw; }
    }

    private void FloatingFullPanelRequested(object? sender, FullPanelOpenRequestEventArgs args)
    {
        if (_isClosing || sender is not FloatingBarWindow host || !_floatingWindows.Contains(host)) return;
        var panel = _fullPanels.Open(host.TargetMonitorDeviceId, args.Anchor, args.VerticalAnchor,
            args.EnsureOpen, host.EffectiveSettings, args.PlacementSide);
        if (panel is not null) host.SetFullPanelOpen(panel.IsVisible);
    }
}
