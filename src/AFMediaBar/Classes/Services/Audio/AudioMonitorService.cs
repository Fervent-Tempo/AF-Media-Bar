// Owns demand-driven NAudio loopback capture on a hosted worker; callbacks only update samples under the gate.
// Host shutdown awaits the worker, which disposes capture before its endpoint. UI callers never touch native audio resources.
using System.Diagnostics;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Models;
using Microsoft.Extensions.Hosting;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 后台回环采集与频谱分析；由宿主等待采集停止，界面仅读取缓存样本。
/// Background loopback capture and spectrum analysis; the host awaits capture shutdown and UI paths read cached samples only.
/// </summary>
public sealed class AudioMonitorService : BackgroundService, IMemoryPrunable
{
    private const int MinimumRingSize = 4_096;
    private readonly RepeatedFailureReporter _captureFailures = new();
    private string? _captureLogSignature;
    private string _captureRestartReason = "initial";
    private readonly AudioCaptureDeviceResolver _deviceResolver;
    private readonly SpectrumCaptureProcessResolver _processResolver;
    private volatile SpectrumCaptureSource _captureSource;
    private volatile string? _captureDeviceId;
    // 生效的设备策略（旧配置的 null 已在写入前解析成等效模式）；采集分流与重建判定都只认这一个值。
    private volatile SpectrumCaptureDeviceMode _captureDeviceMode;
    private SpectrumCaptureProcessResolver.MediaIdentity _mediaSource =
        SpectrumCaptureProcessResolver.MediaIdentity.FromSnapshot(MediaSnapshot.Disconnected);
    private readonly object _sourceGate = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _demand = new(0, 1);
    private int _wakePending;
    private long _lastDemandTick = long.MinValue;
    private int _captureVersion;
    private int _activeCaptureVersion;
    private volatile bool _disposed;
    private bool _disposeCalled;
    private volatile bool _captureReady;
    private MemoryPruneLevel _pruneLevel;
    private int _sampleWriteIndex;
    private int _sampleCount;
    private int _sampleRate;
    private long _lastPacketTick;
    private double _spectrumReference;
    private long _lastSpectrumTick;
    // 首次有采集需求并取得混音格式后再分配，避免频谱未使用时占用缓冲、启动后又按真实采样率重建。
    private float[] _sampleRing = [];
    private double[] _fftReal = [];
    private double[] _fftImaginary = [];
    private double[] _fftWindow = [];
    private double[] _powerSpectrum = [];
    private int _fftSize = SpectrumAnalysisPolicy.MinimumFftSize;
    private int _ringSize = MinimumRingSize;
    // 频段边界只随柱数变化，因此按柱数缓存；采样线程每帧都要读它，不能每帧重新计算等比数列。
    // Band edges change only with the bar count, so they are cached per count: the sampling thread reads them every frame
    // and must not rebuild the geometric progression each time.
    private float[] _bandEdges = [];
    private int _bandEdgeCount;
    // 频段幅度先整体算完再映射：相对 dB 窗口需要一个"本次采样的最大幅度"作为参考更新的输入。
    // Band magnitudes are computed in full before mapping: the relative dB window needs this sample's largest magnitude to advance the reference.
    private double[] _bandMagnitudes = [];

    /// <summary>创建由宿主控制生命周期的频谱服务。 / Creates the host-managed spectrum service.</summary>
    public AudioMonitorService(AudioCaptureDeviceResolver deviceResolver,
        SpectrumCaptureProcessResolver processResolver)
    {
        _deviceResolver = deviceResolver;
        _processResolver = processResolver;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        lock (_sourceGate)
        {
            var settings = SettingsManager.Current.SpectrumComponent.Normalize();
            _captureSource = settings.CaptureSource;
            _captureDeviceMode = settings.ResolveCaptureDeviceMode();
            _captureDeviceId = settings.CaptureDeviceId;
        }
    }

