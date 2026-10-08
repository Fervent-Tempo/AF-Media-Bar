using System.Diagnostics;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Interop;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 一次进程内存读数，用于剪枝前后的对照与日志。
/// One process memory reading, used to compare the state before and after a prune and to write the log line.
/// </summary>
/// <param name="WorkingSetBytes">工作集（物理内存）。/ Working set, the physical memory in use.</param>
/// <param name="PrivateBytes">私有字节（提交量）。/ Private bytes, the committed private memory.</param>
/// <param name="ManagedHeapBytes">托管堆大小。/ Managed heap size.</param>
/// <param name="HandleCount">句柄数。/ Handle count.</param>
/// <param name="ThreadCount">线程数。/ Thread count.</param>
public readonly record struct ProcessMemoryCounters(
    long WorkingSetBytes,
    long PrivateBytes,
    long ManagedHeapBytes,
    int HandleCount,
    int ThreadCount)
{
    /// <summary>格式化为一行便于对比的读数（MB，一位小数）。/ Formats the reading as one comparable line in megabytes with one decimal.</summary>
    /// <returns>读数文本。/ The formatted reading.</returns>
    public string Describe() =>
        $"WorkSet {Megabytes(WorkingSetBytes)}{Megabytes(PrivateBytes)}{Megabytes(ManagedHeapBytes)} / " +
        $"句柄 handles {HandleCount} / 线程 threads {ThreadCount}";

    private static string Megabytes(long bytes) => $"{bytes / 1024d / 1024d:0.0} MB";
}

/// <summary>
/// 一次回收的结果：剪枝前后的读数，以及哪几项原生手段真正生效。
/// The result of one reclaim: the readings before and after, plus which native actions actually took effect.
/// </summary>
/// <param name="Level">执行的档位。/ The level that was executed.</param>
/// <param name="Before">剪枝前的读数。/ The reading before.</param>
/// <param name="After">剪枝后的读数。/ The reading after.</param>
/// <param name="EmptiedWorkingSet">工作集是否已被交还。/ Whether the working set was returned.</param>
/// <param name="MemoryPriority">实际写入的内存优先级。/ The memory priority that was actually written.</param>
/// <param name="EcoQosEnabled">是否已开启执行速度节流。/ Whether execution-speed throttling is on.</param>
public readonly record struct MemoryReclaimResult(
    MemoryPruneLevel Level,
    ProcessMemoryCounters Before,
    ProcessMemoryCounters After,
    bool EmptiedWorkingSet,
    uint MemoryPriority,
    bool EcoQosEnabled);

/// <summary>
/// 一次按需回收的结果：强度与前后读数。
/// The result of one on-demand reclaim: the strength and the readings before and after.
/// </summary>
/// <param name="Strength">使用的强度。/ The strength used.</param>
/// <param name="Before">回收前的读数。/ The reading before.</param>
/// <param name="After">回收后的读数。/ The reading after.</param>
/// <param name="ReturnedWorkingSet">是否真的交还了工作集（温和路径恒为 false）。/ Whether the working set was actually returned; always false on the gentle path.</param>
public readonly record struct WorkingSetTrimResult(
    MemoryTrimStrength Strength,
    ProcessMemoryCounters Before,
    ProcessMemoryCounters After,
    bool ReturnedWorkingSet);

/// <summary>
/// 执行进程级的内存回收：GC 压缩、交还工作集、降低内存优先级、开启执行速度节流（EcoQoS）。
/// Performs the process-level reclaim: a compacting GC, returning the working set, lowering the memory priority, and turning on
/// execution-speed throttling (EcoQoS).
///
/// 这些手段都很粗暴，因此只在"确定现在没人需要这个进程马上响应"时才用：常态节能只开节流，交还工作集与压缩 GC 只发生在显示器关闭、
/// 会话锁定或系统睡眠时（见 <see cref="MemoryPrunePolicy"/>）。
/// These actions are blunt, so they only run while it is certain that nobody needs this process to react right away: ordinary power saving only
/// turns on throttling, while returning the working set and the compacting GC wait for a closed display, a locked session, or a suspending
/// system (see <see cref="MemoryPrunePolicy"/>).
///
/// 数据不会被丢掉：交还的是物理页，内容仍在换页文件里，下次访问会重新读回，代价是恢复瞬间的一次页错误高峰。
/// No data is lost: what is returned is the physical page, whose contents stay in the pagefile and are read back on the next access, at the price
/// of a page-fault peak right after resuming.
/// </summary>
public sealed class ProcessMemoryTrimmer
{
    private readonly AppLogService? _log;

    /// <summary>缓存的当前进程对象；`Process.GetCurrentProcess()` 每次都会新建一个对象，常驻路径上不该反复分配。
    /// Cached process object: `Process.GetCurrentProcess()` allocates a new one every call, which a resident path should not keep doing.</summary>
    private readonly Process _process = Process.GetCurrentProcess();

