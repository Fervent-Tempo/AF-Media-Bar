// 后台解析当前媒体来源的输出端点；每次扫描自行释放 NAudio 设备与会话。
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 按需在后台解析 AF 当前媒体来源的实际输出端点；暂停保留仍连接的目标，无法匹配时回退系统默认设备。
/// 端点、会话和进程读取只在后台循环中执行，停止频谱取样后沿用原有空闲与剪枝节奏。
/// </summary>
public sealed class AudioCaptureDeviceResolver : IDisposable, IMemoryPrunable
{

    /// <summary>"有会话在出声"的峰值下限。会话表是端点音量之前的数值，因此不受系统音量影响，低音量听歌也能被识别。
    /// Session peak that counts as audible. Session meters are pre-endpoint-volume, so system volume does not scale them and quiet
    /// listening is still recognized.</summary>
    private const float AudibleSessionPeakThreshold = 0.002f;

    private readonly MediaSourceProcessResolver _sourceResolver;
    private readonly AudioPlatformService? _platform;
    private readonly Func<SpectrumCaptureProcessResolver.MediaIdentity, string?, (bool Success, string? Target)> _scan;
    private int _refreshVersion;
    private long _refreshTick;
    private int _refreshPass;
    private string _refreshReason = "demand-resumed";
    private readonly Task _worker;

    internal readonly record struct Resolution(string? DeviceId, bool TargetChanged, bool EnvironmentRefresh, int Generation, string Reason);
    internal event Action<Resolution>? Resolved;
    internal Task Completion => _worker;
    private readonly object _sourceGate = new();
    private SpectrumCaptureProcessResolver.MediaIdentity _mediaSource =
        SpectrumCaptureProcessResolver.MediaIdentity.FromSnapshot(MediaSnapshot.Disconnected);
    private int _sourceVersion;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _scanRequested = new(0, 1);
    private int _scanWakePending;
    private long _lastDemandTick = long.MinValue;
    private volatile string? _targetDeviceId;
    private volatile MemoryPruneLevel _pruneLevel;
    private volatile bool _isDisposed;

    // 频谱最慢每 200 ms 取样一次；停止请求满一秒即让后台循环停在信号量上，不再周期唤醒。
    // Spectrum samples arrive at least every 200 ms; after one second without demand the loop parks without periodic wakeups.
    private const long DemandIdleMilliseconds = 1_000;

    /// <summary>最近一次解析出的目标端点标识；尚未解析或解析不出时为空，采集路径遇到空值会回退到系统默认端点。
    /// The endpoint identifier resolved most recently; null before the first resolution or when nothing could be resolved, in which
    /// case the capture path falls back to the system default endpoint.</summary>
    public string? TargetDeviceId => _targetDeviceId;

    /// <summary>参与者名称，只用于诊断。/ Participant name, used for diagnostics only.</summary>
    public string PruneParticipantName => "audio-capture-device";

    /// <summary>
    /// 启动按需解析循环；频谱第一次取样时立即解析，避免后台无频谱时仍枚举音频会话。
    /// Starts the demand-driven resolution loop; the first spectrum sample triggers an immediate scan without background enumeration
    /// while the spectrum is unused.
    /// </summary>
    public AudioCaptureDeviceResolver(MediaSourceProcessResolver sourceResolver, AudioPlatformService? platform = null)
        : this(sourceResolver, platform, null) { }

    // 只替换原生扫描边界，测试仍运行真实的等待、合并、代际和发布流程。
    internal AudioCaptureDeviceResolver(Func<SpectrumCaptureProcessResolver.MediaIdentity, string?, (bool Success, string? Target)> scan)
        : this(new MediaSourceProcessResolver(), null, scan) { }

    private AudioCaptureDeviceResolver(MediaSourceProcessResolver sourceResolver, AudioPlatformService? platform,
        Func<SpectrumCaptureProcessResolver.MediaIdentity, string?, (bool Success, string? Target)>? scan)
    {
        _sourceResolver = sourceResolver;
        _platform = platform;
        _scan = scan ?? ((source, current) =>
        {
            var success = TryResolveTarget(source, current, out var target);
            return (success, target);
        });
        if (_platform is not null) _platform.Changed += OnEnvironmentChanged;
        var token = _cancellation.Token;
        _worker = Task.Run(() => RunLoopAsync(token));
    }

    private void OnEnvironmentChanged(object? sender, EventArgs args) => Refresh("device-notification");

    /// <summary>使在途结果失效；有频谱需求时立即重扫，并在事件后约 250、750 毫秒补扫。</summary>
    public void Refresh(string reason = "environment-changed")
    {
        lock (_sourceGate)
        {
            if (_isDisposed) return;
            _refreshVersion++;
            _refreshTick = Environment.TickCount64;
            _refreshPass = 0;
            _refreshReason = reason;
        }
        if (AudioCaptureDevicePolicy.ShouldScan(_pruneLevel, HasRecentDemand())) WakeScan();
    }