    /// <summary>组合根转发媒体栏实际呈现的来源；这里只保存身份，不持有媒体服务或曲目内容。</summary>
    public void UpdateMediaSource(MediaSnapshot snapshot)
    {
        lock (_sourceGate)
        {
            if (_disposed) return;
            var changed = _mediaSource.IsConnected != snapshot.IsConnected ||
                !string.Equals(_mediaSource.SourceId, snapshot.SourceId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_mediaSource.SourceName, snapshot.SourceName, StringComparison.OrdinalIgnoreCase);
            _mediaSource = SpectrumCaptureProcessResolver.MediaIdentity.FromSnapshot(snapshot);
            _deviceResolver.UpdateMediaSource(_mediaSource);
            if (changed && (_captureSource == SpectrumCaptureSource.CurrentMediaApplication ||
                _captureSource == SpectrumCaptureSource.CurrentDevice && _captureDeviceMode == SpectrumCaptureDeviceMode.FollowMediaSource))
            {
                InvalidateSourceCapture();
                WakeWorker();
            }
        }
    }

    private SpectrumCaptureSource? _presentationCaptureSource;

    public void ApplyPresentationCaptureSource(SpectrumCaptureSource? source)
    {
        if (_presentationCaptureSource == source) return;
        _presentationCaptureSource = source;
        OnSettingsChanged(this, null!);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        var settings = SettingsManager.Current.SpectrumComponent.Normalize();
        var source = _presentationCaptureSource ?? settings.CaptureSource;
        var deviceMode = settings.ResolveCaptureDeviceMode();
        lock (_sourceGate)
        {
            if (_disposed) return;
            // 设备策略必须参与判定：从「跟随媒体来源」切到「系统默认输出设备」时设备标识往往不变，
            // 只比 CaptureDeviceId 会漏掉这次切换，频谱继续跟着旧端点。
            var changed = source != _captureSource || source == SpectrumCaptureSource.CurrentDevice &&
                (!string.Equals(settings.CaptureDeviceId, _captureDeviceId, StringComparison.OrdinalIgnoreCase) ||
                 deviceMode != _captureDeviceMode);
            _captureSource = source;
            _captureDeviceMode = deviceMode;
            _captureDeviceId = settings.CaptureDeviceId;
            if (!changed) return;
            InvalidateSourceCapture();
            WakeWorker();
        }
    }

    private void Unsubscribe()
    {
        SettingsManager.SettingsChanged -= OnSettingsChanged;
    }

    private void InvalidateSourceCapture()
    {
        // 来源通知可能在 UI 线程到达；只作废代际，不等待采样回调或 FFT 持有的锁。
        Interlocked.Increment(ref _captureVersion);
        _captureReady = false;
    }

    /// <summary>
    /// 在后台计算频谱并把结果交给 UI；UI 线程不等待采样回调持有的锁或 FFT。
    /// Computes the spectrum in the background so the UI never waits on the capture callback's gate or FFT.
    /// </summary>
    public Task<(bool IsAvailable, int CaptureVersion)> GetSpectrumAsync(float[] bands, int bandCount) =>
        Task.Run(() =>
        {
            var version = Volatile.Read(ref _captureVersion);
            return (GetSpectrum(bands, bandCount) && IsCaptureCurrent(version), version);
        });

    /// <summary>界面应用异步结果前检查采集代际；切换来源、范围或退出后拒绝旧帧。</summary>
    public bool IsCaptureCurrent(int version) => !_disposed && _captureReady && version == Volatile.Read(ref _captureVersion);

    /// <summary>从缓存样本填充频谱；不会等待音频设备或后台采集。 / Fills bands from cached samples without waiting on audio devices.</summary>
    public bool GetSpectrum(float[] bands, int bandCount)
    {
        var count = SpectrumBandPolicy.ClampBandCount(bandCount);
        if (bands.Length < count)
            throw new ArgumentException($"At least {count} bands are required.", nameof(bands));
        lock (_gate)
        {
            Array.Clear(bands, 0, count);
            if (_disposed || _pruneLevel >= MemoryPruneLevel.DisplayOff)
                return false;
            _lastDemandTick = Environment.TickCount64;
            if (_captureSource == SpectrumCaptureSource.CurrentDevice && _captureDeviceMode == SpectrumCaptureDeviceMode.FollowMediaSource) _deviceResolver.RequestScan();
            WakeWorker();
            if (!_captureReady || _activeCaptureVersion != Volatile.Read(ref _captureVersion))
                return false;
            if (_sampleCount < _fftSize || Environment.TickCount64 - _lastPacketTick > 180)
            {
                var now = Environment.TickCount64;
                var elapsed = _lastSpectrumTick == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(now - _lastSpectrumTick);
                _lastSpectrumTick = now;
                _spectrumReference = SpectrumLevelPolicy.UpdateReference(_spectrumReference, 0, elapsed);
                return true;
            }
            CalculateSpectrum(bands, count);
            return true;
        }
    }