    private uint _appliedMemoryPriority = NativeMethods.MemoryPriorityNormal;
    private bool _ecoQosEnabled;
    private bool _disposed;

    /// <summary>
    /// 创建进程内存回收器。
    /// Creates the process memory trimmer.
    /// </summary>
    /// <param name="log">程序日志；省略时不记录（单元测试）。/ The application log, omitted by unit tests.</param>
    public ProcessMemoryTrimmer(AppLogService? log = null)
    {
        _log = log;
    }

    /// <summary>
    /// 读取当前进程的内存与句柄读数。
    /// Reads the current process's memory and handle counters.
    /// </summary>
    /// <returns>内存读数。/ The memory reading.</returns>
    public ProcessMemoryCounters Capture()
    {
        try
        {
            return new ProcessMemoryCounters(
                _process.WorkingSet64,
                _process.PrivateMemorySize64,
                GC.GetTotalMemory(forceFullCollection: false),
                _process.HandleCount,
                _process.Threads.Count);
        }
        catch (Exception ex)
        {
            // 进程对象在极端情况下（句柄被回收）会失败，读数只是诊断，不能影响剪枝本身。
            // The process object can fail in edge cases; the reading is diagnostics only and must never break the prune itself.
            _log?.Warn("Prune", $"读取内存读数失败: {ex.Message}");
            return default;
        }
    }

    /// <summary>
    /// 只开启常态节流（EcoQoS）：空闲档位下让调度器把这个进程放到效率核与低优先级时间片，不做任何破坏性回收。
    /// Turns on ordinary throttling (EcoQoS) alone: at the idle level the scheduler keeps this process on efficiency cores and low-priority time
    /// slices, with no destructive reclaim.
    /// </summary>
    /// <returns>节流是否已生效。/ Whether throttling is in effect.</returns>
    public bool ApplyIdleThrottling() => SetEcoQos(enabled: true);

    /// <summary>
    /// 按强度做一次"按需回收"：不改变内存优先级与 EcoQoS，只处理托管垃圾（必要时连工作集一起交还）。
    /// Performs one on-demand reclaim at the given strength: the memory priority and EcoQoS are left untouched, and only managed garbage is handled —
    /// together with the working set when the strength asks for it.
    ///
    /// 它与 <see cref="Reclaim"/> 的分工是：`Reclaim` 属于**档位路径**（系统状态决定，含优先级与节流），本方法属于**事件路径**
    /// （某个窗口刚关掉、用户刚点了按钮）。事件路径不动优先级，因为进程此刻仍在正常使用中。
    /// It divides the work with <see cref="Reclaim"/> like this: `Reclaim` belongs to the **level path** — the system state decides, and priority and
    /// throttling come with it — while this method belongs to the **event path**, where a window has just closed or the user just clicked. The event path
    /// leaves the priority alone, because the process is still in normal use at that moment.
    /// </summary>
    /// <param name="strength">回收强度。/ The reclaim strength.</param>
    /// <returns>本次回收的结果与前后读数。/ The result of this reclaim with the readings before and after.</returns>
    public WorkingSetTrimResult TrimWorkingSet(MemoryTrimStrength strength)
    {
        var before = Capture();
        if (strength == MemoryTrimStrength.Gentle)
        {
            // 温和路径刻意不交还工作集：它用在"刚关掉一个窗口、用户随时可能再打开"的场合，
            // 而剥离工作集换来的是下一次交互时的硬缺页。后台 GC 也不会阻塞调用线程。
            // The gentle path deliberately leaves the working set alone: it is used where a window has just closed and may be reopened at any moment, and
            // returning the working set would cost a hard page fault on the next interaction. A background collection also never blocks the caller.
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
            GC.WaitForPendingFinalizers();
            return new WorkingSetTrimResult(strength, before, Capture(), ReturnedWorkingSet: false);
        }

        ForceCompactingCollection();
        ForceCompactingCollection();
        var returned = ReturnWorkingSet();
        return new WorkingSetTrimResult(strength, before, Capture(), returned);
    }

