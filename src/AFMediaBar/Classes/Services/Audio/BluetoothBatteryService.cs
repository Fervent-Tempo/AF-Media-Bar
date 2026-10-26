// 只追踪用户所选设备；服务拥有读取循环及设备观察器，停用后取消并阻止迟到发布。
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using Microsoft.Extensions.Hosting;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>所选蓝牙设备的电量与提醒，独立于默认音频设备。</summary>
public sealed class BluetoothBatteryService(BluetoothBatteryReader reader, BluetoothBatteryCache cache,
    ISystemNotificationService notifications, AppLogService log) : IHostedService, IDisposable
{
    private sealed class Session(string deviceId)
    {
        public string DeviceId { get; } = deviceId;
        public CancellationTokenSource Cancellation { get; } = new();
        public SemaphoreSlim Signal { get; } = new(0, 1);
        public string[] PeerIds = [];
        public int Version;
        public Task Loop { get; set; } = Task.CompletedTask;
        public void Wake()
        {
            try { if (Signal.CurrentCount == 0) Signal.Release(); }
            catch (ObjectDisposedException) { }
            catch (SemaphoreFullException) { }
        }
        public void Cancel() { try { Cancellation.Cancel(); } catch (ObjectDisposedException) { } }
    }
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly List<Task> _loops = [];
    private Session? _session;
    private BluetoothBatteryWidgetSettings? _configuration;
    private DateTimeOffset _lastReadAttempt;
    private bool _lowVisible;
    private bool _disposed;
    private bool _started;
    public event EventHandler? Changed;
    public BluetoothBatterySnapshot? Snapshot => cache.Snapshot;
    public bool ShouldShowWidget
    {
        get
        {
            var settings = SettingsManager.Current;
            var widget = settings.BluetoothBatteryWidget;
            return settings.Experiments.BluetoothBattery && widget.Enabled && Snapshot is { IsConnected: true } snapshot &&
                snapshot.DeviceId == widget.DeviceId && (widget.DisplayMode == BluetoothBatteryDisplayMode.WhenConnected || snapshot.IsRealtime && _lowVisible);
        }
    }
    public Task<IReadOnlyList<BluetoothBatteryDevice>> GetDevicesAsync(CancellationToken token = default) => reader.ListPairedAsync(token);
    public void RequestRefresh() => _session?.Wake();
    public Task StartAsync(CancellationToken cancellationToken) => Application.Current.Dispatcher.InvokeAsync(() =>
    {
        if (_disposed || _started) return;
        _started = true;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        Reconfigure();
    }, DispatcherPriority.Normal, cancellationToken).Task;
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_disposed || args.PropertyName is not (null or nameof(AppSettings.Experiments) or nameof(AppSettings.BluetoothBatteryWidget))) return;
        // 初始状态和配置变更也会通知呈现订阅，与读取结果使用同一 UI 线程。
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess()) Reconfigure();
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(Reconfigure);
    }
    private void Reconfigure()
    {
        if (_disposed) return;
        var settings = SettingsManager.Current.BluetoothBatteryWidget;
        var enabled = SettingsManager.Current.Experiments.BluetoothBattery && settings.DeviceId is not null && (settings.Enabled || settings.Notifications);
        if (_configuration == settings && enabled == (_session is not null)) return;
        // 仅改变组件显隐不重建会话，避免重置同一设备的提醒周期和重复创建观察器。
        if (enabled && _session is not null && _configuration is { } previous && previous.DeviceId == settings.DeviceId &&
            previous.Notifications == settings.Notifications && previous.LowThreshold == settings.LowThreshold)
        {
            _configuration = settings;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        var previousId = _configuration?.DeviceId;
        if (_configuration?.LowThreshold != settings.LowThreshold) _lowVisible = false;
        _configuration = settings;
        Interlocked.Exchange(ref _session, null)?.Cancel();
        if (previousId != settings.DeviceId) { _lastReadAttempt = default; _lowVisible = false; cache.Clear(); }
        if (!enabled) { cache.Clear(); _lowVisible = false; }
        else
        {
            var session = new Session(settings.DeviceId!);
            _session = session;
            session.Loop = Task.Run(() => RunAsync(session));
            _loops.RemoveAll(task => task.IsCompleted);
            _loops.Add(session.Loop);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private async Task RunAsync(Session session)
    {
        var token = session.Cancellation.Token;
        var alerts = new BluetoothBatteryAlertPolicy();
        var observers = new List<Action>();
        var nextRead = _lastReadAttempt.AddMinutes(1);
        var failures = 0;
        bool? connected = null;
        try
        {
            foreach (var le in new[] { false, true })
            {
                var watcher = DeviceInformation.CreateWatcher(BluetoothBatteryReader.Selector(le), BluetoothBatteryReader.DeviceProperties, DeviceInformationKind.AssociationEndpoint);
                void Wake(string? id)
                {
                    if (token.IsCancellationRequested || id is not null && !id.Equals(session.DeviceId, StringComparison.OrdinalIgnoreCase) &&
                        !Volatile.Read(ref session.PeerIds).Contains(id, StringComparer.OrdinalIgnoreCase)) return;
                    if (id is not null) Interlocked.Increment(ref session.Version);
                    session.Wake();
                }
                TypedEventHandler<DeviceWatcher, DeviceInformation> added = (_, device) => Wake(device.Id);
                TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> updated = (_, device) => Wake(device.Id);
                TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> removed = (_, device) => Wake(device.Id);
                TypedEventHandler<DeviceWatcher, object> completed = (_, _) => Wake(null);
                watcher.Added += added; watcher.Updated += updated; watcher.Removed += removed; watcher.EnumerationCompleted += completed;
                observers.Add(() =>
                {
                    watcher.Added -= added; watcher.Updated -= updated; watcher.Removed -= removed; watcher.EnumerationCompleted -= completed;
                    if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
                });
                try { watcher.Start(); }
                catch (Exception error) { log.Warn("Bluetooth", "设备事件观察不可用，继续低频查询", error); }
            }
            while (!token.IsCancellationRequested)
            {
                var version = Volatile.Read(ref session.Version);
                IReadOnlyList<BluetoothBatteryDevice> devices;
                try { devices = await reader.ListPairedAsync(token).ConfigureAwait(false); }
                catch (Exception error) when (!token.IsCancellationRequested)
                {
                    failures = Math.Min(failures + 1, 4);
                    log.Warn("Bluetooth", "配对设备目录读取失败，稍后重试", error);
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_session, session)) return;
                        if (cache.Snapshot is { } previous) cache.Publish(previous with { ReadSucceeded = false });
                        Changed?.Invoke(this, EventArgs.Empty);
                    });
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(60 * (1 << failures), 600)), token).ConfigureAwait(false);
                    continue;
                }
                var device = devices.FirstOrDefault(device => device.Matches(session.DeviceId));
                Volatile.Write(ref session.PeerIds, device?.Peers.Select(peer => peer.Id).ToArray() ?? []);
                var isConnected = device?.IsConnected == true;
                if (connected != isConnected)
                {
                    alerts.ResetBaseline();
                    if (isConnected && (connected == false || cache.Snapshot is null)) nextRead = DateTimeOffset.MinValue;
                    connected = isConnected;
                }
                var old = cache.Snapshot is { } saved && saved.DeviceId == session.DeviceId ? saved : null;
                var reading = old is null
                    ? new BluetoothBatterySnapshot(session.DeviceId, null, null, isConnected, false, null) { DeviceName = device?.DisplayName ?? SettingsManager.Current.BluetoothBatteryWidget.LastKnownName }
                    : old with { IsConnected = isConnected };
                if (isConnected && device is not null && DateTimeOffset.UtcNow >= nextRead)
                {
                    await _readGate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(20));
                        _lastReadAttempt = DateTimeOffset.UtcNow;
                        reading = await reader.ReadAsync(device, session.DeviceId, timeout.Token).ConfigureAwait(false);
                        failures = reading.ReadSucceeded ? 0 : Math.Min(failures + 1, 4);
                    }
                    catch (Exception error) when (!token.IsCancellationRequested)
                    {
                        failures = Math.Min(failures + 1, 4);
                        reading = reading with { ReadSucceeded = false };
                        log.Warn("Bluetooth", "所选设备电量读取失败", error);
                    }
                    finally { _readGate.Release(); }
                    nextRead = DateTimeOffset.UtcNow.AddSeconds(failures == 0 ? 60 : Math.Min(60 * (1 << failures), 600));
                    if (!reading.ReadSucceeded && old is not null)
                        reading = reading with { Percent = old.Percent, Source = old.Source, UpdatedAt = old.UpdatedAt };
                }
                if (!isConnected) reading = reading with { ReadSucceeded = false };
                if (token.IsCancellationRequested || _disposed || !ReferenceEquals(_session, session)) return;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_session, session) ||
                        version != Volatile.Read(ref session.Version) || SettingsManager.Current.BluetoothBatteryWidget.DeviceId != session.DeviceId) return;
                    var configuration = SettingsManager.Current.BluetoothBatteryWidget;
                    if (reading.IsRealtime)
                    {
                        if (reading.Percent <= configuration.LowThreshold) _lowVisible = true;
                        else if (reading.Percent >= configuration.LowThreshold + 5) _lowVisible = false;
                    }
                    cache.Publish(reading);
                    Changed?.Invoke(this, EventArgs.Empty);
                    if (alerts.Evaluate(session.DeviceId, reading, configuration.Notifications, configuration.LowThreshold))
                    {
                        try { notifications.TryShowNotification(Translations.Get("Battery.LowTitle"), Translations.Format("Battery.LowBody", reading.DeviceName, reading.Percent), AFMediaBar.Classes.Models.ShellNotificationTarget.None, "Battery.Low"); }
                        catch (Exception error) { log.Warn("Bluetooth", "低电量通知失败", error); }
                    }
                });
                await session.Signal.WaitAsync(TimeSpan.FromSeconds(60), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposed) log.Warn("Bluetooth", "所选设备追踪已停止", error); }
        finally
        {
            foreach (var release in observers) { try { release(); } catch (Exception) { } }
            Interlocked.CompareExchange(ref _session, null, session);
            session.Signal.Dispose(); session.Cancellation.Dispose();
        }
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        await Task.WhenAll(_loops.ToArray()).WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        Interlocked.Exchange(ref _session, null)?.Cancel();
        cache.Clear();
        Changed = null;
    }
}
