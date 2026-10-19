// 主程序和隔离执行端共用只读系统计数；只在操作前后采样，不执行清理或持有后台资源。
using System.Runtime.InteropServices;

namespace AFMediaBar.Classes.Services;

/// <summary>系统可用内存与缓存（包含待机页），单位为字节；读取失败保留空值。</summary>
public sealed record SystemMemoryCounters(ulong? AvailableBytes, ulong? CacheBytes);

/// <summary>使用公开的 GetPerformanceInfo 读取物理内存，不依赖性能监测组件。</summary>
public static class SystemMemoryCountersReader
{
    public static SystemMemoryCounters Capture()
    {
        var information = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (!GetPerformanceInfo(ref information, information.Size)) return new(null, null);
        try
        {
            return new(checked((ulong)information.PhysicalAvailable * (ulong)information.PageSize),
                checked((ulong)information.SystemCache * (ulong)information.PageSize));
        }
        catch (OverflowException) { return new(null, null); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache;
        public nuint KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(ref PerformanceInformation information, uint size);
}
