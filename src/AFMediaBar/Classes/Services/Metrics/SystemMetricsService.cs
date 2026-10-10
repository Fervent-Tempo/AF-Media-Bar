// 平台性能计数器由本服务拥有；监视器串行采样，释放请求不阻塞调用线程。
using System.Diagnostics;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Abstractions;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 采样系统内存、CPU、GPU 与当前进程内存，并复用跨周期计数器状态。
/// Samples system and process metrics while reusing counters across sampling intervals.
/// </summary>
public sealed class SystemMetricsService : IDisposable, ISystemMetricsSampler
{
    private readonly object _gate = new();
    private readonly HashSet<MetricKind> _demand = [];
    private readonly NetworkUsageSampler _network = new();
    private int _disposed;
    private bool _resourcesReleased;
    private long? _gpuRetryTimestamp;
    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private readonly CpuUsagePolicy _cpu = new();
    private GpuUsageSampler? _gpuUsageSampler;

    public void SetDemand(IReadOnlyCollection<MetricKind> metrics)
    {
        lock (_gate)
        {
            try
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return;
                if (!metrics.Contains(MetricKind.SystemCpu))
                    ResetCpuSample();
                if (!metrics.Contains(MetricKind.SystemGpu))
                    ReleaseGpu();
                if (!metrics.Contains(MetricKind.SystemNetwork))
                    _network.Reset();
                _demand.Clear();
                _demand.UnionWith(metrics);
            }
            finally
            {
                if (Volatile.Read(ref _disposed) != 0)
                    ReleaseResources();
            }
        }
    }

    public SystemMetricsSnapshot Sample()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                ReleaseResources();
                return default;
            }
            try
            {
                var memory = ReadIfRequested(MetricKind.SystemMemory, ReadSystemMemoryPercent);
                var cpu = ReadIfRequested(MetricKind.SystemCpu, ReadSystemCpuPercent);
                var gpu = ReadIfRequested(MetricKind.SystemGpu, ReadSystemGpuPercent);
                var process = ReadIfRequested(MetricKind.ProcessMemory, ReadProcessMemoryMegabytes);
                (double? Upload, double? Download) network = (null, null);
                if (_demand.Contains(MetricKind.SystemNetwork))
                {
                    try { network = _network.Sample(); }
                    catch (Exception exception)
                    {
                        _network.Reset();
                        AppLogService.Current?.Warn("Metrics", $"网络采样失败: {exception.Message}");
                    }
                }
                return new(memory, cpu, gpu, process, network.Upload, network.Download);
            }
            finally
            {
                if (Volatile.Read(ref _disposed) != 0)
                    ReleaseResources();
            }
        }
    }

    private T? ReadIfRequested<T>(MetricKind metric, Func<T?> read) where T : struct
    {
        if (!_demand.Contains(metric))
            return null;
        try { return read(); }
        catch (Exception exception)
        {
            if (metric == MetricKind.SystemGpu)
            {
                ReleaseGpu();
                _gpuRetryTimestamp = Stopwatch.GetTimestamp();
            }
            if (metric == MetricKind.SystemCpu)
                ResetCpuSample();
            AppLogService.Current?.Warn("Metrics", $"{metric} 采样失败: {exception.Message}");
            return null;
        }
    }

    private long? ReadProcessMemoryMegabytes()
    {
        _currentProcess.Refresh();
        return (long)Math.Round(_currentProcess.WorkingSet64 / 1024d / 1024d);
    }

    public void Reset()
    {
        lock (_gate)
        {
            ResetCpuSample();
            ReleaseGpu();
            _network.Reset();
            if (Volatile.Read(ref _disposed) != 0)
                ReleaseResources();
        }
    }

    private int? ResetCpuSample()
    {
        _cpu.Reset();
        return null;
    }

    private int? ReadSystemGpuPercent()
    {
        if (_gpuRetryTimestamp is long retry && Stopwatch.GetElapsedTime(retry).TotalSeconds < 5)
            return null;
        _gpuRetryTimestamp = null;
        _gpuUsageSampler ??= new GpuUsageSampler();
        var value = _gpuUsageSampler.Sample();
        if (_gpuUsageSampler.IsFailed)
        {
            ReleaseGpu();
            _gpuRetryTimestamp = Stopwatch.GetTimestamp();
        }
        return value;
    }

    /// <summary>在没有消费者请求 GPU 时立即释放 PDH 查询。 / Releases the PDH GPU query when no consumer requests it.</summary>
    private void ReleaseGpu()
    {
        _gpuUsageSampler?.Dispose();
        _gpuUsageSampler = null;
        _gpuRetryTimestamp = null;
    }

    private static int? ReadSystemMemoryPercent()
    {
        var status = NativeMethods.MemoryStatusEx.Create();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0)
        {
            return null;
        }

        if (status.AvailablePhysical > status.TotalPhysical)
            return null;
        var used = status.TotalPhysical - status.AvailablePhysical;
        return (int)Math.Clamp(Math.Round(used * 100d / status.TotalPhysical), 0, 100);
    }

    private int? ReadSystemCpuPercent()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return ResetCpuSample();
        }

        return _cpu.Sample(idle.ToUInt64(), kernel.ToUInt64(), user.ToUInt64());
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        // 容器停止超时时也不在 UI 线程等待采样；正在运行的 Sample 会在 finally 释放。
        if (!Monitor.TryEnter(_gate))
            return;
        try { ReleaseResources(); }
        finally { Monitor.Exit(_gate); }
    }

    private void ReleaseResources()
    {
        if (_resourcesReleased)
            return;
        _resourcesReleased = true;
        ReleaseGpu();
        _network.Reset();
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
        public bool IsFailed { get; private set; }

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
                IsFailed = true;
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
                IsFailed = true;
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
                IsFailed = true;
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
                if (item.Name == nint.Zero || !double.IsFinite(item.Value.DoubleValue))
                    continue;

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

            // 先分配新缓冲区；分配失败时旧地址仍有效，随后清理不会重复释放旧地址。
            var buffer = Marshal.AllocHGlobal(checked((int)requiredCapacity));
            if (_buffer != nint.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
            }

            _buffer = buffer;
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
