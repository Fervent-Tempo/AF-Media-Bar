// 实验开启期间拥有电量读取循环；设置、设备事件和退出通过取消/代际阻止迟到结果与通知。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using Microsoft.Extensions.Hosting;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>只监测相关音频设备，读取失败退避；低电量提醒只针对当前系统输出。</summary>
public sealed class BluetoothBatteryService(IAudioDeviceCatalog devices, BluetoothBatteryReader reader,
    BluetoothBatteryCache cache, ISystemNotificationService notifications, AppLogService log) : IHostedService, IDisposable
{
    private sealed class Session
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public SemaphoreSlim Signal { get; } = new(0, 1);
        public Task Loop { get; set; } = Task.CompletedTask;
        public int Version;
        public int NotificationVersion;
        public void Cancel() { try { Cancellation.Cancel(); } catch (ObjectDisposedException) { } }
        public void Wake()
        {
            try { if (Signal.CurrentCount == 0) Signal.Release(); }
            catch (SemaphoreFullException) { }
            catch (ObjectDisposedException) { }
        }
    }
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly List<Task> _loops = [];
    private Session? _session;
    private bool _disposed;
    private bool _notifyEnabled;
    private int _threshold;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        SettingsManager.SettingsChanged += OnSettingsChanged;
        devices.Changed += OnDevicesChanged;
        Reconfigure();
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_disposed || args.PropertyName is not (null or nameof(AppSettings.Experiments) or nameof(AppSettings.AudioDevices))) return;
        Reconfigure();
    }

    private void Reconfigure()
    {
        if (_disposed) return;
        var settings = SettingsManager.Current;
        var enabled = settings.Experiments.NewAudioControl && settings.Experiments.BluetoothBattery;
        var notify = settings.AudioDevices.LowBatteryNotifications;
        var threshold = settings.AudioDevices.LowBatteryThreshold;
        if (notify != _notifyEnabled || threshold != _threshold)
        {
            _notifyEnabled = notify; _threshold = threshold;
            if (_session is { } current) Interlocked.Increment(ref current.NotificationVersion);
        }
        if (!enabled)
        {
            var old = Interlocked.Exchange(ref _session, null);
            old?.Cancel();
            cache.Clear();
        }
        else if (_session is null)
        {
            var session = new Session();
            _session = session;
            session.Loop = Task.Run(() => RunAsync(session));
            _loops.RemoveAll(task => task.IsCompleted);
            _loops.Add(session.Loop);
        }
        else _session?.Wake();
    }

    private void OnDevicesChanged(object? sender, EventArgs args)
    {
        if (_disposed || _session is not { } session) return;
        Interlocked.Increment(ref session.Version);
        session.Wake();
    }

    private async Task RunAsync(Session session)
    {
        var token = session.Cancellation.Token;
        var nextRead = DateTimeOffset.MinValue;
        var failures = 0;
        Guid? target = null;
        var notificationVersion = -1;
        var alerts = new BluetoothBatteryAlertPolicy();
        try
        {
            while (!token.IsCancellationRequested)
            {
                var version = Volatile.Read(ref session.Version);
                var output = await devices.GetDevicesAsync(AudioDeviceDirection.Output, true, token).ConfigureAwait(false);
                var input = await devices.GetDevicesAsync(AudioDeviceDirection.Input, true, token).ConfigureAwait(false);
                var selected = output.FirstOrDefault(device => device.IsDefault);
                var currentTarget = selected?.ContainerId;
                var currentNotificationVersion = Volatile.Read(ref session.NotificationVersion);
                if (currentTarget != target || notificationVersion != currentNotificationVersion)
                {
                    alerts.ResetBaseline();
                    target = currentTarget; notificationVersion = currentNotificationVersion;
                }
                var previous = cache.Get(target);
                if (previous?.IsConnected == false) alerts.ResetBaseline();
                if (DateTimeOffset.UtcNow >= nextRead)
                {
                    var read = false;
                    IReadOnlyDictionary<Guid, BluetoothBatterySnapshot> snapshots = new Dictionary<Guid, BluetoothBatterySnapshot>();
                    try
                    {
                        await _readGate.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                            timeout.CancelAfter(TimeSpan.FromSeconds(20));
                            snapshots = await reader.ReadAsync(output.Concat(input).ToArray(), timeout.Token).ConfigureAwait(false);
                            read = true;
                        }
                        finally { _readGate.Release(); }
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                    catch (Exception exception) when (!token.IsCancellationRequested) { log.Warn("Bluetooth", "电量读取失败", exception); }
                    if (token.IsCancellationRequested || !ReferenceEquals(_session, session) || _disposed) return;
                    failures = read ? 0 : Math.Min(failures + 1, 4);
                    nextRead = DateTimeOffset.UtcNow.AddSeconds(read ? 60 : Math.Min(60 * (1 << failures), 600));
                    if (version != Volatile.Read(ref session.Version)) { alerts.ResetBaseline(); continue; }
                    var merged = new Dictionary<Guid, BluetoothBatterySnapshot>();
                    foreach (var endpoint in output.Concat(input).Where(device => device.ContainerId is not null))
                    {
                        var id = endpoint.ContainerId!.Value;
                        var old = cache.Get(id);
                        var fresh = snapshots.GetValueOrDefault(id);
                        if (fresh is null && old is null) continue;
                        merged[id] = fresh?.ReadSucceeded == true ? fresh : (fresh ?? old!) with
                        {
                            Percent = old?.Percent,
                            Source = old?.Source,
                            UpdatedAt = old?.UpdatedAt,
                            ReadSucceeded = false,
                            IsConnected = read && fresh?.IsConnected == true
                        };
                    }
                    var published = false;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!_disposed && !token.IsCancellationRequested && ReferenceEquals(_session, session) &&
                            version == Volatile.Read(ref session.Version))
                        {
                            cache.Replace(merged);
                            published = true;
                        }
                    });
                    if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_session, session)) return;
                    if (!published) { alerts.ResetBaseline(); continue; }
                    var battery = target is { } physical ? snapshots.GetValueOrDefault(physical) : null;
                    if (alerts.Evaluate(target, battery, Volatile.Read(ref _notifyEnabled), Volatile.Read(ref _threshold)))
                    {
                        var liveOutput = await devices.GetDevicesAsync(AudioDeviceDirection.Output, true, token).ConfigureAwait(false);
                        if (liveOutput.FirstOrDefault(device => device.IsDefault)?.ContainerId == target)
                        {
                            var noticeVersion = Volatile.Read(ref session.Version);
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_session, session) ||
                                    noticeVersion != Volatile.Read(ref session.Version) || currentNotificationVersion != Volatile.Read(ref session.NotificationVersion) || !_notifyEnabled) return;
                                try { notifications.TryShowNotification(Translations.Get("Battery.LowTitle"), Translations.Format("Battery.LowBody", selected?.DisplayName ?? string.Empty, battery!.Percent)); }
                                catch (Exception exception) { log.Warn("Bluetooth", "低电量通知发送失败", exception); }
                            });
                        }
                    }
                }
                var delay = nextRead - DateTimeOffset.UtcNow;
                await session.Signal.WaitAsync(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!_disposed) log.Warn("Bluetooth", "电量监测已停止", exception); }
        finally { Interlocked.CompareExchange(ref _session, null, session); session.Signal.Dispose(); session.Cancellation.Dispose(); }
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
        devices.Changed -= OnDevicesChanged;
        Interlocked.Exchange(ref _session, null)?.Cancel();
        cache.Clear();
    }
}
