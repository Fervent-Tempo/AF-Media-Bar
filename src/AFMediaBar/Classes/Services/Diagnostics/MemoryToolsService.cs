// 按需采集内存快照；不持有计时器，调用方须在后台线程执行采样。
using System.Diagnostics;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Interop;

namespace AFMediaBar.Classes.Services;

/// <summary>带采样时间的内存诊断；读取失败以 null 表达。</summary>
public sealed record MemoryToolsSnapshot(DateTimeOffset At, ProcessMemoryCounters? Process, ulong? AvailableBytes);

/// <summary>手动回收的实际状态，不把未开始或失败当成成功。</summary>
public enum MemoryTrimStatus { Completed, Busy, Cancelled, Unavailable, Failed }

/// <summary>一次按需回收的结果；取消仅适用于开始之前。</summary>
public sealed record MemoryTrimOutcome(MemoryTrimStatus Status, MemoryTrimStrength Strength,
    WorkingSetTrimResult? Counters = null, TimeSpan Elapsed = default, string? Error = null);

/// <summary>内存诊断采样，不产生 GC 或工作集操作。</summary>
public sealed class MemoryToolsService
{
    public MemoryToolsSnapshot Capture()
    {
        ProcessMemoryCounters? counters = null;
        try
        {
            using var process = Process.GetCurrentProcess();
            counters = new(process.WorkingSet64, process.PrivateMemorySize64, GC.GetTotalMemory(false),
                process.HandleCount, process.Threads.Count);
        }
        catch (Exception) { }
        return new(DateTimeOffset.Now, counters, CaptureAvailableBytes());
    }

    public static ulong? CaptureAvailableBytes()
    {
        var memory = new NativeMethods.MemoryStatusEx { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
        return NativeMethods.GlobalMemoryStatusEx(ref memory) ? memory.AvailablePhysical : null;
    }
}
