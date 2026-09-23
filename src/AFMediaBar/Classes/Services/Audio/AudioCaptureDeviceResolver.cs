using System.Diagnostics;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Abstractions;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 后台解析频谱采集的目标输出端点，并把结果缓存下来供采集路径读取。
///
/// 端点和音频会话的枚举（Core Audio + 进程查询）是毫秒级的阻塞调用，不能从频谱的 UI 计时器执行：
/// 本服务只在频谱持续请求采样时，用一条后台循环按档位节奏扫描（正常 2 秒、Idle 10 秒、息屏/睡眠不枚举），
/// 选出"正在出声的那个设备"后写入 <see cref="TargetDeviceId"/>；采集路径只读取这个缓存值，不做任何枚举。
/// Resolves the spectrum capture's target render endpoint on a background loop and caches the result for the capture path.
///
/// Enumerating endpoints and audio sessions (Core Audio plus process queries) consists of blocking calls in the millisecond range and
/// must not run from the spectrum's UI timer: while spectrum samples are requested, a background loop scans on a level-aware cadence
/// (two seconds normally, ten when idle, no enumeration while the display is off or the system suspends), picks the audible device and
/// writes it to <see cref="TargetDeviceId"/>; the capture path only reads that cached value and never enumerates.
/// </summary>
public sealed class AudioCaptureDeviceResolver : IDisposable, IMemoryPrunable
{
    private const int DeviceStateActive = 0x1;

    /// <summary>"有会话在出声"的峰值下限。会话表是端点音量之前的数值，因此不受系统音量影响，低音量听歌也能被识别。
    /// Session peak that counts as audible. Session meters are pre-endpoint-volume, so system volume does not scale them and quiet
    /// listening is still recognized.</summary>
    private const float AudibleSessionPeakThreshold = 0.002f;

    private static readonly Guid AudioDeviceEnumeratorClassId =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid AudioSessionManagerId =
        new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _scanRequested = new(0, 1);
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
    public AudioCaptureDeviceResolver()
    {
        var token = _cancellation.Token;
        _ = Task.Run(() => RunLoopAsync(token));
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

        try
        {
            _scanRequested.Release();
        }
        catch (SemaphoreFullException)
        {
            // Multiple taskbar hosts can request the same scan; one pending wake-up is enough.
        }
        catch (ObjectDisposedException)
        {
            // A final UI tick can race with application shutdown.
        }
    }

    /// <summary>记录剪枝档位；循环按档位决定扫描间隔与是否枚举。/ Records the prune level; the loop derives its scan interval and whether to enumerate from it.</summary>
    /// <param name="level">目标档位。/ The target level.</param>
    public void Prune(MemoryPruneLevel level) => _pruneLevel = level;