    /// <summary>使旧采集失效，由后台释放并在需要时重建。 / Invalidates capture for background disposal and demand-driven recreation.</summary>
    public void ResetAfterEnvironmentChange()
    {
        lock (_gate)
        {
            if (_disposed) return;
            InvalidateCapture();
            WakeWorker();
        }
    }

    /// <summary>参与者名称。 / Diagnostic participant name.</summary>
    public string PruneParticipantName => "audio-capture";

    /// <summary>息屏时停止采集；恢复后等界面再次请求。 / Stops capture with the display off and waits for fresh demand after resuming.</summary>
    public void Prune(MemoryPruneLevel level)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pruneLevel = level;
            if (level >= MemoryPruneLevel.DisplayOff)
            {
                _lastDemandTick = long.MinValue;
                InvalidateCapture();
            }
            WakeWorker();
        }
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        lock (_gate)
        {
            _disposed = true;
            InvalidateCapture();
        }
        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Unsubscribe();
        lock (_gate)
        {
            if (_disposeCalled) return;
            _disposeCalled = true;
            _disposed = true;
            InvalidateCapture();
        }
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                int version;
                lock (_gate) version = _captureVersion;
                if (!HasDemand(version))
                {
                    await _demand.WaitAsync(stoppingToken).ConfigureAwait(false);
                    Interlocked.Exchange(ref _wakePending, 0);
                    continue;
                }
                try
                {
                    await CaptureAsync(version, stoppingToken).ConfigureAwait(false);
                    failures = 0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _captureRestartReason = "failure";
                    _captureFailures.Report(AppLogService.Current, "Audio", "capture", "频谱采集失败", exception);
                    failures = Math.Min(failures + 1, 4);
                    var delay = failures switch { 1 => 100, 2 => 500, 3 => 1000, _ => 3000 };
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            lock (_gate)
            {
                _captureReady = false;
                ClearSamples();
            }
            // Dispose/StopAsync invalidate all publishers before this worker releases its signal.
            _demand.Dispose();
        }
    }

    private async Task CaptureAsync(int version, CancellationToken token)
    {
        SpectrumCaptureSource source;
        SpectrumCaptureDeviceMode deviceMode;
        string? configuredDeviceId;
        SpectrumCaptureProcessResolver.MediaIdentity media;
        lock (_sourceGate)
        {
            source = _captureSource;
            deviceMode = _captureDeviceMode;
            configuredDeviceId = _captureDeviceId;
            media = _mediaSource;
        }
        if (!HasDemand(version)) return;
        uint? processId = null;
        if (source != SpectrumCaptureSource.CurrentDevice)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
                return;
            }
            processId = source == SpectrumCaptureSource.AllApplications
                ? (uint)Environment.ProcessId
                : _processResolver.ResolveProcessId(media, null, token);
            if (processId is null)
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
                return;
            }
        }
        using var enumerator = source == SpectrumCaptureSource.CurrentDevice ? new MMDeviceEnumerator() : null;
        // 设备策略按四种情况显式分流；旧配置（CaptureDeviceMode 为 null）已在写入前按 CaptureDeviceId
        // 解析成等效的 FollowMediaSource / SpecificDevice，这里不再对模式做任何折叠。
        // 非指定设备模式下 CaptureDeviceId 只是保留下来的旧选择，不参与设备选择。
        using var device = source != SpectrumCaptureSource.CurrentDevice ? null : deviceMode switch
        {
            SpectrumCaptureDeviceMode.SpecificDevice => OpenConfiguredDevice(enumerator!, configuredDeviceId ?? string.Empty),
            SpectrumCaptureDeviceMode.SystemDefault => OpenSystemDefaultDevice(enumerator!),
            _ => OpenFollowedDevice(enumerator!, _deviceResolver.TargetDeviceId)
        };
        if (source == SpectrumCaptureSource.CurrentDevice && device is null)
        {
            // 手动端点离线时保留选择，等待重新连接；不扩大到其它设备。
            await Task.Delay(1000, token).ConfigureAwait(false);
            return;
        }
        using var capture = processId is { } pid
            ? await CreateProcessCaptureAsync(pid, source == SpectrumCaptureSource.AllApplications
                ? ProcessLoopbackMode.ExcludeTargetProcessTree : ProcessLoopbackMode.IncludeTargetProcessTree, token).ConfigureAwait(false)
            : new WasapiRecorderBuilder().WithDevice(device!).WithLoopbackCapture().Build();
        var format = capture.WaveFormat;
        var reader = new LoopbackSampleReader(format, PushSample);
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceId = device?.ID;
        lock (_gate)
        {
            if (!HasDemandCore(version)) return;
            _sampleRate = format.SampleRate;
            ConfigureFft(_sampleRate);
            ClearSamples();
            _activeCaptureVersion = version;
            _captureReady = true;
        }
        CaptureDataAvailableHandler onData = (buffer, _, _, _) =>
        {
            lock (_gate)
            {
                if (!HasDemandCore(version) || !_captureReady) return;
                reader.Append(buffer);
                _lastPacketTick = Environment.TickCount64;
            }
        };
        EventHandler<StoppedEventArgs> onStopped = (_, args) => stopped.TrySetResult(args.Exception);
        capture.DataAvailable += onData;
        capture.RecordingStopped += onStopped;
        var stopReason = "demand-or-environment-changed";
        try
        {
            capture.StartRecording();
            var signature = $"{source}|{processId}|{deviceId}|{format}";
            var message = $"频谱采集已启动: source={source} pid={processId?.ToString() ?? "none"} device={deviceId ?? "none"} format={format} reason={_captureRestartReason}";
            var recovered = _captureFailures.Recover(AppLogService.Current, "Audio", "capture", $"频谱采集已恢复: {message}");
            if (!recovered && signature != _captureLogSignature) AppLogService.Current?.Info("Audio", message);
            else AppLogService.Current?.Verbose("Audio", message);
            _captureLogSignature = signature;
            var lastProcessCheck = Environment.TickCount64;
            var lastDefaultCheck = Environment.TickCount64;
            while (HasDemand(version) && !token.IsCancellationRequested)
            {
                if (source == SpectrumCaptureSource.CurrentDevice && deviceMode == SpectrumCaptureDeviceMode.FollowMediaSource)
                {
                    var nextTarget = _deviceResolver.TargetDeviceId;
                    if (!string.IsNullOrEmpty(nextTarget) && !string.Equals(nextTarget, deviceId, StringComparison.OrdinalIgnoreCase)) { stopReason = "device-changed"; break; }
                }
                else if (source == SpectrumCaptureSource.CurrentDevice && deviceMode == SpectrumCaptureDeviceMode.SystemDefault &&
                    Environment.TickCount64 - lastDefaultCheck >= 1000)
                {
                    lastDefaultCheck = Environment.TickCount64;
                    // 采集期间系统默认端点可能被改指（在系统设置里切换默认设备，或原默认设备拔出后系统改指），
                    // 与 AF 内部发起切换时 ResetAfterEnvironmentChange 的重建合流；读不到默认端点就保持当前采集。
                    if (TryReadDefaultEndpointId(enumerator!, out var currentDefault) &&
                        !string.Equals(currentDefault, deviceId, StringComparison.OrdinalIgnoreCase))
                    { stopReason = "default-device-changed"; break; }
                }
                else if (source == SpectrumCaptureSource.CurrentMediaApplication &&
                    Environment.TickCount64 - lastProcessCheck >= 2000)
                {
                    lastProcessCheck = Environment.TickCount64;
                    // 捕获的进程退出或重启时释放旧树；新来源/范围会由代际检查立即使本次采集失效。
                    if (_processResolver.ResolveProcessId(media, processId, token) != processId) { stopReason = "process-changed"; break; }
                }
                if (stopped.Task.IsCompleted)
                    throw await stopped.Task.ConfigureAwait(false) ?? new InvalidOperationException("Loopback capture stopped unexpectedly.");
                await Task.Delay(50, token).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate)
            {
                _captureReady = false;
                ClearSamples();
            }
            capture.DataAvailable -= onData;
            capture.RecordingStopped -= onStopped;
            _captureRestartReason = token.IsCancellationRequested ? "cancelled" : stopReason;
            AppLogService.Current?.Verbose("Audio", $"频谱采集已停止: reason={_captureRestartReason} device={deviceId ?? "none"}");
            // Disposal joins NAudio's capture thread, outside the sample gate and off the UI thread.
        }
    }

    private static MMDevice? OpenConfiguredDevice(MMDeviceEnumerator enumerator, string id)
    {
        MMDevice? device = null;
        try
        {
            device = enumerator.GetDevice(id);
            if (device.State == DeviceState.Active && device.DataFlow == DataFlow.Render) return device;
        }
        catch (COMException) { }
        device?.Dispose();
        return null;
    }

    /// <summary>
    /// 系统默认输出设备。取 Console 角色，与音频设备管理页读写默认设备用的是同一个角色，
    /// 用户在系统里看到的「默认设备」就是这一台；跟随媒体来源的回退仍保持历史上的 Multimedia 角色不变。
    /// The system default output device. The Console role is used because it is the same role the audio device
    /// management page reads and writes, i.e. the device users see as "default" in Windows; the media-source
    /// fallback keeps its historical Multimedia role unchanged.
    /// </summary>
    private static MMDevice OpenSystemDefaultDevice(MMDeviceEnumerator enumerator) =>
        enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

    /// <summary>
    /// 跟随媒体来源：解析器给出目标端点时打开它（与升级前一致地不校验活动状态），
    /// 没有目标时回退到系统默认设备（Multimedia 角色，与升级前一致）。
    /// Follows the media source: opens the resolver's target when there is one, exactly as before the upgrade
    /// without an active-state check, and falls back to the system default device (Multimedia role, unchanged).
    /// </summary>
    private static MMDevice OpenFollowedDevice(MMDeviceEnumerator enumerator, string? resolverTargetId) =>
        string.IsNullOrEmpty(resolverTargetId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(resolverTargetId);

    /// <summary>读取当前系统默认输出端点标识；读取失败（例如当前没有默认设备）时返回 false。</summary>
    private static bool TryReadDefaultEndpointId(MMDeviceEnumerator enumerator, out string? id)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            id = device.ID;
            return true;
        }
        catch (COMException)
        {
            id = null;
            return false;
        }
    }

    private static async Task<WasapiRecorder> CreateProcessCaptureAsync(uint processId, ProcessLoopbackMode mode, CancellationToken token)
    {
        var activation = new WasapiRecorderBuilder().WithProcessLoopback(processId, mode).BuildAsync();
        try
        {
            return await activation.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
        }
        catch
        {
            // WASAPI 激活没有取消入口；观察迟到结果并释放，不能在超时或退出后遗留采集客户端。
            _ = DisposeLateCaptureAsync(activation);
            throw;
        }
    }

    private static async Task DisposeLateCaptureAsync(Task<WasapiRecorder> activation)
    {
        try
        {
            using var capture = await activation.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[AudioMonitorService] Late activation: {exception.Message}");
        }
    }

    private bool HasDemand(int version)
    {
        lock (_gate) return HasDemandCore(version);
    }

    private bool HasDemandCore(int version) => !_disposed && version == Volatile.Read(ref _captureVersion) &&
        _pruneLevel < MemoryPruneLevel.DisplayOff && _lastDemandTick != long.MinValue &&
        Environment.TickCount64 - _lastDemandTick <= 1000;

    private void WakeWorker()
    {
        // Capture requests can arrive every frame. One queued wake-up is sufficient;
        // coalescing before Release avoids a caught first-chance exception on every frame.
        if (Interlocked.Exchange(ref _wakePending, 1) != 0)
            return;
        try { _demand.Release(); }
        catch (ObjectDisposedException) { }
    }

    private void InvalidateCapture()
    {
        Interlocked.Increment(ref _captureVersion);
        _captureReady = false;
        ClearSamples();
    }

    private void ClearSamples()
    {
        _sampleWriteIndex = 0;
        _sampleCount = 0;
        _lastPacketTick = 0;
        _spectrumReference = 0;
        _lastSpectrumTick = 0;
    }

    /// <summary>
    /// 按采样率重建 FFT 缓冲区（点数为 2 的幂、bin 宽目标见 <see cref="SpectrumAnalysisPolicy.ResolveFftSize"/>）。
    /// 缓冲已创建且点数不变时不做任何事；重建会同时清空采样环，因为旧样本的点数语义已经不同。
    /// Rebuilds the FFT buffers for a sample rate (the size is a power of two; see <see cref="SpectrumAnalysisPolicy.ResolveFftSize"/>
    /// for the bin-width target). Existing buffers of the same size are reused; a rebuild also clears the sample ring, because old samples belong
    /// to a different size.
    /// </summary>
    private void ConfigureFft(int sampleRate)
    {
        var fftSize = SpectrumAnalysisPolicy.ResolveFftSize(sampleRate);
        if (fftSize == _fftSize && _sampleRing.Length != 0)
        {
            return;
        }

        _fftSize = fftSize;
        _ringSize = Math.Max(MinimumRingSize, fftSize * 2);
        _sampleRing = new float[_ringSize];
        _fftReal = new double[_fftSize];
        _fftImaginary = new double[_fftSize];
        _fftWindow = CreateFftWindow(_fftSize);
        _powerSpectrum = new double[_fftSize / 2 + 1];
        if (_bandMagnitudes.Length == 0)
            _bandMagnitudes = new double[SpectrumComponentSettings.MaximumBandCount];
        _sampleWriteIndex = 0;
        _sampleCount = 0;
    }

    private void PushSample(float sample)
    {
        _sampleRing[_sampleWriteIndex] = sample;
        _sampleWriteIndex = (_sampleWriteIndex + 1) % _ringSize;
        _sampleCount = Math.Min(_sampleCount + 1, _ringSize);
    }

    private void CalculateSpectrum(float[] bands, int bandCount)
    {
        var start = (_sampleWriteIndex - _fftSize + _ringSize) % _ringSize;
        for (var index = 0; index < _fftSize; index++)
        {
            _fftReal[index] = _sampleRing[(start + index) % _ringSize] * _fftWindow[index];
            _fftImaginary[index] = 0;
        }

        TransformFft();
        var binWidth = _sampleRate / (double)_fftSize;
        var nyquistBin = _fftSize / 2 - 1;

        // 先展成功率谱：频段积分在功率域做，再开方取回幅度，比"取频段内最大 bin"平滑得多，
        // 也不会因为频段比一个 bin 还窄而照抄同一个值。
        // First build the power spectrum: bands integrate in the power domain and take the square root afterwards, which is far
        // smoother than "largest bin in the band" and never copies one bin's value into a band narrower than a bin.
        for (var bin = 0; bin <= nyquistBin; bin++)
        {
            _powerSpectrum[bin] = _fftReal[bin] * _fftReal[bin] + _fftImaginary[bin] * _fftImaginary[bin];
        }

        // 与旧的 2/N 幅度归一化保持一致：幅度 = sqrt(功率积分 × 4/N²)。
        // Keeps the old 2/N amplitude normalization: magnitude = sqrt(integrated power × 4/N²).
        var normalization = 4.0 / ((double)_fftSize * _fftSize);
        var edges = ResolveBandEdges(bandCount);
        var peakMagnitude = 0d;
        for (var band = 0; band < bandCount; band++)
        {
            var low = edges[band];
            var high = edges[band + 1];
            var power = SpectrumAnalysisPolicy.IntegrateBandPower(_powerSpectrum, binWidth, low, high);
            // 频段中心取几何中心：对数频段上它才是听觉意义上的"中间"。
            // The band center is the geometric one: on a logarithmic axis that is the perceptual middle.
            var center = Math.Sqrt((double)low * high);
            var magnitude = Math.Sqrt(power * normalization * SpectrumAnalysisPolicy.ResolveTiltPowerGain(center));
            _bandMagnitudes[band] = magnitude;
            peakMagnitude = Math.Max(peakMagnitude, magnitude);
        }

        // 先按真实时间推进参考峰值，再把各频段映射成相对它的 dB 电平；这样显示强弱与系统音量的绝对大小无关。
        // The reference peak is advanced by real time first, then every band is mapped to a level relative to it, so display strength
        // no longer follows the absolute system volume.
        var now = Environment.TickCount64;
        var elapsed = _lastSpectrumTick == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(now - _lastSpectrumTick);
        _lastSpectrumTick = now;
        _spectrumReference = SpectrumLevelPolicy.UpdateReference(_spectrumReference, peakMagnitude, elapsed);

        for (var band = 0; band < bandCount; band++)
        {
            bands[band] = SpectrumLevelPolicy.ToNormalizedLevel(_bandMagnitudes[band], _spectrumReference);
        }
    }

    /// <summary>返回请求柱数对应的频段边界，柱数变化时重建缓存。 / Returns the band edges for the requested count, rebuilding the cache when the count changes.</summary>
    private float[] ResolveBandEdges(int bandCount)
    {
        if (bandCount == _bandEdgeCount)
            return _bandEdges;

        _bandEdges = SpectrumBandPolicy.CreateBandEdges(bandCount);
        _bandEdgeCount = bandCount;
        return _bandEdges;
    }

    private void TransformFft()
    {
        var target = 0;
        for (var source = 1; source < _fftSize; source++)
        {
            var bit = _fftSize >> 1;
            while ((target & bit) != 0)
            {
                target ^= bit;
                bit >>= 1;
            }

            target ^= bit;
            if (source < target)
            {
                (_fftReal[source], _fftReal[target]) = (_fftReal[target], _fftReal[source]);
                (_fftImaginary[source], _fftImaginary[target]) =
                    (_fftImaginary[target], _fftImaginary[source]);
            }
        }

        for (var length = 2; length <= _fftSize; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var stepReal = Math.Cos(angle);
            var stepImaginary = Math.Sin(angle);
            for (var offset = 0; offset < _fftSize; offset += length)
            {
                var rotationReal = 1d;
                var rotationImaginary = 0d;
                var halfLength = length >> 1;
                for (var index = 0; index < halfLength; index++)
                {
                    var evenIndex = offset + index;
                    var oddIndex = evenIndex + halfLength;
                    var oddReal = _fftReal[oddIndex] * rotationReal -
                        _fftImaginary[oddIndex] * rotationImaginary;
                    var oddImaginary = _fftReal[oddIndex] * rotationImaginary +
                        _fftImaginary[oddIndex] * rotationReal;
                    var evenReal = _fftReal[evenIndex];
                    var evenImaginary = _fftImaginary[evenIndex];
                    _fftReal[evenIndex] = evenReal + oddReal;
                    _fftImaginary[evenIndex] = evenImaginary + oddImaginary;
                    _fftReal[oddIndex] = evenReal - oddReal;
                    _fftImaginary[oddIndex] = evenImaginary - oddImaginary;

                    var nextRotationReal = rotationReal * stepReal -
                        rotationImaginary * stepImaginary;
                    rotationImaginary = rotationReal * stepImaginary +
                        rotationImaginary * stepReal;
                    rotationReal = nextRotationReal;
                }
            }
        }
    }

    private static double[] CreateFftWindow(int fftSize)
    {
        var window = new double[fftSize];
        for (var index = 0; index < fftSize; index++)
        {
            window[index] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (fftSize - 1));
        }

        return window;
    }

}
