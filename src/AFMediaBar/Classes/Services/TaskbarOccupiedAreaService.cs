using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Layout;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 管理任务栏占用区域探测的缓存、代际和结果发布。
/// Manages caching, generations, and result publication for taskbar occupancy probes.
///
/// 平台探测由 <see cref="ITaskbarOccupiedAreaProbe"/> 在后台执行；UI 线程只读取不可变缓存或使用保守区间。
/// Platform probing runs in the background through <see cref="ITaskbarOccupiedAreaProbe"/>; the UI thread only reads immutable cache snapshots or uses conservative ranges.
/// </summary>
public sealed class TaskbarOccupiedAreaService
{
    private static readonly TimeSpan ProbeCacheDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ProbeResultTimeout = TimeSpan.FromMilliseconds(1500);
    private readonly ITaskbarOccupiedAreaProbe _probe;
    private readonly object _cacheGate = new();
    private IntPtr _cachedTaskbarHandle;
    private RECT _cachedTaskbarRect;
    private LayoutOrientation _cachedOrientation;
    private double _cachedDpiScale;
    private int _cachedEdgePaddingPixels;
    private DateTime _cachedAtUtc;
    private IReadOnlyList<TaskbarPrimaryRange> _cachedRanges = [];
    private bool _probeInProgress;
    private DateTime _probeStartedAtUtc;
    private long _cacheGeneration;

    /// <summary>
    /// 使用指定后台平台探测器创建任务栏占用区域服务。
    /// Creates the taskbar occupancy service with the specified background platform probe.
    /// </summary>
    /// <param name="probe">后台任务栏平台探测器 / Background taskbar platform probe.</param>
    public TaskbarOccupiedAreaService(ITaskbarOccupiedAreaProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    /// <summary>
    /// 调度任务栏占用区域探测，并返回带安全间距的已缓存可用主轴区间。
    /// Schedules a taskbar occupancy probe and returns cached free primary-axis ranges with safety gaps.
    /// </summary>
    /// <param name="taskbarHandle">任务栏窗口句柄 / Taskbar window handle.</param>
    /// <param name="taskbarRect">任务栏屏幕矩形 / Taskbar screen rectangle.</param>
    /// <param name="orientation">布局主轴方向 / Layout primary-axis orientation.</param>
    /// <param name="dpiScale">任务栏 DPI 缩放 / Taskbar DPI scale.</param>
    /// <param name="edgePaddingPixels">媒体栏边缘留白 / Media-bar edge padding.</param>
    /// <returns>按主轴顺序排列的已缓存安全区间；探测未完成时返回空集合。 / Cached safe ranges ordered along the primary axis; empty while a probe is pending.</returns>
    public IReadOnlyList<TaskbarPrimaryRange> GetSafePrimaryRanges(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels)
    {
        IReadOnlyList<TaskbarPrimaryRange> availableCache;
        var startProbe = false;
        var probeGeneration = 0L;
        var probeStartedAtUtc = default(DateTime);
        lock (_cacheGate)
        {
            var now = DateTime.UtcNow;
            var cacheMatches = MatchesCacheKey(
                taskbarHandle,
                taskbarRect,
                orientation,
                dpiScale,
                edgePaddingPixels);
            if (cacheMatches && now - _cachedAtUtc < ProbeCacheDuration)
                return _cachedRanges;

            var activeProbeTimedOut = _probeInProgress && now - _probeStartedAtUtc > ProbeResultTimeout;
            availableCache = cacheMatches && !activeProbeTimedOut ? _cachedRanges : [];
            if (!_probeInProgress)
            {
                _probeInProgress = true;
                _probeStartedAtUtc = now;
                probeGeneration = _cacheGeneration;
                probeStartedAtUtc = now;
                startProbe = true;
            }
        }

        if (startProbe)
        {
            StartProbe(
                taskbarHandle,
                taskbarRect,
                orientation,
                dpiScale,
                edgePaddingPixels,
                probeGeneration,
                probeStartedAtUtc);
        }

        // UI Automation 可能等待 Explorer；持有 Explorer 子 HWND 的 WPF 线程绝不能同步等待。
        // UI Automation may wait on Explorer. Never block the WPF thread that owns an
        // Explorer child HWND; use the previous matching snapshot or the caller's fallback.
        return availableCache;
    }

    /// <summary>
    /// 使当前占用区缓存失效；正在运行的旧探测完成后不会发布结果。
    /// Invalidates the current occupancy cache; an in-flight older probe will not publish its result.
    /// </summary>
    public void InvalidateCache()
    {
        lock (_cacheGate)
        {
            _cacheGeneration++;
            _cachedTaskbarHandle = IntPtr.Zero;
            _cachedTaskbarRect = default;
            _cachedDpiScale = 0;
            _cachedAtUtc = default;
            _cachedRanges = [];
        }
    }

    private void StartProbe(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels,
        long probeGeneration,
        DateTime probeStartedAtUtc)
    {
        try
        {
            _probe.Start(
                taskbarHandle,
                taskbarRect,
                orientation,
                dpiScale,
                edgePaddingPixels,
                ranges => CompleteProbe(
                    taskbarHandle,
                    taskbarRect,
                    orientation,
                    dpiScale,
                    edgePaddingPixels,
                    probeGeneration,
                    probeStartedAtUtc,
                    ranges),
                FailProbe);
        }
        catch (Exception)
        {
            FailProbe();
        }
    }

    private void CompleteProbe(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels,
        long probeGeneration,
        DateTime probeStartedAtUtc,
        IReadOnlyList<TaskbarPrimaryRange> ranges)
    {
        lock (_cacheGate)
        {
            var completedAtUtc = DateTime.UtcNow;
            if (probeGeneration == _cacheGeneration &&
                completedAtUtc - probeStartedAtUtc <= ProbeResultTimeout)
            {
                _cachedTaskbarHandle = taskbarHandle;
                _cachedTaskbarRect = taskbarRect;
                _cachedOrientation = orientation;
                _cachedDpiScale = dpiScale;
                _cachedEdgePaddingPixels = edgePaddingPixels;
                _cachedAtUtc = completedAtUtc;
                _cachedRanges = ranges.ToArray();
            }

            _probeInProgress = false;
        }
    }

    private void FailProbe()
    {
        lock (_cacheGate)
        {
            _probeInProgress = false;
        }
    }

    private bool MatchesCacheKey(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels)
    {
        return taskbarHandle == _cachedTaskbarHandle &&
               taskbarRect.Equals(_cachedTaskbarRect) &&
               orientation == _cachedOrientation &&
               Math.Abs(dpiScale - _cachedDpiScale) < 0.01 &&
               edgePaddingPixels == _cachedEdgePaddingPixels;
    }
}
