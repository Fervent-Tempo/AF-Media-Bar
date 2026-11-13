// 移植自 Tester/KugouMem/Program.cs（指针链由 Cheat Engine 表定位，那里的注释保留了完整的定位过程）；
// 毫秒级进度地址来自后续的 Cheat Engine 复核。
// Adapted from Tester/KugouMem/Program.cs (the pointer chains were located with Cheat Engine tables; that project's comments keep the full
// story); the millisecond position address came from a later Cheat Engine pass.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services.Media.Readers;
using AFMediaBar.Classes.Services.Media.Sources.Shared;
using AFMediaBar.Classes.Services.Win32;

namespace AFMediaBar.Classes.Services.Media.Sources.KuGou;

/// <summary>
/// 独占酷狗进程的只读句柄，读取时间轴、播放状态与实时窗口标题；Dispose 释放句柄。
///
/// 进度来自 kgplayer.dll 中的 double 秒值，时长来自 kugou.dll 的指针链，按模块版本转换为秒。
/// 调用方负责判断进度和时长是否匹配。
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

    // 进度和播放图的入口位于 kgplayer.dll，时长链的入口位于 kugou.dll。
    private static readonly nint[] _graphOffsets = [0xC0, 0xB8];

    private readonly Process _process;
    private readonly ProcessMemory _memory;
    private readonly nint _positionModuleBase;
    private readonly nint _durationModuleBase;
    private readonly KuGouMemoryLayout _layout;
    private readonly KuGouMetadataProbe _metadataProbe;
    private nint _quartzBase;
    private int _quartzSize;
    private readonly bool _supportsIndependentRead;
    private nint _metadataWindow;
    private bool _disposed;

    private KuGou(Process process, ProcessMemory memory, nint positionModuleBase, nint durationModuleBase,
        nint quartzBase, int quartzSize, bool supportsIndependentRead, KuGouMemoryLayout layout)
    {
        _process = process;
        _memory = memory;
        _positionModuleBase = positionModuleBase;
        _durationModuleBase = durationModuleBase;
        _layout = layout;
        _metadataProbe = new KuGouMetadataProbe(memory);
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
        ProcessMemory? memory = null;
        KuGou? reader = null;
        try
        {
            var modules = process.Modules.OfType<ProcessModule>().ToArray();
            var position = modules.FirstOrDefault(
                candidate => PositionModuleName.Equals(candidate.ModuleName, StringComparison.OrdinalIgnoreCase));
            var duration = modules.FirstOrDefault(
                candidate => DurationModuleName.Equals(candidate.ModuleName, StringComparison.OrdinalIgnoreCase));
            if (position is null || duration is null)
            {
                MediaReadDiagnostics.Report("kugou", "attach", "缺少 kgplayer.dll 或 kugou.dll", process.Id, process.ProcessName);
                return null;
            }

            memory = ProcessMemory.TryOpen(process.Id, out var openError);
            if (memory is null)
            {
                MediaReadDiagnostics.Report("kugou", "attach", $"OpenProcess 失败 error={openError}", process.Id, process.ProcessName);
                return null;
            }

            var positionFingerprint = PeBuildFingerprint.TryRead(memory, position.BaseAddress, out var playerBuild)
                ? playerBuild : default;
            var durationFingerprint = PeBuildFingerprint.TryRead(memory, duration.BaseAddress, out var durationBuild)
                ? durationBuild : default;
            var profile = KuGouBuildProfiles.Match(
                positionFingerprint, durationFingerprint, position.FileVersionInfo, duration.FileVersionInfo);
            var supportsIndependentRead = profile is not null;
            var layout = profile?.Layout ?? KuGouBuildProfiles.LegacyFallback;
            if (profile is null)
            {
                MediaReadDiagnostics.Report("kugou", "attach",
                    $"未登记构建 kgplayer={positionFingerprint}/{position.FileVersionInfo.FileVersion} kugou={durationFingerprint}/{duration.FileVersionInfo.FileVersion}",
                    process.Id, process.ProcessName);
            }

            var quartz = modules.FirstOrDefault(candidate => "quartz.dll".Equals(candidate.ModuleName, StringComparison.OrdinalIgnoreCase));
            reader = new KuGou(process, memory, position.BaseAddress, duration.BaseAddress,
                quartz?.BaseAddress ?? 0, quartz?.ModuleMemorySize ?? 0, supportsIndependentRead, layout);
            memory = null; // 句柄与 Process 已交给 reader。
            if (reader.TryReadTimelineForAttach(out _, out _))
            {
                MediaReadDiagnostics.Info("kugou",
                    $"已附加 pid={process.Id} profile={(supportsIndependentRead ? "verified" : "legacy-fallback")} " +
                    $"kgplayer={positionFingerprint} kugou={durationFingerprint}");
                return reader;
            }

            MediaReadDiagnostics.Report("kugou", "attach", "TryReadTimeline 失败", process.Id, process.ProcessName);
            reader.Dispose();
            reader = null;
            return null;
        }
        catch (Exception exception)
        {
            reader?.Dispose();
            memory?.Dispose();
            MediaReadDiagnostics.Report("kugou", "attach", "枚举模块或读取失败", process.Id, process.ProcessName, exception);
            return null;
        }
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
        if (_disposed || !TryReadPosition(out position))
            return false;
        return TryReadDuration(null, out duration);
    }

    /// <summary>附加阶段允许用元数据 JSON 补时长，避免时长链更新后整个进程被判为不可用。</summary>
    internal bool TryReadTimelineForAttach(out double position, out double duration)
    {
        position = 0;
        duration = 0;
        if (_disposed || !TryReadPosition(out position))
            return false;
        if (TryReadDuration(null, out duration))
            return true;
        return _metadataProbe.TryGetDuration(ReadCaption(), out duration) && duration > 0;
    }

    private bool TryReadPosition(out double position)
    {
        position = 0;
        if (!_memory.TryReadDouble(_positionModuleBase + _layout.PositionOffset, out var value) ||
            !double.IsFinite(value) || value < 0)
            return false;
        position = value;
        return true;
    }

    private bool TryReadDuration(string? caption, out double duration)
    {
        duration = 0;
        if (TryReadChain(_layout.DurationRootOffset, _layout.DurationOffsets, out var value) && value >= 0)
        {
            duration = (double)value / _layout.DurationUnitsPerSecond;
            return true;
        }

        return caption is not null && _metadataProbe.TryGetDuration(caption, out duration) && duration > 0;
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
        if (!TryReadPlaybackOnce(out var first) || first is null)
            return false;

        // 两次读取之间切歌、seek 或图状态变化时拒绝本轮，下一轮再发布。
        Thread.Sleep(20);
        if (_disposed || !TryReadPlaybackOnce(out var second) || second is null)
            return false;
        if (first.Identity != second.Identity ||
            Math.Abs(first.Duration - second.Duration) > 1 ||
            Math.Abs(first.Position - second.Position) > 2 ||
            first.IsPlaying != second.IsPlaying)
            return false;
        info = second;
        return true;
    }

    private bool TryReadPlaybackOnce(out KuGouPlaybackInfo? info)
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
            !TryReadPosition(out var position) || !TryReadDuration(caption, out var duration) ||
            duration <= 0 || duration > 86_400 || position > duration + 2)
            return false;

        // DirectShow 播放图的 FilterState：1 暂停，2 播放。先验证对象虚表属于 quartz.dll。
        var address = _positionModuleBase + _layout.GraphRootOffset;
        foreach (var offset in _graphOffsets)
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
