using System.Buffers.Binary;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 通过 WASAPI 回环采集默认输出，并以复用缓冲区计算可变段数的 FFT 频谱。
/// Captures the default render loopback and computes a variable-count FFT spectrum with reused buffers.
/// </summary>
public sealed class AudioMonitorService : IDisposable, IMemoryPrunable
{
    private const int FftSize = 512;
    private const int SampleRingSize = 4096;
    private const int InitialPacketBufferSize = 64 * 1024;
    private const int InitialCaptureRetryMilliseconds = 100;
    private const int SecondCaptureRetryMilliseconds = 500;
    private const int ThirdCaptureRetryMilliseconds = 1_000;
    private const int MaximumCaptureRetryMilliseconds = 3_000;
    // 采集设备检查的低频节奏：两秒一次端点与会话枚举，远低于任何可感知开销，又能让"出声设备变化"在两秒内被跟上。
    // Low cadence for the capture-device check: one endpoint and session enumeration every two seconds, far below any perceptible cost,
    // while a change of the audible device is followed within about two seconds.
    private const int CaptureDeviceCheckIntervalMilliseconds = 2_000;

    /// <summary>"有会话在出声"的峰值下限。会话表是端点音量之前的数值，因此不受系统音量影响，低音量听歌也能被识别。
    /// Session peak that counts as audible. Session meters are pre-endpoint-volume, so system volume does not scale them and quiet
    /// listening is still recognized.</summary>
    private const float AudibleSessionPeakThreshold = 0.002f;

    /// <summary>设备状态掩码：只枚举已启用的渲染端点。/ Device state mask: enumerate active render endpoints only.</summary>
    private const int DeviceStateActive = 0x1;
    // audioclient.h / mmreg.h：WASAPI 回环、静音包和 WAVE 格式常量。
    // audioclient.h / mmreg.h: WASAPI loopback, silent-buffer, and WAVE constants.
    private const uint AudioClientStreamFlagsLoopback = 0x00020000;
    private const uint AudioCaptureBufferFlagsSilent = 0x00000002;
    private const ushort WaveFormatPcm = 0x0001;
    private const ushort WaveFormatIeeeFloat = 0x0003;
    private const ushort WaveFormatExtensibleTag = 0xFFFE;

    private static readonly Guid AudioClientId =
        new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid AudioCaptureClientId =
        new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    private static readonly Guid PcmSubFormat =
        new("00000001-0000-0010-8000-00AA00389B71");
    private static readonly Guid FloatSubFormat =
        new("00000003-0000-0010-8000-00AA00389B71");
    private static readonly Guid AudioSessionManagerId =
        new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    private readonly float[] _sampleRing = new float[SampleRingSize];
    private readonly double[] _fftReal = new double[FftSize];
    private readonly double[] _fftImaginary = new double[FftSize];
    private readonly double[] _fftWindow = CreateFftWindow();
    // 频段边界只随柱数变化，因此按柱数缓存；采样线程每帧都要读它，不能每帧重新计算等比数列。
    // Band edges change only with the bar count, so they are cached per count: the sampling thread reads them every frame
    // and must not rebuild the geometric progression each time.
    private float[] _bandEdges = SpectrumBandPolicy.CreateBandEdges(SpectrumComponentSettings.DefaultBandCount);
    private int _bandEdgeCount = SpectrumComponentSettings.DefaultBandCount;
    // 频段幅度先整体算完再映射：相对 dB 窗口需要一个"本次采样的最大幅度"作为参考更新的输入。
    // Band magnitudes are computed in full before mapping: the relative dB window needs this sample's largest magnitude to advance the reference.
    private readonly double[] _bandMagnitudes = new double[SpectrumComponentSettings.MaximumBandCount];
    private byte[] _packetBuffer = new byte[InitialPacketBufferSize];
    // 这些 COM 对象跨采样复用；切换设备或 Dispose 时必须按依赖逆序释放。
    // These COM objects span samples and must be released in reverse dependency order.
    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private int _sampleWriteIndex;
    private int _sampleCount;
    private int _sampleRate;
    private int _channelCount;
    private int _blockAlign;
    private int _bitsPerSample;
    private ushort _formatTag;
    private long _lastPacketTick;
    private long _nextCaptureAttemptTick;
    private int _captureFailureCount;
    private double _spectrumReference;
    private long _lastSpectrumTick;
    private string? _capturedDeviceId;
    private long _nextCaptureDeviceCheckTick;
    private bool _disposed;

