// 在独立 MTA 线程拥有端点和会话创建通知；AF 写入后异步落盘，停止后在拥有线程释放 COM。
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>默认记住 AF 成功写入的应用音量，只在新会话出现时恢复一次，不持续强制音量。</summary>
public sealed class ApplicationVolumeMemoryService : IHostedService, IDisposable
{
    private readonly AudioProcessInfoService _processInfo;
    private readonly ApplicationVolumeMemoryStore _store;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, EndpointWatch> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Thread _thread;
    private readonly Dictionary<string, int> _volumes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _editedSessions = new(StringComparer.Ordinal);
    private long _revision;
    private long _savedRevision;
    // AF 写入与新会话恢复共用此锁；磁盘读写始终在锁外执行。
    internal object WriteGate { get; } = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _notifications;
    private int _started;
    private int _stopping;
    private int _devicesQueued;

    public ApplicationVolumeMemoryService(AudioProcessInfoService processInfo, ApplicationVolumeMemoryStore store)
    {
        _processInfo = processInfo;
        _store = store;
        _thread = new Thread(Run) { IsBackground = true, Name = "AF Volume Memory" };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _stopping) == 0 && Interlocked.Exchange(ref _started, 1) == 0) _thread.Start();
        return Task.CompletedTask;
    }

    /// <summary>仅由 AF 的音量写入成功路径调用；不在调用线程进行磁盘或音频操作。</summary>
    public void RecordAppliedVolume(string? executablePath, int percent, string? editedSessionKey)
    {
        var path = ApplicationVolumeMemoryStore.NormalizePath(executablePath);
        if (path is null) return;
        var volume = Math.Clamp(percent, 0, 100);
        lock (WriteGate)
        {
            if (Volatile.Read(ref _stopping) != 0) return;
            if (!_volumes.TryGetValue(path, out var previous) || previous != volume) _revision++;
            _volumes[path] = volume;
            if (editedSessionKey is not null) _editedSessions.Add(editedSessionKey);
            Queue(() =>
            {
                try
                {
                    SavePendingVolumes();
                    SynchronizeDevices(path, editedSessionKey);
                }
                finally
                {
                    if (editedSessionKey is not null)
                        lock (WriteGate) _editedSessions.Remove(editedSessionKey);
                }
            });
        }
    }

    private void SavePendingVolumes()
    {
        Dictionary<string, int> snapshot;
        long revision;
        lock (WriteGate)
        {
            if (_savedRevision == _revision) return;
            revision = _revision;
            snapshot = new(_volumes, StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            _store.Save(snapshot);
            lock (WriteGate) _savedRevision = revision;
        }
        catch (Exception error) { Debug.WriteLine($"[VolumeMemory] Save: {error}"); }
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
            var saved = _store.Load();
            lock (WriteGate)
                foreach (var pair in saved) _volumes.TryAdd(pair.Key, pair.Value);
            try { SynchronizeDevices(); }
            catch (Exception error) { Debug.WriteLine($"[VolumeMemory] Start: {error}"); }
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                // 停止时排空 AF 已成功写入的记录；各原生刷新入口会拒绝新的音频操作。
                try { action(); }
                catch (Exception error) { Debug.WriteLine($"[VolumeMemory] {error}"); }
            }
        }
        catch (Exception error) { Debug.WriteLine($"[VolumeMemory] {error}"); }
        finally
        {
            Interlocked.Exchange(ref _stopping, 1);
            try { ReleaseDevices(); }
            finally
            {
                if (initialized >= 0) CoUninitialize();
                _queue.Dispose();
                _completion.TrySetResult();
            }
        }
    }

    private void SynchronizeDevices(string? editedPath = null, string? editedSessionKey = null)
    {
        if (Volatile.Read(ref _stopping) != 0) return;
        lock (WriteGate) if (_volumes.Count == 0) return;
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
                SynchronizeSessions(endpoint, editedPath, editedSessionKey);
            }
            catch (Exception error) { Debug.WriteLine($"[VolumeMemory] Endpoint: {error.Message}"); }
            finally { if (!retained) device.Dispose(); }
        }
        foreach (var id in _endpoints.Keys.Where(id => !present.Contains(id)).ToArray())
            if (_endpoints.Remove(id, out var endpoint))
                try { endpoint.Dispose(); } catch (Exception error) { Debug.WriteLine(error); }
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

    private void SynchronizeSessions(EndpointWatch endpoint, string? editedPath = null, string? editedSessionKey = null)
    {
        if (Volatile.Read(ref _stopping) != 0) return;
        endpoint.Manager.RefreshSessions();
        var sessions = endpoint.Manager.Sessions;
        var present = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < sessions.Count; index++)
        {
            using var session = sessions[index];
            string? key = null;
            try
            {
                if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                key = session.GetSessionInstanceIdentifier;
                present.Add(key);
                if (endpoint.Observed.Contains(key)) continue;
                var pid = session.GetProcessID;
                if (pid == 0 || pid == Environment.ProcessId) continue;
                var path = _processInfo.GetExecutablePath(pid);
                if (path is null) continue;
                lock (WriteGate)
                {
                    if (!_volumes.TryGetValue(path, out var saved)) continue;
                    var wasEdited = _editedSessions.Remove(key) || editedPath is not null &&
                        path.Equals(editedPath, StringComparison.OrdinalIgnoreCase) && (editedSessionKey is null || key == editedSessionKey);
                    endpoint.Observed.Add(key);
                    if (wasEdited || Volatile.Read(ref _stopping) != 0) continue;
                    var current = Math.Clamp((int)Math.Round(session.SimpleAudioVolume.Volume * 100), 0, 100);
                    if (saved != current) session.SimpleAudioVolume.Volume = saved / 100f;
                }
            }
            catch (Exception error)
            {
                if (key is not null) endpoint.Observed.Remove(key);
                Debug.WriteLine($"[VolumeMemory] Session: {error.Message}");
            }
        }
        endpoint.Observed.IntersectWith(present);
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
        public HashSet<string> Observed { get; } = new(StringComparer.Ordinal);
        public int RefreshQueued;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Created is not null) Manager.OnSessionCreated -= Created;
            Device.Dispose();
        }
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint apartment);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