    /// <summary>
    /// 执行一次深度回收：压缩 GC → 降低内存优先级 → 开启节流 → 交还工作集。
    /// Performs one deep reclaim: a compacting GC, a lower memory priority, throttling, and then returning the working set.
    /// </summary>
    /// <param name="level">必须是 <see cref="MemoryPruneLevel.DisplayOff"/> 或更高；更低档位只做节流。
    /// Must be <see cref="MemoryPruneLevel.DisplayOff"/> or higher; lower levels only throttle.</param>
    /// <returns>本次回收的结果与前后读数。/ The result of this reclaim with the readings before and after.</returns>
    public MemoryReclaimResult Reclaim(MemoryPruneLevel level)
    {
        if (level < MemoryPruneLevel.DisplayOff)
        {
            var idleBefore = Capture();
            return new MemoryReclaimResult(level, idleBefore, idleBefore, false, _appliedMemoryPriority, ApplyIdleThrottling());
        }

        var before = Capture();

        // 顺序是有意的：先把托管对象收干净再交还物理页。反过来的话，GC 会在 EmptyWorkingSet 之后重新把页摸一遍，
        // 交还立刻被抵消。两次收集是因为终结器可能让对象复活。
        // The order is deliberate: managed garbage is collected first and the physical pages are returned after. Reversed, the GC would touch
        // the pages again right after EmptyWorkingSet and undo it. Two collections because a finalizer can resurrect an object.
        ForceCompactingCollection();
        ForceCompactingCollection();

        var priority = level >= MemoryPruneLevel.Suspended
            ? NativeMethods.MemoryPriorityVeryLow
            : NativeMethods.MemoryPriorityLow;
        var priorityApplied = TrySetMemoryPriority(priority);
        var ecoQos = SetEcoQos(enabled: true);
        var emptied = ReturnWorkingSet();

        return new MemoryReclaimResult(
            level,
            before,
            Capture(),
            emptied,
            priorityApplied ? priority : _appliedMemoryPriority,
            ecoQos);
    }

    /// <summary>
    /// 恢复正常运行：内存优先级回到系统默认、关掉执行速度节流。工作集不需要"恢复"，它是按需读回的。
    /// Restores normal operation: the memory priority returns to the system default and execution-speed throttling is turned off. The working set
    /// needs no restore, because it is read back on demand.
    /// </summary>
    /// <returns>是否确实处于正常运行状态（未开启节流且优先级为默认）。/ Whether normal operation is in effect: throttling off and the default priority.</returns>
    public bool Restore()
    {
        var priorityRestored = TrySetMemoryPriority(NativeMethods.MemoryPriorityNormal);
        var throttlingCleared = SetEcoQos(enabled: false);
        return priorityRestored && throttlingCleared;
    }

    /// <summary>释放缓存的进程对象。/ Releases the cached process object.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _process.Dispose();
    }

    private static void ForceCompactingCollection()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
    }

    /// <summary>
    /// 交还工作集：先清空，再设下限/上限为 `-1`。
    /// Returns the working set: first emptying it, then asking for the smallest possible working set.
    ///
    /// 两项一起调用是有意的：`K32EmptyWorkingSet` 之后仍会有少量页被立刻摸回来（正在执行的栈、GC 自身），
    /// `SetProcessWorkingSetSize(-1, -1)` 把这部分也压下去，实测的对照项目用的就是这一对。
    /// Calling both is deliberate: a few pages are touched back immediately after `K32EmptyWorkingSet` — the running stack, the GC itself — and
    /// `SetProcessWorkingSetSize(-1, -1)` pushes those down too, which is the pair the reference project uses.
    /// </summary>
    /// <returns>至少一项成功时为 true。/ True when at least one of the two succeeded.</returns>
    private bool ReturnWorkingSet()
    {
        var emptied = TryEmptyWorkingSet();
        var sized = TrySetWorkingSetSize();
        return emptied || sized;
    }

    private bool TrySetWorkingSetSize()
    {
        if (NativeMethods.SetProcessWorkingSetSize(_process.Handle, new IntPtr(-1), new IntPtr(-1)))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        _log?.Warn("Prune", $"设置工作集上限失败(error {error})");
        return false;
    }

    private bool TryEmptyWorkingSet()
    {
        if (NativeMethods.K32EmptyWorkingSet(_process.Handle))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        _log?.Warn("Prune", $"交还工作集失败(error {error})");
        return false;
    }

    private bool TrySetMemoryPriority(uint priority)
    {
        var information = new NativeMethods.MemoryPriorityInformation { MemoryPriority = priority };
        if (NativeMethods.SetProcessInformation(
                _process.Handle,
                NativeMethods.ProcessMemoryPriority,
                ref information,
                (uint)Marshal.SizeOf<NativeMethods.MemoryPriorityInformation>()))
        {
            _appliedMemoryPriority = priority;
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        _log?.Warn("Prune", $"设置内存优先级失败(error {error})");
        return false;
    }

    private bool SetEcoQos(bool enabled)
    {
        var state = new NativeMethods.ProcessPowerThrottlingState
        {
            Version = NativeMethods.ProcessPowerThrottlingVersion,
            ControlMask = NativeMethods.ProcessPowerThrottlingExecutionSpeed,
            StateMask = enabled ? NativeMethods.ProcessPowerThrottlingExecutionSpeed : 0
        };

        if (NativeMethods.SetProcessInformation(
                _process.Handle,
                NativeMethods.ProcessPowerThrottling,
                ref state,
                (uint)Marshal.SizeOf<NativeMethods.ProcessPowerThrottlingState>()))
        {
            _ecoQosEnabled = enabled;
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        _log?.Warn("Prune", $"设置电源节流失败(error {error})");
        return _ecoQosEnabled;
    }
}
