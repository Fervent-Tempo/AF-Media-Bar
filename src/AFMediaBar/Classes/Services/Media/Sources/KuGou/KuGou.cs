// 移植自 Tester/KugouMem/Program.cs（指针链由 Cheat Engine 表定位，那里的注释保留了完整的定位过程）；
// 毫秒级进度地址来自后续的 Cheat Engine 复核。
// Adapted from Tester/KugouMem/Program.cs (the pointer chains were located with Cheat Engine tables; that project's comments keep the full
// story); the millisecond position address came from a later Cheat Engine pass.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services.Win32;

namespace AFMediaBar.Classes.Services.Media.Sources.KuGou;

/// <summary>
/// 独占酷狗进程的只读句柄，读取时间轴、播放状态与实时窗口标题；Dispose 释放句柄。
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

    private const nint LegacyDurationBaseOffset = 0x023C9B68;
    private static readonly nint[] DurationOffsets = [0x70, 0x30, 0x0, 0x24];
    private const nint GraphBaseOffset = 0x3936A8;
    private static readonly nint[] GraphOffsets = [0xC0, 0xB8];

    private readonly Process _process;
    private readonly ProcessMemory _memory;
    private readonly nint _positionModuleBase;
    private readonly nint _durationModuleBase;
    private readonly nint _durationBaseOffset;
    private nint _quartzBase;
    private int _quartzSize;
    private readonly bool _supportsIndependentRead;
    private nint _metadataWindow;
    private bool _disposed;

    private KuGou(Process process, ProcessMemory memory, nint positionModuleBase, nint durationModuleBase,
        nint quartzBase, int quartzSize, bool supportsIndependentRead, nint durationBaseOffset)
    {
        _process = process;
        _memory = memory;
        _positionModuleBase = positionModuleBase;
        _durationModuleBase = durationModuleBase;
        _durationBaseOffset = durationBaseOffset;
        _quartzBase = quartzBase;
        _quartzSize = quartzSize;
        _supportsIndependentRead = supportsIndependentRead;
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
        nint quartzBase;
        int quartzSize;
        bool supportsIndependentRead;
        nint durationBaseOffset;
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
            var quartz = modules.FirstOrDefault(candidate => "quartz.dll".Equals(candidate.ModuleName, StringComparison.OrdinalIgnoreCase));
            quartzBase = quartz?.BaseAddress ?? 0;
            quartzSize = quartz?.ModuleMemorySize ?? 0;
            var engineVersion = position.FileVersionInfo;
            var clientVersion = duration.FileVersionInfo;
            // 私有偏移按已验证版本选择；未知版本继续尝试原有 SMTC 时间轴增强。
            supportsIndependentRead = engineVersion.FileMajorPart == 1 && engineVersion.FileMinorPart == 2 &&
                engineVersion.FileBuildPart == 0 && engineVersion.FilePrivatePart == 519 &&
                clientVersion.FileMajorPart == 20 && clientVersion.FileMinorPart == 1 && clientVersion.FileBuildPart == 51 &&
                clientVersion.FilePrivatePart is 27972 or 27974;
            durationBaseOffset = supportsIndependentRead && clientVersion.FilePrivatePart == 27974
                ? 0x023C7B68 : LegacyDurationBaseOffset;
        }
        catch
        {
            // 候选进程可能恰好退出，或模块枚举被拒绝：按"没有这个候选"处理。
            // The candidate may have just exited, or the module enumeration was denied: treat it as absent.
            return null;
        }

        var reader = new KuGou(process, new ProcessMemory(process.Id), positionModuleBase, durationModuleBase,
            quartzBase, quartzSize, supportsIndependentRead, durationBaseOffset);
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

        if (!TryReadChain(_durationBaseOffset, DurationOffsets, out var durationSeconds) || durationSeconds < 0)
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

    internal bool TryReadPlayback(out KuGouPlaybackInfo? info)
    {
        info = null;
        if (_disposed || !_supportsIndependentRead || _process.HasExited) return false;
        if (_quartzBase == 0)
        {
            // 启动时可能尚未加载播放图模块，播放开始后重新枚举，避免永久停留在 SMTC 回退。
            _process.Refresh();
            var quartz = _process.Modules.OfType<ProcessModule>().FirstOrDefault(
                candidate => "quartz.dll".Equals(candidate.ModuleName, StringComparison.OrdinalIgnoreCase));
            _quartzBase = quartz?.BaseAddress ?? 0;
            _quartzSize = quartz?.ModuleMemorySize ?? 0;
            if (_quartzBase == 0) return false;
        }
        var caption = ReadCaption();
        if (!KuGouTrackMetadata.TryParse(caption, out var title, out var artist) ||
            !TryReadTimeline(out var position, out var duration) || duration <= 0 || duration > 86_400 || position > duration + 2)
            return false;

        // DirectShow 播放图的 FilterState：1 暂停，2 播放。先验证对象虚表属于 quartz.dll。
        var address = _positionModuleBase + GraphBaseOffset;
        foreach (var offset in GraphOffsets)
        {
            if (!_memory.TryReadInt64(address, out var pointer) || pointer == 0) return false;
            address = (nint)pointer + offset;
        }
        if (!_memory.TryReadInt64(address, out var filter) || filter == 0 ||
            !_memory.TryReadInt64((nint)filter, out var table) || table < _quartzBase || table >= _quartzBase + _quartzSize ||
            !_memory.TryReadInt32((nint)filter + 0x28, out var state) || state is not (1 or 2))
            return false;

        // 前后核对窗口标题，丢弃读取过程中发生的切歌。
        if (!string.Equals(caption, ReadCaption(), StringComparison.Ordinal)) return false;
        info = new(title, artist, position, duration, state == 2);
        return true;
    }

    private string ReadCaption()
    {
        string Read(nint window)
        {
            NativeMethods.GetWindowThreadProcessId(window, out int pid);
            // 桌面歌词窗口也带有“ - 酷狗音乐”后缀，排除工具窗口和有所有者的弹窗。
            if (pid != _process.Id || (NativeMethods.GetWindowLong(window, NativeMethods.GWL_EXSTYLE) & 0x80) != 0 ||
                NativeMethods.GetWindow(window, 4) != 0) return string.Empty;
            var text = new StringBuilder(1024);
            _ = GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }

        var caption = _metadataWindow == 0 ? string.Empty : Read(_metadataWindow);
        if (KuGouTrackMetadata.TryParse(caption, out _, out _)) return caption;
        // EnumWindows 包含隐藏到托盘的顶层窗口，不能依赖只寻找可见窗口的 Process.MainWindowTitle。
        NativeMethods.EnumWindows((window, parameter) =>
        {
            var candidate = Read(window);
            if (!KuGouTrackMetadata.TryParse(candidate, out _, out _)) return true;
            _metadataWindow = window;
            caption = candidate;
            return false;
        }, 0);
        return caption;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int count);

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
