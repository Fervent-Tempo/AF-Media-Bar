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
    private const int MinimumRingSize = 4_096;
    private const int InitialPacketBufferSize = 64 * 1024;
    private const int InitialCaptureRetryMilliseconds = 100;
    private const int SecondCaptureRetryMilliseconds = 500;
    private const int ThirdCaptureRetryMilliseconds = 1_000;
    private const int MaximumCaptureRetryMilliseconds = 3_000;
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

    /// <summary>
    /// 后台解析出的采集目标端点。端点与会话枚举不在本服务里做（见 <see cref="AudioCaptureDeviceResolver"/>）：
    /// 本服务的 UI 调用路径只读这个缓存值，最多做一次字符串比较，绝不做枚举。
    /// The capture target endpoint resolved in the background. Endpoint and session enumeration does not happen in this service (see
    /// <see cref="AudioCaptureDeviceResolver"/>): the UI-facing paths here only read this cached value and at most compare a string,
    /// never enumerate.
    /// </summary>
    private readonly AudioCaptureDeviceResolver _deviceResolver;

    // FFT 相关缓冲区随采样率确定点数后再分配（96 kHz 至少要 4096 点才能把 45–206 Hz 的前几段分开），
    // 因此它们不是 readonly 的固定数组；ConfigureFft 在拿到混音格式后统一重建。
    // The FFT buffers are allocated once the sample rate fixes the size (96 kHz needs at least 4096 points to separate the first bands
    // between 45 and 206 Hz), so they are not fixed readonly arrays; ConfigureFft rebuilds them after the mix format is known.
    private float[] _sampleRing = new float[MinimumRingSize];
    private double[] _fftReal = new double[SpectrumAnalysisPolicy.MinimumFftSize];
    private double[] _fftImaginary = new double[SpectrumAnalysisPolicy.MinimumFftSize];
    private double[] _fftWindow = CreateFftWindow(SpectrumAnalysisPolicy.MinimumFftSize);
    private double[] _powerSpectrum = new double[SpectrumAnalysisPolicy.MinimumFftSize / 2 + 1];
    private int _fftSize = SpectrumAnalysisPolicy.MinimumFftSize;
    private int _ringSize = MinimumRingSize;
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
    private bool _disposed;

    /// <summary>
    /// 创建频谱采集服务；目标端点来自后台解析器，本服务不在 UI 路径上做任何枚举。
    /// Creates the spectrum capture service; the target endpoint comes from the background resolver, so this service never enumerates
    /// on a UI-facing path.
    /// </summary>
    /// <param name="deviceResolver">后台端点解析器。/ The background endpoint resolver.</param>
    public AudioMonitorService(AudioCaptureDeviceResolver deviceResolver)
    {
        _deviceResolver = deviceResolver;
    }

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

        if (_disposed)
        {
            Array.Clear(bands, 0, count);
            return false;
        }

        _deviceResolver.RequestScan();
        if (!EnsureCapture())
        {
            Array.Clear(bands, 0, count);
            return false;
        }

        try
        {
            // 目标端点由后台解析器缓存：这里只比较一次字符串，发现变化就释放采集，让下一次懒建落到新目标上。
            // The target endpoint is cached by the background resolver: this only compares one string and releases the capture on a
            // change, so the next lazy init lands on the new target.
            var targetId = _deviceResolver.TargetDeviceId;
            if (_captureClient is not null &&
                !string.IsNullOrEmpty(_capturedDeviceId) &&
                !string.IsNullOrEmpty(targetId) &&
                !string.Equals(targetId, _capturedDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                ReleaseCaptureAndScheduleRetry();
                Array.Clear(bands, 0, count);
                return false;
            }

            DrainCapturePackets();
            if (_sampleCount < _fftSize ||
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

            // 目标端点取自后台解析器的缓存值（"正在出声的那个设备"，见 AudioCaptureDeviceResolver）；解析器尚未给出结果时
            // 回退到系统默认端点，与旧行为一致。这里只做一次 GetDevice/GetDefaultAudioEndpoint 调用，不枚举端点与会话。
            // The target endpoint comes from the background resolver's cache (the device that is actually audible, see
            // AudioCaptureDeviceResolver); before the first resolution it falls back to the system default endpoint exactly as the old
            // behaviour did. This does one GetDevice or GetDefaultAudioEndpoint call — no endpoint or session enumeration.
            var targetDeviceId = _deviceResolver.TargetDeviceId;
            if (!string.IsNullOrEmpty(targetDeviceId))
            {
                if (_deviceEnumerator.GetDevice(targetDeviceId, out _device) < 0 || _device is null)
                {
                    return FailCaptureInitialization();
                }

                _capturedDeviceId = targetDeviceId;
            }
            else
            {
                if (_deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out _device) < 0 ||
                    _device is null)
                {
                    return FailCaptureInitialization();
                }

                _capturedDeviceId = _device.GetId(out var defaultId) >= 0 && !string.IsNullOrEmpty(defaultId)
                    ? defaultId
                    : null;
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
        var supported = _sampleRate > 0 &&
            _channelCount is > 0 and <= 32 &&
            _blockAlign >= _channelCount &&
            supportedBits;
        if (supported)
        {
            // 采样率一确定就把 FFT 点数定下来：96 kHz 需要 4096 点，低频段才不会挤在同一个 bin 里。
            // The sample rate fixes the FFT size right here: 96 kHz needs 4096 points so the low bands do not crowd into one bin.
            ConfigureFft(_sampleRate);
        }

        return supported;
    }

    /// <summary>
    /// 按采样率重建 FFT 缓冲区（点数为 2 的幂、bin 宽目标见 <see cref="SpectrumAnalysisPolicy.ResolveFftSize"/>）。
    /// 点数不变时不做任何事；重建会同时清空采样环，因为旧样本的点数语义已经不同。
    /// Rebuilds the FFT buffers for a sample rate (the size is a power of two; see <see cref="SpectrumAnalysisPolicy.ResolveFftSize"/>
    /// for the bin-width target). An unchanged size does nothing; a rebuild also clears the sample ring, because old samples belong
    /// to a different size.
    /// </summary>
    private void ConfigureFft(int sampleRate)
    {
        var fftSize = SpectrumAnalysisPolicy.ResolveFftSize(sampleRate);
        if (fftSize == _fftSize)
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
        _sampleWriteIndex = 0;
        _sampleCount = 0;
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

    private static double[] CreateFftWindow(int fftSize)
    {
        var window = new double[fftSize];
        for (var index = 0; index < fftSize; index++)
        {
            window[index] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (fftSize - 1));
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
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out nint devices);
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
