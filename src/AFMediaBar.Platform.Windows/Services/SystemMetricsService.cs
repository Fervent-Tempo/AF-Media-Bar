using System.Diagnostics;
using System.Runtime.InteropServices;
using AFMediaBar.Interop;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 采样系统内存、CPU、GPU 与当前进程内存，并复用跨周期计数器状态。
/// Samples system and process metrics while reusing counters across sampling intervals.
/// </summary>
public sealed class SystemMetricsService : IDisposable
{
    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasPreviousCpuSample;
    private GpuUsageSampler? _gpuUsageSampler;

    public SystemMetricsSnapshot Sample(MetricSettings settings)
    {
        var includeMetrics = settings.Enabled;
        var systemMemoryPercent = includeMetrics && settings.ShowSystemMemory
            ? ReadSystemMemoryPercent()
            : 0;
        var systemCpuPercent = includeMetrics && settings.ShowSystemCpu
            ? ReadSystemCpuPercent()
            : ResetCpuSample();
        var systemGpuPercent = ReadSystemGpuPercent(
            includeMetrics && settings.ShowSystemGpu);
        var processMemoryMegabytes = 0L;
        if (includeMetrics && settings.ShowProcessMemory)
        {
            _currentProcess.Refresh();
            processMemoryMegabytes = (long)Math.Round(
                _currentProcess.WorkingSet64 / 1024d / 1024d);
        }

        return new SystemMetricsSnapshot(
            systemMemoryPercent,
            systemCpuPercent,
            systemGpuPercent,
            processMemoryMegabytes);
    }

    private int? ResetCpuSample()
    {
        _hasPreviousCpuSample = false;
        return null;
    }

    private int? ReadSystemGpuPercent(bool includeGpu)
    {
        if (!includeGpu)
        {
            // PDH 查询持有原生句柄和非托管缓冲区，禁用 GPU 指标时立即释放。
            // PDH owns native handles and buffers; release them as soon as GPU metrics stop.
            _gpuUsageSampler?.Dispose();
            _gpuUsageSampler = null;
            return null;
        }

        _gpuUsageSampler ??= new GpuUsageSampler();
        return _gpuUsageSampler.Sample();
    }

    private static int ReadSystemMemoryPercent()
    {
        var status = NativeMethods.MemoryStatusEx.Create();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0)
        {
            return 0;
        }

