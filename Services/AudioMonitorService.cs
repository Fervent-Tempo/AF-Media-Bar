using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace AFMediaBar.Services;

/// <summary>
/// 通过 WASAPI 回环采集默认输出，并以复用缓冲区计算九段 FFT 频谱。
/// Captures the default render loopback and computes nine FFT bands with reused buffers.
/// </summary>
internal sealed class AudioMonitorService : IDisposable
{
    internal const int BandCount = 9;

    private const int FftSize = 512;
    private const int SampleRingSize = 4096;
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
    private static readonly float[] BandEdges =
        [45, 90, 180, 360, 720, 1400, 2800, 5600, 11000, 20000];

    private readonly float[] _sampleRing = new float[SampleRingSize];
    private readonly double[] _fftReal = new double[FftSize];
    private readonly double[] _fftImaginary = new double[FftSize];
    private readonly double[] _fftWindow = CreateFftWindow();
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
    private bool _disposed;

    internal bool GetSpectrum(float[] bands)
    {
        if (bands.Length < BandCount)
        {
            throw new ArgumentException($"At least {BandCount} bands are required.", nameof(bands));
        }

        if (_disposed || !EnsureCapture())
        {
            Array.Clear(bands, 0, BandCount);
            return false;
        }

        try
        {
            DrainCapturePackets();
            if (_sampleCount < FftSize ||
                Environment.TickCount64 - _lastPacketTick > 180)
            {
                Array.Clear(bands, 0, BandCount);
                return true;
            }

            CalculateSpectrum(bands);
            return true;
        }
        catch
        {
            ReleaseCaptureAndScheduleRetry();
            Array.Clear(bands, 0, BandCount);
            return false;
        }
    }

    internal void ResetAfterEnvironmentChange()
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

            if (_deviceEnumerator.GetDefaultAudioEndpoint(
                    EDataFlow.Render,
                    ERole.Multimedia,
                    out _device) < 0 ||
                _device is null)
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

    private void CalculateSpectrum(float[] bands)
    {
        var start = (_sampleWriteIndex - FftSize + SampleRingSize) % SampleRingSize;
        for (var index = 0; index < FftSize; index++)
        {
            _fftReal[index] = _sampleRing[(start + index) % SampleRingSize] * _fftWindow[index];
            _fftImaginary[index] = 0;
        }

        TransformFft();
        var binWidth = _sampleRate / (double)FftSize;
        var nyquistBin = FftSize / 2 - 1;
        for (var band = 0; band < BandCount; band++)
        {
            var firstBin = Math.Clamp((int)Math.Ceiling(BandEdges[band] / binWidth), 1, nyquistBin);
            var lastBin = Math.Clamp((int)Math.Floor(BandEdges[band + 1] / binWidth), firstBin, nyquistBin);
            var maximum = 0d;
            for (var bin = firstBin; bin <= lastBin; bin++)
            {
                var magnitude = Math.Sqrt(
                    _fftReal[bin] * _fftReal[bin] +
                    _fftImaginary[bin] * _fftImaginary[bin]) * 2 / FftSize;
                maximum = Math.Max(maximum, magnitude);
            }

            bands[band] = (float)Math.Clamp(
                Math.Log10(1 + maximum * 120) / Math.Log10(121),
                0,
                1);
        }
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