    /// <summary>停止后台循环；循环在下一个唤醒点自我结束，已解析的目标保持可读（只影响采集重建的时机）。</summary>
    /// <summary>Stops the background loop; it ends at its next wake point, and the last resolved target stays readable (which only affects when a capture is rebuilt).</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        try
        {
            await _scanRequested.WaitAsync(token).ConfigureAwait(false);
            while (!token.IsCancellationRequested)
            {
                if (!HasRecentDemand())
                {
                    await _scanRequested.WaitAsync(token).ConfigureAwait(false);
                }

                ResolveOnce(token);
                await _scanRequested.WaitAsync(AudioCaptureDevicePolicy.ResolveScanInterval(_pruneLevel), token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose cancels either wait immediately.
        }
        finally
        {
            _scanRequested.Dispose();
        }
    }

    private bool HasRecentDemand()
    {
        var last = Interlocked.Read(ref _lastDemandTick);
        return last != long.MinValue && Environment.TickCount64 - last <= DemandIdleMilliseconds;
    }

    /// <summary>
    /// 解析一次并更新缓存；失败时保留上一次结果，下一次唤醒再试。
    /// Resolves once and updates the cache; a failure keeps the previous result and the next wake retries.
    /// </summary>
    private void ResolveOnce(CancellationToken token)
    {
        if (token.IsCancellationRequested ||
            !AudioCaptureDevicePolicy.ShouldScan(_pruneLevel, HasRecentDemand()))
        {
            return;
        }

        try
        {
            var resolved = ResolveTarget();
            if (!string.IsNullOrEmpty(resolved))
            {
                _targetDeviceId = resolved;
            }
        }
        catch (Exception ex)
        {
            // 音频栈在设备切换/驱动重启期间会短暂抛错：这不是故障，保留上一次目标即可。
            // The audio stack briefly throws while devices switch or drivers restart: that is not a failure, keeping the previous target is enough.
            Debug.WriteLine($"[AudioCaptureDeviceResolver] Resolve failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 枚举活动端点与会话，按 <see cref="AudioCaptureDevicePolicy.SelectTarget"/> 选出目标标识。
    /// 每次解析都在当前线程自建并释放 COM 对象，解析之间不共享任何 COM 实例，因此循环运行在线程池线程上也安全。
    /// Enumerates active endpoints and sessions and picks the target identifier through <see cref="AudioCaptureDevicePolicy.SelectTarget"/>.
    /// Every resolution creates and releases its COM objects on the current thread and shares nothing between resolutions, so running
    /// the loop on a thread-pool thread is safe.
    /// </summary>
    private string? ResolveTarget()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(AudioDeviceEnumeratorClassId, throwOnError: true)!)!;
            var defaultId = ResolveDefaultEndpointId(enumerator);
            var candidates = new List<AudioEndpointAudibility>();
            if (enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out collection) < 0 ||
                collection is null ||
                collection.GetCount(out var count) < 0)
            {
                return null;
            }

            for (uint index = 0; index < count; index++)
            {
                if (collection.Item(index, out var endpoint) < 0 || endpoint is null)
                {
                    continue;
                }

                try
                {
                    if (endpoint.GetId(out var id) < 0 || string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    var rank = ResolveAudibilityRank(endpoint, out var peak);
                    if (rank > AudioCaptureDevicePolicy.RankSilent)
                    {
                        candidates.Add(new AudioEndpointAudibility(id, rank, peak));
                    }
                }
                finally
                {
                    ReleaseComObject(ref endpoint);
                }
            }

            return AudioCaptureDevicePolicy.SelectTarget(_targetDeviceId, defaultId, candidates);
        }
        finally
        {
            ReleaseComObject(ref collection);
            ReleaseComObject(ref enumerator);
        }
    }

    /// <summary>
    /// 测出一个端点的可听等级：只看仍在播放（Active）且不是本进程与系统混音进程之外的应用会话；
    /// 应用会话超过阈值算 <see cref="AudioCaptureDevicePolicy.RankApplication"/>，只有系统混音会话超过阈值算
    /// <see cref="AudioCaptureDevicePolicy.RankSystemOnly"/>。
    /// Measures one endpoint's audibility rank: only sessions that are Active and belong neither to this process nor to the system
    /// mixer's own process count as applications; an application peak above the threshold reads
    /// <see cref="AudioCaptureDevicePolicy.RankApplication"/>, and only a system-mixer peak above it reads
    /// <see cref="AudioCaptureDevicePolicy.RankSystemOnly"/>.
    /// </summary>
    /// <param name="endpoint">要测量的端点。/ The endpoint to measure.</param>
    /// <param name="peak">该端点的最大会话峰值（0–1）。/ Largest session peak on the endpoint (0–1).</param>
    private int ResolveAudibilityRank(IMMDevice endpoint, out float peak)
    {
        peak = 0f;
        object? managerObject = null;
        IAudioSessionEnumerator? sessionEnumerator = null;
        try
        {
            var managerId = AudioSessionManagerId;
            if (endpoint.Activate(ref managerId, ClsCtx.All, nint.Zero, out managerObject) < 0 ||
                managerObject is not IAudioSessionManager2 manager ||
                manager.GetSessionEnumerator(out sessionEnumerator) < 0 ||
                sessionEnumerator is null ||
                sessionEnumerator.GetCount(out var sessionCount) < 0)
            {
                return AudioCaptureDevicePolicy.RankSilent;
            }

            var applicationPeak = 0f;
            var systemPeak = 0f;
            for (var index = 0; index < sessionCount; index++)
            {
                if (sessionEnumerator.GetSession(index, out var sessionObject) < 0 || sessionObject is null)
                {
                    continue;
                }

                try
                {
                    if (sessionObject is not IAudioSessionControl2 control ||
                        control.GetState(out var state) < 0 ||
                        state != AudioSessionState.Active ||
                        control.GetProcessId(out var processId) < 0 ||
                        processId is 0 ||
                        processId == (uint)Environment.ProcessId)
                    {
                        continue;
                    }

                    if (sessionObject is not IAudioMeterInformation meter ||
                        meter.GetPeakValue(out var sessionPeak) < 0 ||
                        sessionPeak <= 0)
                    {
                        continue;
                    }

                    if (IsSystemAudioProcess(processId))
                    {
                        systemPeak = Math.Max(systemPeak, sessionPeak);
                    }
                    else
                    {
                        applicationPeak = Math.Max(applicationPeak, sessionPeak);
                    }
                }
                finally
                {
                    ReleaseComObject(ref sessionObject);
                }
            }

            peak = Math.Max(applicationPeak, systemPeak);
            if (applicationPeak >= AudibleSessionPeakThreshold)
            {
                return AudioCaptureDevicePolicy.RankApplication;
            }

            return systemPeak >= AudibleSessionPeakThreshold
                ? AudioCaptureDevicePolicy.RankSystemOnly
                : AudioCaptureDevicePolicy.RankSilent;
        }
        catch
        {
            return AudioCaptureDevicePolicy.RankSilent;
        }
        finally
        {
            ReleaseComObject(ref sessionEnumerator);
            ReleaseComObject(ref managerObject);
        }
    }

    /// <summary>该进程是否是系统混音进程（audiodg）；查不到进程时按系统进程处理，它不会成为"应用在出声"的证据。
    /// Whether the process is the system audio engine (audiodg); an unresolvable process counts as a system one, so it can never
    /// become evidence of "an application is playing".</summary>
    private static bool IsSystemAudioProcess(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "audiodg", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>解析系统默认多媒体端点的标识；不可用时返回 null。/ Resolves the system default multimedia endpoint identifier, or null.</summary>
    private static string? ResolveDefaultEndpointId(IMMDeviceEnumerator enumerator)
    {
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var endpoint) < 0 ||
                endpoint is null)
            {
                return null;
            }

            try
            {
                return endpoint.GetId(out var id) >= 0 && !string.IsNullOrEmpty(id) ? id : null;
            }
            finally
            {
                ReleaseComObject(ref endpoint);
            }
        }
        catch
        {
            return null;
        }
    }

    private static void ReleaseComObject<T>(ref T? value)
        where T : class
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }

        value = null;
    }

    private enum EDataFlow
    {
        Render,
        Capture,
        All
    }

    private enum ERole
    {
        Console,
        Multimedia,
        Communications
    }

    [Flags]
    private enum ClsCtx
    {
        InprocServer = 0x1,
        InprocHandler = 0x2,
        LocalServer = 0x4,
        All = InprocServer | InprocHandler | LocalServer
    }

    /// <summary>音频会话状态（audiosessiontypes.h）。/ Audio session state (audiosessiontypes.h).</summary>
    private enum AudioSessionState
    {
        Inactive,
        Active,
        Expired
    }

    // COM 声明来自 Windows Core Audio SDK；顺序和封送类型必须与 ABI 一致。
    // COM declarations mirror the Core Audio SDK; method order and marshaling are ABI-sensitive.
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(nint client);
        int UnregisterEndpointNotificationCallback(nint client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out uint count);
        int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(
            ref Guid interfaceId,
            ClsCtx classContext,
            nint activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        int OpenPropertyStore(int accessMode, out nint properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    // 会话枚举与峰值：用于判断"哪个端点正在出声"（见 ResolveAudibilityRank），槽位顺序来自 audiopolicy.h。
    // Session enumeration and metering: used to decide which endpoint is audible (see ResolveAudibilityRank); the slots follow audiopolicy.h.
    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int GetAudioSessionControl(
            ref Guid audioSessionGuid,
            uint streamFlags,
            [MarshalAs(UnmanagedType.IUnknown)] out object sessionControl);
        int GetSimpleAudioVolume(
            ref Guid audioSessionGuid,
            uint streamFlags,
            [MarshalAs(UnmanagedType.IUnknown)] out object audioVolume);
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        int RegisterSessionNotification(nint sessionNotification);
        int UnregisterSessionNotification(nint sessionNotification);
        int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, nint duckNotification);
        int UnregisterDuckNotification(nint duckNotification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int sessionCount);
        int GetSession(int sessionIndex, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        int GetState(out AudioSessionState state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingParam);
        int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        int RegisterAudioSessionNotification(nint client);
        int UnregisterAudioSessionNotification(nint client);
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetProcessId(out uint processId);
        int IsSystemSoundsSession();
        int SetDuckingPreference(bool optOut);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        int GetPeakValue(out float peak);
        int GetMeteringChannelCount(out uint channelCount);
        int GetChannelsPeakValues(uint channelCount, nint peakValues);
        int QueryHardwareSupport(out uint hardwareSupportMask);
    }
}
