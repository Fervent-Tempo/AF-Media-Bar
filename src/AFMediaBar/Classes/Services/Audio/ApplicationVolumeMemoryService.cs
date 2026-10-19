// 在独立 MTA 线程拥有端点、会话及通知；回调只排队，停止后在同一线程解除订阅并释放 COM。
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using Microsoft.Extensions.Hosting;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>仅为用户选择的应用恢复新会话音量；后续变化更新记忆，不持续强制音量。</summary>
public sealed class ApplicationVolumeMemoryService : IHostedService, IDisposable
{
    private readonly AudioProcessInfoService _processInfo;
    private readonly Dispatcher _uiDispatcher = DispatcherHelper.Current;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, EndpointWatch> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int Version, int Volume)> _pendingVolumes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Thread _thread;
    private ApplicationVolumeMemoryEntry[] _entries = [];
    private Dictionary<string, int> _enrollments = new(StringComparer.OrdinalIgnoreCase);
    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _notifications;
    private int _started;
    private int _stopping;
    private int _nextEnrollment;
    private int _devicesQueued;

    public ApplicationVolumeMemoryService(AudioProcessInfoService processInfo)
    {
        _processInfo = processInfo;
        _thread = new Thread(Run) { IsBackground = true, Name = "AF Volume Memory" };
        // Core Audio 的会话创建通知要求在非 UI 的 MTA 中注册，不能借用现有 STA 音频线程。
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _stopping) != 0 || Interlocked.Exchange(ref _started, 1) != 0) return Task.CompletedTask;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _thread.Start();
        UpdateConfiguration();
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (args.PropertyName is null or nameof(AppSettings.ApplicationVolumeMemory)) UpdateConfiguration();
    }

    private void UpdateConfiguration()
    {
        if (Volatile.Read(ref _stopping) != 0) return;
        if (!_uiDispatcher.CheckAccess())
        {
            if (!_uiDispatcher.HasShutdownStarted) _uiDispatcher.BeginInvoke(() => UpdateConfiguration());
            return;
        }
        var entries = SettingsManager.Current.ApplicationVolumeMemory.Entries.ToArray();
        var previous = Volatile.Read(ref _entries);
        var enrollments = Volatile.Read(ref _enrollments);
        var next = entries.ToDictionary(entry => entry.ExecutablePath,
            entry => enrollments.TryGetValue(entry.ExecutablePath, out var version) ? version : ++_nextEnrollment,
            StringComparer.OrdinalIgnoreCase);
        Volatile.Write(ref _entries, entries);
        Volatile.Write(ref _enrollments, next);
        // 音量更新不会重建订阅或再次恢复；只有启用列表改变才重新枚举。
        if (previous.Length == 0 || !previous.Select(entry => entry.ExecutablePath)
            .SequenceEqual(entries.Select(entry => entry.ExecutablePath), StringComparer.OrdinalIgnoreCase)) QueueDevices();
    }

    private void Queue(Action action)
    {
        if (Volatile.Read(ref _stopping) != 0) return;
        try { _queue.Add(action); }
        catch (InvalidOperationException) { }
    }

    private void QueueDevices()
    {
        if (Interlocked.Exchange(ref _devicesQueued, 1) != 0) return;
        Queue(() => { Interlocked.Exchange(ref _devicesQueued, 0); SynchronizeDevices(); });
    }

    private void Run()
    {
        var initialized = CoInitializeEx(IntPtr.Zero, 0);
        try
        {
            Marshal.ThrowExceptionForHR(initialized);
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                if (Volatile.Read(ref _stopping) != 0) break;
                try { action(); }
                catch (Exception error) { Debug.WriteLine($"[VolumeMemory] {error}"); }
            }
        }
        catch (Exception error) { Debug.WriteLine($"[VolumeMemory] {error}"); }
        finally
        {
            Interlocked.Exchange(ref _stopping, 1);
            SettingsManager.SettingsChanged -= OnSettingsChanged;
            try { ReleaseDevices(); }
            finally
            {
                if (initialized >= 0) CoUninitialize();
                _queue.Dispose();
                _completion.TrySetResult();
            }
        }
    }

    private void SynchronizeDevices()
    {
        if (Volatile.Read(ref _entries).Length == 0) { ReleaseDevices(); return; }
        if (_enumerator is null)
        {
            _enumerator = new MMDeviceEnumerator();
            _notifications = _enumerator.CreateNotificationClient(false);
            _notifications.DeviceAdded += (_, _) => QueueDevices();
            _notifications.DeviceRemoved += (_, _) => QueueDevices();
            _notifications.DeviceStateChanged += (_, _) => QueueDevices();
            _notifications.DefaultDeviceChanged += (_, _) => QueueDevices();
        }
        using var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            var retained = false;
            try
            {
                var id = device.ID;
                present.Add(id);
                if (!_endpoints.TryGetValue(id, out var endpoint))
                {
                    endpoint = new EndpointWatch(device);
                    endpoint.Created = (_, _) => QueueSessions(endpoint);
                    endpoint.Manager.OnSessionCreated += endpoint.Created;
                    _endpoints.Add(id, endpoint);
                    retained = true;
                }
                SynchronizeSessions(endpoint);
            }
            catch (Exception error) { Debug.WriteLine($"[VolumeMemory] Endpoint: {error.Message}"); }
            finally { if (!retained) device.Dispose(); }
        }
        foreach (var id in _endpoints.Keys.Where(id => !present.Contains(id)).ToArray())
        {
            if (_endpoints.Remove(id, out var endpoint))
                try { endpoint.Dispose(); } catch (Exception error) { Debug.WriteLine(error); }
        }
    }

    private void QueueSessions(EndpointWatch endpoint)
    {
        if (Interlocked.Exchange(ref endpoint.RefreshQueued, 1) != 0) return;
        Queue(() =>
        {
            Interlocked.Exchange(ref endpoint.RefreshQueued, 0);
            if (_endpoints.TryGetValue(endpoint.Id, out var current) && ReferenceEquals(current, endpoint))
                SynchronizeSessions(endpoint);
        });
    }

    private void SynchronizeSessions(EndpointWatch endpoint)
    {
        endpoint.Manager.RefreshSessions();
        var sessions = endpoint.Manager.Sessions;
        var present = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            var retained = false;
            SessionWatch? watch = null;
            string? attachedKey = null;
            try
            {
                if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                var key = session.GetSessionInstanceIdentifier;
                if (endpoint.Sessions.TryGetValue(key, out var existing))
                {
                    if (Enrollment(existing.Path) == existing.Enrollment && FindPreference(existing.Path) is not null)
                    {
                        present.Add(key);
                        continue;
                    }
                    endpoint.Sessions.Remove(key);
                    existing.Dispose();
                }
                var pid = session.GetProcessID;
                if (pid == 0 || pid == Environment.ProcessId) continue;
                var path = _processInfo.GetExecutablePath(pid);
                var version = Enrollment(path);
                var preference = FindPreference(path);
                if (preference is null || version == 0) continue;
                present.Add(key);
                watch = new SessionWatch(session, preference.ExecutablePath, version);
                var owned = watch;
                watch.VolumeChanged = () => QueueVolume(endpoint, key, owned);
                watch.Ended = () => QueueSessions(endpoint);
                endpoint.Sessions.Add(key, watch);
                attachedKey = key;
                retained = true;
                session.RegisterEventClient(watch);
                var current = ReadVolume(session);
                if (Volatile.Read(ref _stopping) != 0 || version != Enrollment(path) || FindPreference(path) is null)
                {
                    endpoint.Sessions.Remove(key);
                    watch.Dispose();
                    continue;
                }
                var saved = _pendingVolumes.TryGetValue(preference.ExecutablePath, out var pending) && pending.Version == version
                    ? pending.Volume : FindPreference(path)?.VolumePercent;
                watch.LastVolume = saved ?? current;
                if (saved is { } target && target != current &&
                    Volatile.Read(ref _stopping) == 0 && version == Enrollment(path) && FindPreference(path) is not null)
                    session.SimpleAudioVolume.Volume = target / 100f;
                else if (saved is null) Remember(watch.Path, current, version);
            }
            catch (Exception error)
            {
                Debug.WriteLine($"[VolumeMemory] Session: {error.Message}");
                if (attachedKey is not null)
                {
                    endpoint.Sessions.Remove(attachedKey);
                    watch?.Dispose();
                }
            }
            finally { if (!retained) session.Dispose(); }
        }
        foreach (var key in endpoint.Sessions.Keys.Where(key => !present.Contains(key)).ToArray())
        {
            if (endpoint.Sessions.Remove(key, out var watch))
                try { watch.Dispose(); } catch (Exception error) { Debug.WriteLine(error); }
        }
    }

    private ApplicationVolumeMemoryEntry? FindPreference(string? path) => path is null ? null :
        Volatile.Read(ref _entries).FirstOrDefault(entry => entry.ExecutablePath.Equals(path, StringComparison.OrdinalIgnoreCase));

    private int Enrollment(string? path) => path is null ? 0 : Volatile.Read(ref _enrollments).GetValueOrDefault(path);

    private static int ReadVolume(AudioSessionControl session) => Math.Clamp((int)Math.Round(session.SimpleAudioVolume.Volume * 100), 0, 100);

    private void QueueVolume(EndpointWatch endpoint, string key, SessionWatch watch)
    {
        if (Interlocked.Exchange(ref watch.ReadQueued, 1) != 0) return;
        Queue(() =>
        {
            Interlocked.Exchange(ref watch.ReadQueued, 0);
            if (!endpoint.Sessions.TryGetValue(key, out var current) || !ReferenceEquals(current, watch) ||
                Enrollment(watch.Path) != watch.Enrollment || FindPreference(watch.Path) is null ||
                watch.Session.State == AudioSessionState.AudioSessionStateExpired) return;
            // 回读最新值，避免恢复产生的迟到通知覆盖随后由用户设置的音量。
            var volume = ReadVolume(watch.Session);
            if (volume == watch.LastVolume || Enrollment(watch.Path) != watch.Enrollment) return;
            watch.LastVolume = volume;
            Remember(watch.Path, volume, watch.Enrollment);
        });
    }

    private void Remember(string path, int volume, int version)
    {
        if (Volatile.Read(ref _stopping) != 0 || version != Enrollment(path) || _uiDispatcher.HasShutdownStarted) return;
        _pendingVolumes[path] = (version, volume);
        _uiDispatcher.BeginInvoke(() =>
        {
            try
            {
                if (Volatile.Read(ref _stopping) != 0 || version != Enrollment(path)) return;
                var settings = SettingsManager.Current.ApplicationVolumeMemory;
                var entry = settings.Entries.FirstOrDefault(entry => entry.ExecutablePath.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (entry is null || entry.VolumePercent == volume) return;
                SettingsManager.SetApplicationVolumeMemorySettings(settings with
                {
                    Entries = settings.Entries.Select(candidate => ReferenceEquals(candidate, entry) ? candidate with { VolumePercent = volume } : candidate).ToArray()
                });
            }
            catch (Exception error) { Debug.WriteLine($"[VolumeMemory] Save: {error}"); }
            finally
            {
                Queue(() =>
                {
                    if (_pendingVolumes.TryGetValue(path, out var pending) && pending == (version, volume))
                        _pendingVolumes.Remove(path);
                });
            }
        }, DispatcherPriority.Background);
    }

    private void ReleaseDevices()
    {
        foreach (var endpoint in _endpoints.Values)
            try { endpoint.Dispose(); } catch (Exception error) { Debug.WriteLine(error); }
        _endpoints.Clear();
        try { _notifications?.Dispose(); } catch (Exception error) { Debug.WriteLine(error); }
        _notifications = null;
        try { _enumerator?.Dispose(); } catch (Exception error) { Debug.WriteLine(error); }
        _enumerator = null;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return _completion.Task.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _queue.CompleteAdding();
        if (Volatile.Read(ref _started) == 0) { _queue.Dispose(); _completion.TrySetResult(); }
    }

    private sealed class EndpointWatch(MMDevice device) : IDisposable
    {
        private bool _disposed;
        public MMDevice Device { get; } = device;
        public string Id { get; } = device.ID;
        public AudioSessionManager Manager { get; } = device.AudioSessionManager;
        public AudioSessionManager.SessionCreatedDelegate? Created;
        public Dictionary<string, SessionWatch> Sessions { get; } = new(StringComparer.Ordinal);
        public int RefreshQueued;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Created is not null) Manager.OnSessionCreated -= Created;
            foreach (var session in Sessions.Values)
                try { session.Dispose(); } catch (Exception error) { Debug.WriteLine(error); }
            Sessions.Clear();
            Device.Dispose();
        }
    }

    private sealed class SessionWatch(AudioSessionControl session, string path, int enrollment) : IAudioSessionEventsHandler, IDisposable
    {
        private bool _disposed;
        public AudioSessionControl Session { get; } = session;
        public string Path { get; } = path;
        public int Enrollment { get; } = enrollment;
        public int LastVolume;
        public int ReadQueued;
        public Action? VolumeChanged;
        public Action? Ended;
        public void OnVolumeChanged(float volume, bool isMuted) => VolumeChanged?.Invoke();
        public void OnStateChanged(AudioSessionState state) { if (state == AudioSessionState.AudioSessionStateExpired) Ended?.Invoke(); }
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => Ended?.Invoke();
        public void OnDisplayNameChanged(string displayName) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            VolumeChanged = null; Ended = null;
            try { Session.UnRegisterEventClient(this); }
            finally { Session.Dispose(); }
        }
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint apartment);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