    /// <summary>
    /// 按请求的柱数填充频谱。缓冲区必须容得下全部请求的频段，因为调用方持有它并在两次采样之间复用。
    /// Fills the spectrum for the requested bar count. The buffer must hold every requested band, because the caller owns it
    /// and reuses it between samples.
    /// </summary>
    /// <param name="bands">接收归一化频段值的缓冲区（0–1）。/ Buffer receiving normalized band values, 0–1.</param>
    /// <param name="bandCount">本次要计算的频段数量；越界时被夹取到持久化区间。/ Number of bands to compute; clamped to the persisted range.</param>
    /// <returns>采集可用并已写入频段时为真；无采集或发生异常时为假。/ True when capture was available and the bands were written; false without capture or after a failure.</returns>
    public bool GetSpectrum(float[] bands, int bandCount)
    {
        var count = SpectrumBandPolicy.ClampBandCount(bandCount);
        if (bands.Length < count)
        {
            throw new ArgumentException($"At least {count} bands are required.", nameof(bands));
        }

        if (_disposed || !EnsureCapture())
        {
            Array.Clear(bands, 0, count);
            return false;
        }

        try
        {
            CheckCaptureDeviceChanged();
            if (_captureClient is null)
            {
                // 刚刚因为默认设备变化释放了采集：这一帧返回"无采集"，下一帧的懒建会落在新的默认设备上。
                // The capture was just released because the default device changed: this frame reports "no capture", and the next
                // frame's lazy init lands on the new default device.
                Array.Clear(bands, 0, count);
                return false;
            }

            DrainCapturePackets();
            if (_sampleCount < FftSize ||
                Environment.TickCount64 - _lastPacketTick > 180)
            {
                // 静音/无包期间也让参考按真实时间衰减：恢复播放时柱子从当前位置平滑长回来，而不是因为参考过期先跳到满格。
                // The reference decays by real time during silence or missing packets as well: when playback resumes the bars grow back
                // smoothly instead of jumping to full height because the reference went stale.
                var now = Environment.TickCount64;
                var elapsed = _lastSpectrumTick == 0
                    ? TimeSpan.Zero
                    : TimeSpan.FromMilliseconds(now - _lastSpectrumTick);
                _lastSpectrumTick = now;
                _spectrumReference = SpectrumLevelPolicy.UpdateReference(_spectrumReference, 0, elapsed);
                Array.Clear(bands, 0, count);
                return true;
            }

            CalculateSpectrum(bands, count);
            return true;
        }
        catch
        {
            ReleaseCaptureAndScheduleRetry();
            Array.Clear(bands, 0, count);
            return false;
        }
    }