        var used = status.TotalPhysical - status.AvailablePhysical;
        return (int)Math.Clamp(Math.Round(used * 100d / status.TotalPhysical), 0, 100);
    }

    private int? ReadSystemCpuPercent()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }

        var currentIdle = idle.ToUInt64();
        var currentKernel = kernel.ToUInt64();
        var currentUser = user.ToUInt64();

        if (!_hasPreviousCpuSample)
        {
            _previousIdle = currentIdle;
            _previousKernel = currentKernel;
            _previousUser = currentUser;
            _hasPreviousCpuSample = true;
            return null;
        }

        var idleDelta = currentIdle - _previousIdle;
        var kernelDelta = currentKernel - _previousKernel;
        var userDelta = currentUser - _previousUser;
        var totalDelta = kernelDelta + userDelta;

        _previousIdle = currentIdle;
        _previousKernel = currentKernel;
        _previousUser = currentUser;

        if (totalDelta == 0)
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Round((totalDelta - idleDelta) * 100d / totalDelta), 0, 100);
    }

    public void Dispose()
    {
        _gpuUsageSampler?.Dispose();
        _currentProcess.Dispose();
    }

    private sealed class GpuUsageSampler : IDisposable
    {
        private const string CounterPath = @"\GPU Engine(*)\Utilization Percentage";
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;
        private static readonly int ItemSize =
            Marshal.SizeOf<NativeMethods.PdhFmtCounterValueItem>();

        private readonly Dictionary<EngineKey, double> _engineTotals = [];
        private nint _query;
        private nint _counter;
        private nint _buffer;
        private uint _bufferCapacity;
        private bool _hasBaseline;

        internal GpuUsageSampler()
        {
            if (NativeMethods.PdhOpenQuery(null, nint.Zero, out _query) !=
                NativeMethods.ErrorSuccess)
            {
                _query = nint.Zero;
                return;
            }

            if (NativeMethods.PdhAddEnglishCounter(
                    _query,
                    CounterPath,
                    nint.Zero,
                    out _counter) == NativeMethods.ErrorSuccess)
            {
                return;
            }

            _ = NativeMethods.PdhCloseQuery(_query);
            _query = nint.Zero;
            _counter = nint.Zero;
        }

        internal int? Sample()
        {
            if (_query == nint.Zero ||
                NativeMethods.PdhCollectQueryData(_query) != NativeMethods.ErrorSuccess)
            {
                return null;
            }

            if (!_hasBaseline)
            {
                _hasBaseline = true;
                return null;
            }

            uint bufferSize = 0;
            uint itemCount = 0;
            var status = NativeMethods.PdhGetFormattedCounterArray(
                _counter,
                NativeMethods.PdhFmtDouble,
                ref bufferSize,
                ref itemCount,
                nint.Zero);
            if (status != NativeMethods.PdhMoreData || bufferSize == 0 || itemCount == 0)
            {
                return null;
            }

            EnsureBufferCapacity(bufferSize);
            bufferSize = _bufferCapacity;
            status = NativeMethods.PdhGetFormattedCounterArray(
                _counter,
                NativeMethods.PdhFmtDouble,
                ref bufferSize,
                ref itemCount,
                _buffer);
            if (status != NativeMethods.ErrorSuccess)
            {
                return null;
            }

            _engineTotals.Clear();
            var count = checked((int)itemCount);
            for (var index = 0; index < count; index++)
            {
                var itemPointer = IntPtr.Add(_buffer, checked(index * ItemSize));
                var item = Marshal.PtrToStructure<NativeMethods.PdhFmtCounterValueItem>(
                    itemPointer);
                if (item.Value.Status is not NativeMethods.PdhStatusValidData and
                    not NativeMethods.PdhStatusNewData)
                {
                    continue;
                }

                var key = GetEngineKey(item.Name);
                _engineTotals.TryGetValue(key, out var total);
                _engineTotals[key] = total + Math.Max(0, item.Value.DoubleValue);
            }

            if (_engineTotals.Count == 0)
            {
                return null;
            }

            var busiestEngine = _engineTotals.Values.Max();
            return (int)Math.Clamp(Math.Round(busiestEngine), 0, 100);
        }

        private void EnsureBufferCapacity(uint requiredCapacity)
        {
            if (_buffer != nint.Zero && _bufferCapacity >= requiredCapacity)
            {
                return;
            }

            if (_buffer != nint.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
            }

            _buffer = Marshal.AllocHGlobal(checked((int)requiredCapacity));
            _bufferCapacity = requiredCapacity;
        }

        private static EngineKey GetEngineKey(nint instanceName)
        {
            var start = FindLuidOffset(instanceName);
            var hash = FnvOffsetBasis;
            var length = 0;
            for (var offset = start; ; offset += sizeof(char))
            {
                var character = unchecked((char)(ushort)Marshal.ReadInt16(instanceName, offset));
                if (character == '\0')
                {
                    break;
                }

                if (character is >= 'A' and <= 'Z')
                {
                    character = (char)(character + ('a' - 'A'));
                }

                hash ^= character;
                hash *= FnvPrime;
                length++;
            }

            return new EngineKey(hash, length);
        }

        private static int FindLuidOffset(nint instanceName)
        {
            ReadOnlySpan<char> token = "luid_";
            for (var offset = 0; ; offset += sizeof(char))
            {
                var character = unchecked((char)(ushort)Marshal.ReadInt16(instanceName, offset));
                if (character == '\0')
                {
                    return 0;
                }

                var matches = true;
                for (var tokenIndex = 0; tokenIndex < token.Length; tokenIndex++)
                {
                    var candidate = unchecked((char)(ushort)Marshal.ReadInt16(
                        instanceName,
                        offset + tokenIndex * sizeof(char)));
                    if (candidate == '\0' ||
                        char.ToLowerInvariant(candidate) != token[tokenIndex])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return offset;
                }
            }
        }

        public void Dispose()
        {
            if (_buffer != nint.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = nint.Zero;
                _bufferCapacity = 0;
            }

            if (_query != nint.Zero)
            {
                _ = NativeMethods.PdhCloseQuery(_query);
                _query = nint.Zero;
                _counter = nint.Zero;
            }
        }

        private readonly record struct EngineKey(ulong Hash, int Length);
    }
}
