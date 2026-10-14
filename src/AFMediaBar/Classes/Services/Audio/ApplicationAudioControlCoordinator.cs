// 串行协调同一应用的音量和静音意图，淘汰旧请求；容器拥有协调器，停止后排空任务再释放队列。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>音量/静音写入及回读边界，不向 UI 传递原生会话。</summary>
public interface IApplicationAudioControl
{
    ApplicationAudioControlResult Apply(string processName, int? volumePercent, bool? muted, Func<bool> isCurrent);
}

/// <summary>回读状态及请求代际；失败和部分成功不能伪装为完整应用。</summary>
public sealed record ApplicationAudioControlResult(AudioDeviceOperationResult Operation, ApplicationVolumeSnapshot? Snapshot = null, long Version = 0);

/// <summary>按应用隔离、音量与静音共享的最新请求队列。</summary>
public sealed class ApplicationAudioControlCoordinator : IDisposable
{
    private readonly IApplicationAudioControl _control;
    private readonly TimeSpan _volumeDelay;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stop = new();
    private bool _disposed;
    private bool _stopped;
    private int _pending;

    public ApplicationAudioControlCoordinator(IApplicationAudioControl control)
        : this(control, TimeSpan.FromMilliseconds(AudioApplyPolicy.ApplicationVolumeDelayMilliseconds)) { }

    public ApplicationAudioControlCoordinator(IApplicationAudioControl control, TimeSpan volumeDelay)
    { _control = control; _volumeDelay = volumeDelay; }

    public Task<ApplicationAudioControlResult> SetVolumeAsync(string processName, int percent, bool immediately, CancellationToken token) =>
        ApplyAsync(processName, Math.Clamp(percent, 0, 100), null, immediately ? TimeSpan.Zero : _volumeDelay, token);
    public Task<ApplicationAudioControlResult> SetMuteAsync(string processName, bool muted, CancellationToken token) =>
        ApplyAsync(processName, null, muted, TimeSpan.Zero, token);

    public bool IsCurrent(string processName, long version)
    {
        lock (_gate) return !_disposed && _entries.TryGetValue(processName, out var entry) && entry.Version == version;
    }

    public bool IsPending(string processName)
    { lock (_gate) return !_disposed && _entries.TryGetValue(processName, out var entry) && entry.PendingVersion == entry.Version; }

    private async Task<ApplicationAudioControlResult> ApplyAsync(string name, int? percent, bool? muted, TimeSpan delay, CancellationToken ownerToken)
    {
        Entry entry;
        long version;
        CancellationToken stopToken;
        lock (_gate)
        {
            if (_disposed) return Cancelled();
            if (!_entries.TryGetValue(name, out entry!)) _entries.Add(name, entry = new());
            version = ++entry.Version;
            entry.PendingVersion = version;
            stopToken = _stop.Token;
            _pending++;
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ownerToken, stopToken);
        var token = cancellation.Token;
        var entered = false;
        bool Current() => !token.IsCancellationRequested && IsCurrent(name, version);
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
            if (!Current()) return Cancelled(version);
            await entry.Queue.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            if (!Current()) return Cancelled(version);
            var result = await Task.Run(() => _control.Apply(name, percent, muted, Current), token).ConfigureAwait(false);
            return Current() ? result with { Version = version } : Cancelled(version);
        }
        catch (OperationCanceledException) { return Cancelled(version); }
        catch (Exception exception)
        { return Current() ? new(new(AudioDeviceOperationStatus.Failed, exception.Message), Version: version) : Cancelled(version); }
        finally
        {
            if (entered) entry.Queue.Release();
            lock (_gate)
            {
                _pending--;
                if (entry.PendingVersion == version) entry.PendingVersion = 0;
                if (_stopped && _pending == 0) ReleaseResources();
            }
        }
    }

    private static ApplicationAudioControlResult Cancelled(long version = 0) => new(new(AudioDeviceOperationStatus.Cancelled), Version: version);
    private void ReleaseResources()
    {
        foreach (var entry in _entries.Values) entry.Queue.Dispose();
        _entries.Clear();
        _stop.Dispose();
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _stop.Cancel();
        lock (_gate) { _stopped = true; if (_pending == 0) ReleaseResources(); }
    }
    private sealed class Entry
    {
        public long Version;
        public long PendingVersion;
        public SemaphoreSlim Queue { get; } = new(1, 1);
    }
}