    /// <summary>报告频谱正在取样；从休眠状态恢复时唤醒后台解析器，不在调用线程枚举设备。
    /// Reports active spectrum sampling and wakes the background resolver when parked, without enumerating on the caller's thread.</summary>
    public void RequestScan()
    {
        if (_isDisposed)
        {
            return;
        }

        var now = Environment.TickCount64;
        var previous = Interlocked.Exchange(ref _lastDemandTick, now);
        if (previous != long.MinValue && now - previous <= DemandIdleMilliseconds)
        {
            return;
        }

        Refresh("demand-resumed");
    }

    private void WakeScan()
    {
        // Several taskbar hosts can resume together; coalesce their wake-ups before
        // Release so the normal full-semaphore case never throws a first-chance exception.
        if (Interlocked.Exchange(ref _scanWakePending, 1) != 0)
            return;
        try { _scanRequested.Release(); }
        catch (ObjectDisposedException)
        {
            // A final UI tick can race with application shutdown.
        }
    }

    internal void UpdateMediaSource(SpectrumCaptureProcessResolver.MediaIdentity source)
    {
        lock (_sourceGate)
        {
            if (_isDisposed || _mediaSource == source) return;
            _mediaSource = source;
            _sourceVersion++;
        }
        Refresh("media-source-changed");
    }

    /// <summary>记录剪枝档位；循环按档位决定扫描间隔与是否枚举。/ Records the prune level; the loop derives its scan interval and whether to enumerate from it.</summary>
    /// <param name="level">目标档位。/ The target level.</param>
    public void Prune(MemoryPruneLevel level)
    {
        var previous = _pruneLevel;
        _pruneLevel = level;
        if (previous >= MemoryPruneLevel.DisplayOff && level < MemoryPruneLevel.DisplayOff)
            Refresh("sampling-resumed");
    }

