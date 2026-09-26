// 移植自 Tester/KugouMem/Program.cs（指针链由 Cheat Engine 表定位，那里的注释保留了完整的定位过程）。
// Adapted from Tester/KugouMem/Program.cs (the pointer chains were located with Cheat Engine tables; that project's comments keep the full story).

using System.Diagnostics;
using AFMediaBar.Classes.Services.Win32;

namespace AFMediaBar.Classes.Services.Players;

/// <summary>
/// 从酷狗音乐进程内存读取播放进度与曲目时长。
/// Reads the playback position and track duration from the KuGou process memory.
///
/// 两个值都是 int32 秒，且各自挂在一条入口相对 kugou.dll 模块基址固定的多级指针链上；本类只负责"把链走通并读数"，
/// 数值是否可信（是否真是一对进度/时长）由调用方判定。
/// Both values are int32 seconds, each hanging off a multi-level pointer chain whose entry is fixed relative to the kugou.dll module base.
/// This class only walks the chains and reads the numbers; judging whether they are plausible (really a position/duration pair) is up to the
/// caller.
/// </summary>
public sealed class KuGou : IDisposable
{
    private const string ModuleName = "kugou.dll";

    /// <summary>
    /// 酷狗会拉起多个进程，只有部分映射了 kugou.dll；候选名与 MediaSourceProcessResolver 的映射保持一致。
    /// KuGou spawns several processes and only some of them map kugou.dll; the candidate names follow MediaSourceProcessResolver's mapping.
    /// </summary>
    private static readonly string[] ProcessNames = ["KuGou", "KuGouMusic"];

    // position: "kugou.dll"+0x023C9BD8 -> +0x78 -> +0x0 -> +0x130 -> +0x48
    // duration: "kugou.dll"+0x023C9B68 -> +0x70 -> +0x30 -> +0x0 -> +0x24
    // CE 表的 <Offsets> 自底向上书写，套用时要按相反顺序；两条链入口不同、层数不同，不能合并。
    // The CE table lists <Offsets> bottom-up and they apply in reverse order; the two chains differ in entry and depth and cannot be merged.
    private const nint PositionBaseOffset = 0x023C9BD8;
    private static readonly nint[] PositionOffsets = [0x78, 0x0, 0x130, 0x48];

    private const nint DurationBaseOffset = 0x023C9B68;
    private static readonly nint[] DurationOffsets = [0x70, 0x30, 0x0, 0x24];

    private readonly Process _process;
    private readonly ProcessMemory _memory;
    private readonly nint _moduleBase;
    private bool _disposed;

    private KuGou(Process process, ProcessMemory memory, nint moduleBase)
    {
        _process = process;
        _memory = memory;
        _moduleBase = moduleBase;
    }

    /// <summary>
    /// 附加到一个两条指针链都能走通的酷狗进程；找不到就返回 null，调用方在下一次轮询时再试。
    /// Attaches to a KuGou process where both pointer chains resolve; returns null when none does, and the caller retries on the next poll.
    /// </summary>
    public static KuGou? Attach()
    {
        KuGou? attached = null;
        foreach (var name in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                if (attached is not null || (attached = TryAttach(process)) is null)
                {
                    process.Dispose();
                }
            }
        }

        return attached;
    }

    /// <summary>
    /// 酷狗的多个进程里只有引擎所在的那个能走通指针链，因此附加前先完整读一遍：链走不通的候选立即放弃，
    /// 而不是留到轮询里每次都读出不可信的值。
    /// Among KuGou's several processes only the one hosting the engine resolves the chains, so both are read once before attaching: a
    /// candidate whose chains fail is dropped immediately instead of surviving into the poll loop and producing implausible values forever.
    /// </summary>
    private static KuGou? TryAttach(Process process)
    {
        nint moduleBase;
        try
        {
            var module = process.Modules.OfType<ProcessModule>().FirstOrDefault(
                candidate => ModuleName.Equals(candidate.ModuleName, StringComparison.OrdinalIgnoreCase));
            if (module is null)
            {
                return null;
            }

            moduleBase = module.BaseAddress;
        }
        catch
        {
            // 候选进程可能恰好退出，或模块枚举被拒绝：按"没有这个候选"处理。
            // The candidate may have just exited, or the module enumeration was denied: treat it as absent.
            return null;
        }

        var reader = new KuGou(process, new ProcessMemory(process.Id), moduleBase);
        if (reader.TryReadTimeline(out _, out _))
        {
            return reader;
        }

        reader.Dispose();
        return null;
    }

    /// <summary>
    /// 读取当前播放进度与曲目时长（秒）。链上任一环读不到、或值已明显不是时间（负数）时返回 false；
    /// 没在播放时也可能读到 0/0，那不算失败。
    /// Reads the current playback position and track duration, in seconds. Returns false when any link of either chain cannot be read or a
    /// value is clearly not a time (negative); 0/0 while nothing plays is a success, not a failure.
    /// </summary>
    public bool TryReadTimeline(out double position, out double duration)
    {
        position = 0;
        duration = 0;

        if (_disposed ||
            !TryReadChain(PositionBaseOffset, PositionOffsets, out var positionSeconds) ||
            !TryReadChain(DurationBaseOffset, DurationOffsets, out var durationSeconds) ||
            positionSeconds < 0 ||
            durationSeconds < 0)
        {
            return false;
        }

        position = positionSeconds;
        duration = durationSeconds;
        return true;
    }

    private bool TryReadChain(nint baseOffset, nint[] offsets, out int value)
    {
        var address = _moduleBase + baseOffset;
        foreach (var offset in offsets)
        {
            if (!_memory.TryReadInt64(address, out var pointer))
            {
                value = 0;
                return false;
            }

            address = (nint)pointer + offset;
        }

        return _memory.TryReadInt32(address, out value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _memory.Dispose();
        _process.Dispose();
    }
}