    public void ResetAfterEnvironmentChange()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseCapture();
        ReleaseComObject(ref _deviceEnumerator);
        _captureFailureCount = 0;
        _nextCaptureAttemptTick = 0;
    }

    /// <summary>参与者名称，只用于诊断。/ Participant name, used for diagnostics only.</summary>
    public string PruneParticipantName => "audio-capture";

    /// <summary>
    /// 停掉 WASAPI 回环采集，把音频引擎、设备与两个 client 全部还给系统。空闲档位不动它：那时频谱组件可能仍然可见，
    /// 而采集只花一点 CPU、不占内存，真正值得停的是"屏幕已经关了"的情形。
    /// Stops the WASAPI loopback capture and gives the audio engine, the device, and both clients back to the system. The idle level leaves it
    /// alone, because the spectrum component may still be visible then and capture costs a little CPU rather than memory; what is really worth
    /// stopping is a display that is already off.
    ///
    /// 这里不"记住需要重建"：下一次 <see cref="GetSpectrum"/> 会通过 <see cref="EnsureCapture"/> 自己懒建，因此恢复无需额外预热。
    /// Nothing is remembered for later: the next <see cref="GetSpectrum"/> lazily recreates through <see cref="EnsureCapture"/>, so restoring needs
    /// no warm-up.
    /// </summary>
    /// <param name="level">目标档位。/ The target level.</param>
    public void Prune(MemoryPruneLevel level)
    {
        if (_disposed || level < MemoryPruneLevel.DisplayOff)
        {
            return;
        }

        ReleaseCapture();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Stop 必须先于 COM 释放，否则音频引擎仍可能访问 capture client。
        // Stop before releasing COM so the audio engine no longer uses the capture client.
        _disposed = true;
        ReleaseCapture();
        ReleaseComObject(ref _deviceEnumerator);
        GC.SuppressFinalize(this);
    }

    private bool EnsureCapture()
    {
        if (_captureClient is not null)
        {
            return true;
        }

        if (Environment.TickCount64 < _nextCaptureAttemptTick)
        {
            return false;
        }

        nint mixFormatPointer = nint.Zero;
        try
        {
            _deviceEnumerator ??= (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(
                    new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"),
                    throwOnError: true)!)!;

            // 目标设备由"哪个设备在出声"决定（见 TryResolveTargetDevice）：默认端点没有声音而音乐在别的端点时，
            // 采集也必须落在音乐所在的端点上，而不是默认端点。
            // The target device comes from "which device is audible" (see TryResolveTargetDevice): when the default endpoint carries no
            // sound while music plays on another one, the capture has to land on the music's endpoint rather than the default.
            if (!TryResolveTargetDevice(out _device, out _capturedDeviceId) || _device is null)
            {
                return FailCaptureInitialization();
            }

            var audioClientId = AudioClientId;
            if (_device.Activate(
                    ref audioClientId,
                    ClsCtx.All,
                    nint.Zero,
                    out var clientObject) < 0 ||
                clientObject is not IAudioClient audioClient)
            {
                return FailCaptureInitialization();
            }

            _audioClient = audioClient;
            if (_audioClient.GetMixFormat(out mixFormatPointer) < 0 ||
                mixFormatPointer == nint.Zero ||
                !ReadMixFormat(mixFormatPointer))
            {
                return FailCaptureInitialization();
            }

            var sessionId = Guid.Empty;
            if (_audioClient.Initialize(
                    AudioClientShareMode.Shared,
                    AudioClientStreamFlagsLoopback,
                    1_000_000,
                    0,
                    mixFormatPointer,
                    ref sessionId) < 0)
            {
                return FailCaptureInitialization();
            }

            var captureClientId = AudioCaptureClientId;
            if (_audioClient.GetService(ref captureClientId, out var captureObject) < 0 ||
                captureObject is not IAudioCaptureClient captureClient)
            {
                return FailCaptureInitialization();
            }

            _captureClient = captureClient;
            if (_audioClient.Start() < 0)
            {
                return FailCaptureInitialization();
            }

            _sampleWriteIndex = 0;
            _sampleCount = 0;
            _lastPacketTick = Environment.TickCount64;
            _captureFailureCount = 0;
            _nextCaptureAttemptTick = 0;
            return true;
        }
        catch
        {
            return FailCaptureInitialization();
        }
        finally
        {
            if (mixFormatPointer != nint.Zero)
            {
                Marshal.FreeCoTaskMem(mixFormatPointer);
            }
        }
    }

    private bool FailCaptureInitialization()
    {
        ReleaseCaptureAndScheduleRetry();
        return false;
    }

    private void ReleaseCaptureAndScheduleRetry()
    {
        ReleaseCapture();
        _captureFailureCount = Math.Min(_captureFailureCount + 1, 4);
        var retryMilliseconds = _captureFailureCount switch
        {
            1 => InitialCaptureRetryMilliseconds,
            2 => SecondCaptureRetryMilliseconds,
            3 => ThirdCaptureRetryMilliseconds,
            _ => MaximumCaptureRetryMilliseconds
        };
        _nextCaptureAttemptTick = Environment.TickCount64 + retryMilliseconds;
    }

    private bool ReadMixFormat(nint formatPointer)
    {
        var format = Marshal.PtrToStructure<WaveFormatEx>(formatPointer);
        _sampleRate = checked((int)format.SamplesPerSecond);
        _channelCount = format.Channels;
        _blockAlign = format.BlockAlign;
        _bitsPerSample = format.BitsPerSample;
        _formatTag = format.FormatTag;

        if (_formatTag == WaveFormatExtensibleTag && format.ExtraSize >= 22)
        {
            var extensible = Marshal.PtrToStructure<WaveFormatExtensible>(formatPointer);
            _formatTag = extensible.SubFormat == FloatSubFormat
                ? WaveFormatIeeeFloat
                : extensible.SubFormat == PcmSubFormat
                    ? WaveFormatPcm
                    : (ushort)0;
        }

        var supportedBits = _formatTag == WaveFormatIeeeFloat
            ? _bitsPerSample == 32
            : _formatTag == WaveFormatPcm && _bitsPerSample is 16 or 24 or 32;
        return _sampleRate > 0 &&
            _channelCount is > 0 and <= 32 &&
            _blockAlign >= _channelCount &&
            supportedBits;
    }

    private void DrainCapturePackets()
    {
        while (_captureClient!.GetNextPacketSize(out var frameCount) >= 0 && frameCount > 0)
        {
            if (_captureClient.GetBuffer(
                    out var data,
                    out frameCount,
                    out var flags,
                    out _,
                    out _) < 0)
            {
                throw new InvalidOperationException("WASAPI capture buffer unavailable.");
            }

            try
            {
                var frameCountInt = checked((int)frameCount);
                if ((flags & AudioCaptureBufferFlagsSilent) != 0 || data == nint.Zero)
                {
                    for (var frame = 0; frame < frameCountInt; frame++)
                    {
                        PushSample(0);
                    }
                }
                else
                {
                    var byteCount = checked(frameCountInt * _blockAlign);
                    if (_packetBuffer.Length < byteCount)
                    {
                        Array.Resize(ref _packetBuffer, Math.Max(byteCount, _packetBuffer.Length * 2));
                    }

                    Marshal.Copy(data, _packetBuffer, 0, byteCount);
                    DecodeFrames(frameCountInt);
                }

                _lastPacketTick = Environment.TickCount64;
            }
            finally
            {
                _captureClient.ReleaseBuffer(frameCount);
            }
        }
    }

    private void DecodeFrames(int frameCount)
    {
        var bytesPerSample = _bitsPerSample / 8;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var frameOffset = frame * _blockAlign;
            var sum = 0f;
            for (var channel = 0; channel < _channelCount; channel++)
            {
                var offset = frameOffset + channel * bytesPerSample;
                sum += ReadSample(offset);
            }

            PushSample(Math.Clamp(sum / _channelCount, -1f, 1f));
        }
    }

    private float ReadSample(int offset)
    {
        var sample = _packetBuffer.AsSpan(offset);
        if (_formatTag == WaveFormatIeeeFloat)
        {
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sample));
        }

        return _bitsPerSample switch
        {
            16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
            24 => ReadPcm24(sample) / 8388608f,
            32 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f,
            _ => 0
        };
    }

    private static int ReadPcm24(ReadOnlySpan<byte> sample)
    {
        var value = sample[0] | sample[1] << 8 | sample[2] << 16;
        return (value & 0x00800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }

    private void PushSample(float sample)
    {
        _sampleRing[_sampleWriteIndex] = sample;
        _sampleWriteIndex = (_sampleWriteIndex + 1) % SampleRingSize;
        _sampleCount = Math.Min(_sampleCount + 1, SampleRingSize);
    }

    private void CalculateSpectrum(float[] bands, int bandCount)
    {
        var start = (_sampleWriteIndex - FftSize + SampleRingSize) % SampleRingSize;
        for (var index = 0; index < FftSize; index++)
        {
            _fftReal[index] = _sampleRing[(start + index) % SampleRingSize] * _fftWindow[index];
            _fftImaginary[index] = 0;
        }

        TransformFft();
        var edges = ResolveBandEdges(bandCount);
        var binWidth = _sampleRate / (double)FftSize;
        var nyquistBin = FftSize / 2 - 1;
        var peakMagnitude = 0d;
        for (var band = 0; band < bandCount; band++)
        {
            var firstBin = Math.Clamp((int)Math.Ceiling(edges[band] / binWidth), 1, nyquistBin);
            var lastBin = Math.Clamp((int)Math.Floor(edges[band + 1] / binWidth), firstBin, nyquistBin);
            var maximum = 0d;
            for (var bin = firstBin; bin <= lastBin; bin++)
            {
                var magnitude = Math.Sqrt(
                    _fftReal[bin] * _fftReal[bin] +
                    _fftImaginary[bin] * _fftImaginary[bin]) * 2 / FftSize;
                maximum = Math.Max(maximum, magnitude);
            }

            _bandMagnitudes[band] = maximum;
            peakMagnitude = Math.Max(peakMagnitude, maximum);
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
        for (var source = 1; source < FftSize; source++)
        {
            var bit = FftSize >> 1;
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

        for (var length = 2; length <= FftSize; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var stepReal = Math.Cos(angle);
            var stepImaginary = Math.Sin(angle);
            for (var offset = 0; offset < FftSize; offset += length)
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

    private void ReleaseCapture()
    {
        if (_audioClient is not null)
        {
            try
            {
                _audioClient.Stop();
            }
            catch
            {
                // 切换设备时端点可能已消失。 / The endpoint may vanish during device changes.
            }
        }

        ReleaseComObject(ref _captureClient);
        ReleaseComObject(ref _audioClient);
        ReleaseComObject(ref _device);
        _sampleWriteIndex = 0;
        _sampleCount = 0;
        // 采集重建后信号链是全新的：参考与时间戳一起归零，避免旧参考把新链路的电平压低或抬高。
        // The signal path is brand new after a capture restart: the reference and its timestamp reset together so an old reference
        // cannot scale the new path's levels down or up.
        _spectrumReference = 0;
        _lastSpectrumTick = 0;
        _capturedDeviceId = null;
    }

    /// <summary>
    /// 低频检查当前采集的端点是否仍是"在出声的那个设备"，不是就释放采集，让下一次懒建落在新的目标端点上。
    ///
    /// 检查对象不是"系统默认设备"，而是"实际出声的设备"：虚拟声卡会把不同应用路由到不同端点，系统默认端点可能完全没有声音，
    /// 只跟随默认端点时频谱会一直停在静音（把音乐实际所在的端点设为默认只是碰巧能用的特例）。
    /// Checks at a low cadence whether the captured endpoint is still the audible device, and releases the capture when it is not, so the
    /// next lazy init lands on the new target.
    ///
    /// The target is the audible device, not the system default: a virtual audio driver routes different applications to different
    /// endpoints and the default may carry no sound at all, in which case following only the default leaves the spectrum silent (setting
    /// the music's endpoint as the default is merely the special case where that happens to work).
    /// </summary>
    private void CheckCaptureDeviceChanged()
    {
        if (_captureClient is null || _deviceEnumerator is null || string.IsNullOrEmpty(_capturedDeviceId))
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now < _nextCaptureDeviceCheckTick)
        {
            return;
        }

        _nextCaptureDeviceCheckTick = now + CaptureDeviceCheckIntervalMilliseconds;

        IMMDevice? target = null;
        try
        {
            if (!TryResolveTargetDevice(out target, out var targetId) || target is null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(targetId) &&
                !string.Equals(targetId, _capturedDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                ReleaseCaptureAndScheduleRetry();
            }
        }
        catch
        {
            // 设备在检查期间消失：交给既有的失败重试路径处理，检查本身绝不向外抛。
            // A device vanishing mid-check is left to the existing failure-retry path; this check never throws outward.
        }
        finally
        {
            ReleaseComObject(ref target);
        }
    }

    /// <summary>
    /// 解析本次采集应使用的输出端点：候选枚举交给 <see cref="AudioCaptureDevicePolicy"/> 决策，这里只负责把每个端点的
    /// 可听状态测出来（会话是否在播、是应用还是系统混音、峰值多少）。
    /// Resolves the render endpoint this capture should use: the choice itself is made by <see cref="AudioCaptureDevicePolicy"/>, while
    /// this method only measures each endpoint's audibility (whether a session plays, whether it is an application or the system mixer,
    /// and its peak).
    /// </summary>
    /// <param name="device">解析出的端点；失败时为空。/ The resolved endpoint, or null on failure.</param>
    /// <param name="deviceId">端点标识；失败时为空。/ The endpoint identifier, or null on failure.</param>
    private bool TryResolveTargetDevice(out IMMDevice? device, out string? deviceId)
    {
        device = null;
        deviceId = null;
        if (_deviceEnumerator is null)
        {
            return false;
        }

        var defaultId = ResolveDefaultEndpointId();
        var candidates = new List<AudioEndpointAudibility>();
        if (_deviceEnumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out var collection) < 0 ||
            collection is null)
        {
            return false;
        }

        try
        {
            if (collection.GetCount(out var count) < 0)
            {
                return false;
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
        }
        finally
        {
            ReleaseComObject(ref collection);
        }

        var targetId = AudioCaptureDevicePolicy.SelectTarget(_capturedDeviceId, defaultId, candidates);
        if (string.IsNullOrEmpty(targetId) ||
            _deviceEnumerator.GetDevice(targetId, out device) < 0 ||
            device is null)
        {
            device = null;
            return false;
        }

        deviceId = targetId;
        return true;
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
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "audiodg", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>解析系统默认多媒体端点的标识；不可用时返回 null。/ Resolves the system default multimedia endpoint identifier, or null.</summary>
    private string? ResolveDefaultEndpointId()
    {
        if (_deviceEnumerator is null)
        {
            return null;
        }

        try
        {
            if (_deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var endpoint) < 0 ||
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

    private static double[] CreateFftWindow()
    {
        var window = new double[FftSize];
        for (var index = 0; index < FftSize; index++)
        {
            window[index] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (FftSize - 1));
        }

        return window;
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

    private enum AudioClientShareMode
    {
        Shared,
        Exclusive
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

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        internal ushort FormatTag;
        internal ushort Channels;
        internal uint SamplesPerSecond;
        internal uint AverageBytesPerSecond;
        internal ushort BlockAlign;
        internal ushort BitsPerSample;
        internal ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatExtensible
    {
        internal WaveFormatEx Format;
        internal ushort ValidBitsPerSample;
        internal uint ChannelMask;
        internal Guid SubFormat;
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

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out uint count);
        int Item(uint index, out IMMDevice device);
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

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        int Initialize(
            AudioClientShareMode shareMode,
            uint streamFlags,
            long bufferDuration,
            long periodicity,
            nint format,
            ref Guid audioSessionGuid);
        int GetBufferSize(out uint bufferFrameCount);
        int GetStreamLatency(out long latency);
        int GetCurrentPadding(out uint paddingFrameCount);
        int IsFormatSupported(AudioClientShareMode shareMode, nint format, out nint closestMatch);
        int GetMixFormat(out nint deviceFormat);
        int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        int Start();
        int Stop();
        int Reset();
        int SetEventHandle(nint eventHandle);
        int GetService(
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        int GetBuffer(
            out nint data,
            out uint frameCount,
            out uint flags,
            out ulong devicePosition,
            out ulong performanceCounterPosition);
        int ReleaseBuffer(uint frameCount);
        int GetNextPacketSize(out uint frameCount);
    }
}