    /// <summary>停止后台循环；循环在下一个唤醒点自我结束，已解析的目标保持可读（只影响采集重建的时机）。</summary>
    /// <summary>Stops the background loop; it ends at its next wake point, and the last resolved target stays readable (which only affects when a capture is rebuilt).</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        lock (_sourceGate)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _refreshVersion++;
        }
        if (_platform is not null) _platform.Changed -= OnEnvironmentChanged;
        _cancellation.Cancel();
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!AudioCaptureDevicePolicy.ShouldScan(_pruneLevel, HasRecentDemand()))
                {
                    await _scanRequested.WaitAsync(token).ConfigureAwait(false);
                    Interlocked.Exchange(ref _scanWakePending, 0);
                    continue;
                }

                if (_scanRequested.Wait(0)) Interlocked.Exchange(ref _scanWakePending, 0);
                ResolveOnce(token);
                TimeSpan delay;
                lock (_sourceGate)
                {
                    var due = _refreshPass switch { 0 => 0, 1 => 250, 2 => 750, _ => -1 };
                    delay = due < 0 ? AudioCaptureDevicePolicy.ResolveScanInterval(_pruneLevel) :
                        TimeSpan.FromMilliseconds(Math.Max(0, _refreshTick + due - Environment.TickCount64));
                }
                if (await _scanRequested.WaitAsync(delay, token).ConfigureAwait(false))
                    Interlocked.Exchange(ref _scanWakePending, 0);
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose cancels either wait immediately.
        }
        finally
        {
            _scanRequested.Dispose();
            _cancellation.Dispose();
        }
    }

    private bool HasRecentDemand()
    {
        var last = Interlocked.Read(ref _lastDemandTick);
        return last != long.MinValue && Environment.TickCount64 - last <= DemandIdleMilliseconds;
    }

    /// <summary>
    /// 成功枚举后更新缓存（可清空失效 ID）；枚举失败时保留上一次结果，下一次唤醒再试。
    /// Updates the cache after a successful enumeration (including clearing a stale ID); an enumeration failure keeps the previous result.
    /// </summary>
    private void ResolveOnce(CancellationToken token)
    {
        if (token.IsCancellationRequested ||
            !AudioCaptureDevicePolicy.ShouldScan(_pruneLevel, HasRecentDemand()))
        {
            return;
        }

        SpectrumCaptureProcessResolver.MediaIdentity source;
        string? current;
        int version;
        int generation;
        int pass;
        string reason;
        lock (_sourceGate)
        {
            source = _mediaSource;
            current = _targetDeviceId;
            version = _sourceVersion;
            generation = _refreshVersion;
            pass = _refreshPass++;
            reason = _refreshReason;
        }
        try
        {
            var result = _scan(source, current);
            Resolution update;
            lock (_sourceGate)
            {
                if (!result.Success || token.IsCancellationRequested || _isDisposed ||
                    generation != _refreshVersion || version != _sourceVersion ||
                    !AudioCaptureDevicePolicy.ShouldScan(_pruneLevel, HasRecentDemand())) return;
                var changed = !string.Equals(current, result.Target, StringComparison.OrdinalIgnoreCase);
                _targetDeviceId = result.Target;
                update = new Resolution(result.Target, changed,
                    pass == 0 && reason is not ("demand-resumed" or "media-source-changed"), generation, reason);
            }
            var message = $"频谱端点解析: generation={generation} reason={reason} pass={pass} target={result.Target ?? "none"} changed={update.TargetChanged}";
            if (pass == 0 || update.TargetChanged) AppLogService.Current?.Info("Audio", message);
            else AppLogService.Current?.Verbose("Audio", message);
            if (!_isDisposed && generation == Volatile.Read(ref _refreshVersion)) Resolved?.Invoke(update);
        }
        catch (Exception ex)
        {
            // 设备切换期间扫描失败时保留旧目标，后续补扫或轮询继续确认。
            AppLogService.Current?.Verbose("Audio", $"频谱端点解析失败: generation={generation} reason={reason} {ex.Message}");
        }
    }

    /// <summary>
    /// 枚举活动端点与会话，按 <see cref="AudioCaptureDevicePolicy.SelectTarget"/> 选出目标标识；枚举失败与成功但无端点分开报告。
    /// 每次解析都在当前线程自建并释放 COM 对象，解析之间不共享任何 COM 实例，因此循环运行在线程池线程上也安全。
    /// Enumerates active endpoints and sessions and picks the target identifier through <see cref="AudioCaptureDevicePolicy.SelectTarget"/>;
    /// enumeration failure is distinct from a successful scan with no endpoints.
    /// Every resolution creates and releases its COM objects on the current thread and shares nothing between resolutions, so running
    /// the loop on a thread-pool thread is safe.
    /// </summary>
    private bool TryResolveTarget(SpectrumCaptureProcessResolver.MediaIdentity source, string? current, out string? targetDeviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = ResolveDefaultEndpointId(enumerator);
        var activeDeviceIds = new List<string>();
        var sessions = new List<(string DeviceId, string ProcessName, string DisplayName, float Peak)>();
        using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        for (var index = 0; index < devices.Count; index++)
        {
            try
            {
                using var endpoint = devices[index];
                var id = endpoint.ID;
                activeDeviceIds.Add(id);
                if (source.IsConnected) ReadMediaSessions(endpoint, sessions);
            }
            catch (COMException)
            {
                // A device may disappear while scanning; continue with surviving endpoints.
            }
        }
        var matches = _sourceResolver.ResolveMatches(source.SourceId, source.SourceName,
            sessions.Select(session => (session.ProcessName, session.DisplayName)));
        var candidates = sessions.Where(session => matches.Contains(session.ProcessName))
            .Select(session => new AudioEndpointAudibility(session.DeviceId,
                session.Peak >= AudibleSessionPeakThreshold ? AudioCaptureDevicePolicy.RankApplication :
                    AudioCaptureDevicePolicy.RankMatchedSilent, session.Peak)).ToArray();
        targetDeviceId = AudioCaptureDevicePolicy.SelectTarget(current, defaultId, activeDeviceIds, candidates);
        return true;
    }

    private static void ReadMediaSessions(MMDevice endpoint,
        List<(string DeviceId, string ProcessName, string DisplayName, float Peak)> result)
    {
        try
        {
            var sessions = endpoint.AudioSessionManager.Sessions;
            for (var index = 0; index < sessions.Count; index++)
            {
                try
                {
                    using var session = sessions[index];
                    var processId = session.GetProcessID;
                    if (session.State == AudioSessionState.AudioSessionStateExpired || processId == 0 || processId == Environment.ProcessId) continue;
                    var name = ReadProcessName(processId);
                    if (name is null) continue;
                    var peak = session.State == AudioSessionState.AudioSessionStateActive
                        ? session.AudioMeterInformation?.MasterPeakValue ?? 0 : 0;
                    result.Add((endpoint.ID, name, session.DisplayName, peak));
                }
                catch (COMException) { }
            }
        }
        catch (COMException) { }
    }

    private static string? ReadProcessName(uint processId)
    {
        try { using var process = Process.GetProcessById((int)processId); return process.ProcessName; }
        catch { return null; }
    }

    /// <summary>解析系统默认多媒体端点的标识；不可用时返回 null。/ Resolves the system default multimedia endpoint identifier, or null.</summary>
    private static string? ResolveDefaultEndpointId(MMDeviceEnumerator enumerator)
    {
        try
        {
            using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return endpoint.ID;
        }
        catch (COMException)
        {
            return null;
        }
    }
}
