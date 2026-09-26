// 移植自 Tester/KugouMem/Program.cs（指针链由 Cheat Engine 表定位，那里的注释保留了完整的定位过程）；
// 毫秒级进度地址来自后续的 Cheat Engine 复核。
// Adapted from Tester/KugouMem/Program.cs (the pointer chains were located with Cheat Engine tables; that project's comments keep the full
// story); the millisecond position address came from a later Cheat Engine pass.

using System.Diagnostics;
using AFMediaBar.Classes.Services.Win32;

namespace AFMediaBar.Classes.Services.Players;

/// <summary>
/// 从酷狗音乐进程内存读取播放进度与曲目时长。
/// Reads the playback position and track duration from the KuGou process memory.
///
/// 进度是 kgplayer.dll 内的静态 double（毫秒精度）；时长挂在一条入口相对 kugou.dll 模块基址固定的多级指针链上（int32 秒）。
/// 本类只负责"把地址与链走通并读数"，数值是否可信（是否真是一对进度/时长）由调用方判定。
/// The position is a static double inside kgplayer.dll (millisecond precision); the duration hangs off a multi-level pointer chain whose
/// entry is fixed relative to the kugou.dll module base (int32 seconds). This class only resolves the address and the chain and reads the
/// numbers; judging whether they are plausible (really a position/duration pair) is up to the caller.
/// </summary>
public sealed class KuGou : IDisposable
{
    private const string DurationModuleName = "kugou.dll";
    private const string PositionModuleName = "kgplayer.dll";

    /// <summary>
    /// 酷狗会拉起多个进程，只有部分映射了这两个模块；候选名与 MediaSourceProcessResolver 的映射保持一致。
    /// KuGou spawns several processes and only some of them map both modules; the candidate names follow MediaSourceProcessResolver's mapping.
    /// </summary>
    private static readonly string[] ProcessNames = ["KuGou", "KuGouMusic"];

    // 进度：kgplayer.dll 内的静态 double，毫秒精度。整秒读数配合"每次发布都盖时间戳"的外推会产生锯齿（歌词抽搐），
    // 双精度读数与网易云走同一条路，不再需要任何补偿。
    // duration: "kugou.dll"+0x023C9B68 -> +0x70 -> +0x30 -> +0x0 -> +0x24
    // CE 表的 <Offsets> 自底向上书写，套用时要按相反顺序。
    // Position: a static double inside kgplayer.dll, millisecond precision. The whole-second reading sawtoothed under the UI's
    // extrapolation (twitching lyrics); a double reads like NetEase's and needs no compensation.
    // The CE table lists <Offsets> bottom-up and they apply in reverse order.
    private const nint PositionModuleOffset = 0x3929D8;

    private const nint DurationBaseOffset = 0x023C9B68;
    private static readonly nint[] DurationOffsets = [0x70, 0x30, 0x0, 0x24];

    private readonly Process _process;
    private readonly ProcessMemory _memory;
    private readonly nint _positionModuleBase;
    private readonly nint _durationModuleBase;
    private bool _disposed;

    private KuGou(Process process, ProcessMemory memory, nint positionModuleBase, nint durationModuleBase)
    {
        _process = process;
        _memory = memory;
        _positionModuleBase = positionModuleBase;
        _durationModuleBase = durationModuleBase;
    }

    /// <summary>
    /// 附加到一个进度地址与时长链都能读通的酷狗进程；找不到就返回 null，调用方在下一次轮询时再试。
    /// Attaches to a KuGou process where the position address and the duration chain both resolve; returns null when none does, and the
    /// caller retries on the next poll.
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
    /// 酷狗的多个进程里只有引擎所在的那个能读通进度地址与时长链，因此附加前先完整读一遍：读不通的候选立即放弃，
    /// 而不是留到轮询里每次都读出不可信的值。
    /// Among KuGou's several processes only the one hosting the engine resolves the position address and the duration chain, so both are
    /// read once before attaching: a candidate that fails is dropped immediately instead of surviving into the poll loop and producing
    /// implausible values forever.
    /// </summary>
    private static KuGou? TryAttach(Process process)
    {
        nint positionModuleBase;
        nint durationModuleBase;
        try
        {
            var modules = process.Modules.OfType<ProcessModule>().ToArray();
            var position = modules.FirstOrDefault(
                candidate => PositionModuleName.Equals(candidate.ModuleName, StringComparison.OrdinalIgnoreCase));
            var duration = modules.FirstOrDefault(
                candidate => DurationModuleName.Equals(candidate.ModuleName, StringComparison.OrdinalIgnoreCase));
            if (position is null || duration is null)
            {
                return null;
            }

            positionModuleBase = position.BaseAddress;
            durationModuleBase = duration.BaseAddress;
        }
        catch
        {
            // 候选进程可能恰好退出，或模块枚举被拒绝：按"没有这个候选"处理。
            // The candidate may have just exited, or the module enumeration was denied: treat it as absent.
            return null;
        }

        var reader = new KuGou(process, new ProcessMemory(process.Id), positionModuleBase, durationModuleBase);
        if (reader.TryReadTimeline(out _, out _))
        {
            return reader;
        }

        reader.Dispose();
        return null;
    }

    /// <summary>
    /// 读取当前播放进度（毫秒精度的秒）与曲目时长（秒）。进度不是有限非负数、时长链任一环读不到、
    /// 或值已明显不是时间（负数）时返回 false；没在播放时也可能读到 0/0，那不算失败。
    /// Reads the current playback position (seconds, millisecond precision) and track duration (seconds). Returns false when the position is
    /// not a finite non-negative number, any link of the duration chain cannot be read, or a value is clearly not a time (negative);
    /// 0/0 while nothing plays is a success, not a failure.
    /// </summary>
    public bool TryReadTimeline(out double position, out double duration)
    {
        position = 0;
        duration = 0;

        if (_disposed)
        {
            return false;
        }

        if (!_memory.TryReadDouble(_positionModuleBase + PositionModuleOffset, out var positionValue) ||
            !double.IsFinite(positionValue) ||
            positionValue < 0)
        {
            return false;
        }

        if (!TryReadChain(DurationBaseOffset, DurationOffsets, out var durationSeconds) || durationSeconds < 0)
        {
            return false;
        }

        position = positionValue;
        duration = durationSeconds;
        return true;
    }

    private bool TryReadChain(nint baseOffset, nint[] offsets, out int value)
    {
        var address = _durationModuleBase + baseOffset;
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
