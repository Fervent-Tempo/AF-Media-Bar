// 应用级协调器拥有读取循环、观察器和提供器会话；窗口只借用概览订阅。
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using Microsoft.Extensions.Hosting;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>共享主设备及按需多设备电量，保留已发布的蓝牙设置入口。</summary>
public sealed class BluetoothBatteryService(DeviceBatteryReader reader, BluetoothBatteryCache cache,
    ISystemNotificationService notifications, AppLogService log) : IHostedService, IDisposable
{
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Dictionary<string, BluetoothBatterySnapshot> _saved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _nextRead = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly BluetoothBatteryAlertPolicy _alerts = new();
    private CancellationTokenSource? _cycle;
    private Task _loop = Task.CompletedTask;
    private int _version;
    private int _overviewCount;
    private int _forceRefresh;
    private readonly HashSet<string> _lowVisible = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private bool _started;
    private string? _selectedId;
    private int _threshold;
    public event EventHandler? Changed;
    public BluetoothBatterySnapshot? Snapshot => cache.Snapshot;
    public IReadOnlyList<BluetoothBatterySnapshot> Readings { get; private set; } = [];
    public bool IsRefreshing { get; private set; }
    public bool LastDiscoveryFailed { get; private set; }
    public bool ShouldShowWidget => WidgetReadings.Count > 0;
    public IReadOnlyList<BluetoothBatterySnapshot> WidgetReadings
    {
        get
        {
            var settings = SettingsManager.Current;
            if (!settings.Experiments.BluetoothBattery || !WidgetRequested(settings)) return [];
            return settings.BluetoothBatteryWidget.GetSelectedDevices().Select(d => Readings.FirstOrDefault(r =>
                r.DeviceId.Equals(d.Id, StringComparison.OrdinalIgnoreCase)))
                .OfType<BluetoothBatterySnapshot>().Where(r => r.IsConnected &&
                    (settings.BluetoothBatteryWidget.DisplayMode == BluetoothBatteryDisplayMode.WhenConnected ||
                     r.IsRealtime && _lowVisible.Contains(r.DeviceId))).ToArray();
        }
    }
    internal static IReadOnlyList<string> TrackingIds(BluetoothBatteryWidgetSettings widget, bool showWidget) =>
        (showWidget ? widget.GetSelectedDevices().Select(d => d.Id) : [])
        .Concat((widget.Notifications || widget.ConnectionNotifications) && widget.DeviceId is { } id ? [id] : []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public Task<IReadOnlyList<BluetoothBatteryDevice>> GetDevicesAsync(CancellationToken token = default) => reader.GetDevicesAsync(token);
    public IDisposable AcquireOverviewSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Increment(ref _overviewCount) == 1) Interlocked.Increment(ref _forceRefresh);
        Invalidate();
        return new Lease(() => { Interlocked.Decrement(ref _overviewCount); Invalidate(); });
    }
    public void RequestRefresh() { Interlocked.Increment(ref _forceRefresh); Invalidate(); }
    private void Invalidate()
    {
        if (_disposed) return;
        Interlocked.Increment(ref _version);
        try { Volatile.Read(ref _cycle)?.Cancel(); } catch (ObjectDisposedException) { }
        try { if (_signal.CurrentCount == 0) _signal.Release(); } catch (SemaphoreFullException) { }
    }
    public Task StartAsync(CancellationToken cancellationToken) => Application.Current.Dispatcher.InvokeAsync(() =>
    {
        if (_disposed || _started) return;
        _started = true;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _loop = Task.Run(RunAsync);
    }, DispatcherPriority.Normal, cancellationToken).Task;
    private static bool WidgetRequested(AppSettings settings) => new[] { BarLayoutState.Resident, BarLayoutState.Idle }
        .Any(state => ComponentLayoutPolicy.Resolve(settings, state).Any(entry => entry.Component == TaskbarRestComponent.BluetoothBattery && entry.IsVisible));
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (args.PropertyName is null or nameof(AppSettings.Experiments) or nameof(AppSettings.TaskbarExperience) or nameof(AppSettings.BluetoothBatteryWidget)) Invalidate();
    }
    private async Task RunAsync()
    {
        var token = _lifetime.Token;
        var observers = new List<Action>();
        IReadOnlyList<BluetoothBatteryDevice> catalog = [];
        var nextDiscovery = DateTimeOffset.MinValue;
        var catalogVersion = -1;
        var appliedRefresh = -1;
        try
        {
            while (!token.IsCancellationRequested)
            {
                while (_signal.Wait(0)) { }
                var version = Volatile.Read(ref _version);
                using var cycle = CancellationTokenSource.CreateLinkedTokenSource(token);
                Interlocked.Exchange(ref _cycle, cycle);
                var wait = TimeSpan.FromSeconds(60);
                try
                {
                    var config = await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var settings = SettingsManager.Current;
                        return (Enabled: settings.Experiments.BluetoothBattery, Widget: settings.BluetoothBatteryWidget,
                            TrackMain: WidgetRequested(settings) || settings.BluetoothBatteryWidget.Notifications || settings.BluetoothBatteryWidget.ConnectionNotifications,
                            Tracking: TrackingIds(settings.BluetoothBatteryWidget, WidgetRequested(settings)),
                            Overview: Volatile.Read(ref _overviewCount) > 0);
                    });
                    var main = config.Enabled && config.TrackMain ? config.Widget.DeviceId : null;
                    var overview = config.Enabled && config.Overview;
                    var tracking = config.Enabled ? config.Tracking : [];
                    if (tracking.Count == 0 && !overview)
                    {
                        ReleaseObservers(observers);
                        await reader.ReleaseExceptAsync(new HashSet<string>()).ConfigureAwait(false);
                        await PublishAsync(version, cycle.Token, () =>
                        {
                            cache.Clear(); _lowVisible.Clear(); Readings = []; IsRefreshing = false;
                            _alerts.EvaluateConnection(null, null, false);
                        });
                        nextDiscovery = DateTimeOffset.MinValue;
                        wait = Timeout.InfiniteTimeSpan;
                    }
                    else
                    {
                        // 概览关闭先释放不再追踪的设备，不等可能耗时的 HID++ 目录查询。
                        if (!overview)
                            await reader.ReleaseExceptAsync(catalog.Where(d => tracking.Any(d.Matches)).Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)).ConfigureAwait(false);
                        if (observers.Count == 0) CreateObservers(observers);
                        if (main != _selectedId || config.Widget.LowThreshold != _threshold)
                        {
                            _selectedId = main;
                            _alerts.ResetBaseline();
                        }
                        var refresh = Volatile.Read(ref _forceRefresh);
                        var force = refresh != appliedRefresh;
                        if (force || DateTimeOffset.UtcNow >= nextDiscovery || version != catalogVersion)
                        {
                            await PublishAsync(version, cycle.Token, () => { IsRefreshing = true; });
                            catalog = await reader.GetDevicesAsync(cycle.Token).ConfigureAwait(false);
                            catalogVersion = version;
                            nextDiscovery = DateTimeOffset.UtcNow.AddSeconds(60);
                            var failed = reader.HadDiscoveryFailures;
                            await PublishAsync(version, cycle.Token, () => { LastDiscoveryFailed = failed; });
                        }
                        var targets = catalog.Where(d => overview || tracking.Any(d.Matches)).ToArray();
                        await reader.ReleaseExceptAsync(targets.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)).ConfigureAwait(false);
                        var results = new List<BluetoothBatterySnapshot>();
                        foreach (var device in targets)
                        {
                            cycle.Token.ThrowIfCancellationRequested();
                            var id = config.Widget.GetSelectedDevices().FirstOrDefault(d => device.Matches(d.Id))?.Id ?? (device.Matches(main) ? main! : device.Id);
                            _saved.TryGetValue(device.Id, out var old);
                            var reading = old ?? new(id, null, null, device.IsConnected, false, null)
                            { DeviceName = device.DisplayName, Transport = device.Transport, Protocol = device.Protocol };
                            if (!device.IsConnected) reading = reading with { IsConnected = false, ReadSucceeded = false };
                            else if (force || old is null || !old.IsConnected || DateTimeOffset.UtcNow >= _nextRead.GetValueOrDefault(device.Id))
                            {
                                try
                                {
                                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cycle.Token);
                                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                                    reading = await reader.ReadAsync(device, id, timeout.Token).ConfigureAwait(false);
                                }
                                catch (Exception error) when (!cycle.IsCancellationRequested)
                                {
                                    log.Warn("Battery", "设备电量读取失败", error);
                                    reading = reading with { ReadSucceeded = false };
                                }
                                var failures = reading.ReadSucceeded ? 0 : Math.Min(_failures.GetValueOrDefault(device.Id) + 1, 4);
                                _failures[device.Id] = failures;
                                _nextRead[device.Id] = DateTimeOffset.UtcNow.AddSeconds(device.IsPassive ? 2 : failures == 0 ? 60 : Math.Min(60 * (1 << failures), 600));
                                if (!reading.ReadSucceeded && old is not null)
                                    reading = reading with { Percent = old.Percent, Source = old.Source, UpdatedAt = old.UpdatedAt, IsCharging = old.IsCharging, FreshnessKnown = old.FreshnessKnown };
                            }
                            reading = reading with { DeviceId = id, Kind = device.Kind, DeviceName = device.DisplayName };
                            if (device.Matches(main) && old?.IsConnected != reading.IsConnected) _alerts.ResetBaseline();
                            _saved[device.Id] = reading;
                            results.Add(reading);
                            if (device.IsPassive) wait = TimeSpan.FromSeconds(2);
                        }
                        if (main is not null && !results.Any(r => catalog.Any(d => d.Matches(main) && d.Matches(r.DeviceId))))
                            results.Add((Snapshot is { } previous && previous.DeviceId == main ? previous : new(main, null, null, false, false, null)
                            { DeviceName = config.Widget.LastKnownName }) with { IsConnected = false, ReadSucceeded = false });
                        cycle.Token.ThrowIfCancellationRequested();
                        await PublishAsync(version, cycle.Token, () =>
                        {
                            Readings = results.ToArray(); IsRefreshing = false;
                            appliedRefresh = refresh;
                            var selected = results.FirstOrDefault(r => r.DeviceId.Equals(main, StringComparison.OrdinalIgnoreCase) ||
                                catalog.Any(d => d.Matches(main) && d.Matches(r.DeviceId)));
                            if (selected is not null) selected = selected with { DeviceId = main! };
                            cache.Publish(selected);
                            if (_threshold != config.Widget.LowThreshold) { _lowVisible.Clear(); _threshold = config.Widget.LowThreshold; }
                            foreach (var reading in results.Where(r => r.IsRealtime))
                            {
                                if (reading.Percent <= config.Widget.LowThreshold) _lowVisible.Add(reading.DeviceId);
                                else if (reading.Percent >= config.Widget.LowThreshold + 5) _lowVisible.Remove(reading.DeviceId);
                            }
                            if (_alerts.Evaluate(main, selected, config.Widget.Notifications, config.Widget.LowThreshold))
                                Notify(selected!, false);
                            if (_alerts.EvaluateConnection(main, selected, config.Widget.ConnectionNotifications))
                                Notify(selected!, true);
                        });
                        var retained = catalog.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        foreach (var id in _saved.Keys.Where(id => !retained.Contains(id)).ToArray())
                        { _saved.Remove(id); _nextRead.Remove(id); _failures.Remove(id); }
                    }
                }
                catch (OperationCanceledException) when (cycle.IsCancellationRequested) { }
                catch (Exception error)
                {
                    log.Warn("Battery", "设备目录读取失败，稍后重试", error);
                    await PublishAsync(version, cycle.Token, () =>
                    {
                        LastDiscoveryFailed = true; IsRefreshing = false;
                        Readings = Readings.Select(r => r with { ReadSucceeded = false }).ToArray();
                        if (Snapshot is { } previous) cache.Publish(previous with { ReadSucceeded = false });
                    });
                }
                finally { Interlocked.CompareExchange(ref _cycle, null, cycle); }
                if (version != Volatile.Read(ref _version)) continue;
                await _signal.WaitAsync(wait, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            ReleaseObservers(observers);
            await reader.ReleaseExceptAsync(new HashSet<string>()).ConfigureAwait(false);
        }
    }
    private async Task PublishAsync(int version, CancellationToken token, Action apply)
    {
        if (!CanPublish(version, token, _version, _disposed)) return;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!CanPublish(version, token, _version, _disposed)) return;
            apply(); Changed?.Invoke(this, EventArgs.Empty);
        });
    }
    internal static bool CanPublish(int version, CancellationToken token, int currentVersion, bool disposed) =>
        !disposed && !token.IsCancellationRequested && version == currentVersion;
    internal static (string Title, string Body) NotificationText(BluetoothBatterySnapshot snapshot, bool connected) => connected
        ? (Translations.Get("Battery.ConnectionTitle"), Translations.Format("Battery.ConnectionBody", snapshot.DeviceName, snapshot.Label))
        : (Translations.Get("Battery.LowTitle"), Translations.Format("Battery.LowBody", snapshot.DeviceName, snapshot.Percent));
    private void Notify(BluetoothBatterySnapshot snapshot, bool connected)
    {
        var (title, body) = NotificationText(snapshot, connected);
        try { notifications.TryShowNotification(title, body, ShellNotificationTarget.None, connected ? "Battery.Connected" : "Battery.Low"); }
        catch (Exception error) { log.Warn("Battery", "设备电量通知失败", error); }
    }
    private void CreateObservers(List<Action> observers)
    {
        foreach (var selector in new[] { BluetoothBatteryReader.Selector(false), BluetoothBatteryReader.Selector(true),
            "System.Devices.InterfaceClassGuid:=\"{4D1E55B2-F16F-11CF-88CB-001111000030}\"" })
        {
            var watcher = DeviceInformation.CreateWatcher(selector);
            TypedEventHandler<DeviceWatcher, DeviceInformation> added = (_, _) => Invalidate();
            TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> changed = (_, _) => Invalidate();
            watcher.Added += added; watcher.Updated += changed; watcher.Removed += changed;
            observers.Add(() =>
            {
                watcher.Added -= added; watcher.Updated -= changed; watcher.Removed -= changed;
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
            });
            try { watcher.Start(); }
            catch (Exception error) { log.Warn("Battery", "设备观察器不可用，继续低频查询", error); }
        }
    }
    private static void ReleaseObservers(List<Action> observers)
    {
        foreach (var release in observers) { try { release(); } catch (Exception) { } }
        observers.Clear();
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _lifetime.Cancel();
        try { Volatile.Read(ref _cycle)?.Cancel(); } catch (ObjectDisposedException) { }
        cache.Clear(); Readings = []; Changed = null;
    }
}
