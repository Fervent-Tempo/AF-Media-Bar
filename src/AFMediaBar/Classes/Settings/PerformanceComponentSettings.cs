// 按功能归组的设置契约；保持既有类型全名、枚举值及默认语义。
using AFMediaBar.Classes.Models;
using System.IO;

namespace AFMediaBar.Classes.Settings;


/// <summary>性能组件的指标呈现方式，值用于设置持久化。</summary>
public enum PerformanceDisplayMode
{
    Cycle = 0,
    Parallel = 1
}

/// <summary>任务栏性能组件设置。 / Taskbar performance component settings.</summary>
public readonly record struct PerformanceComponentSettings(
    IReadOnlyList<MetricKind>? Metrics,
    int RefreshIntervalMilliseconds,
    bool OpenTaskManagerOnClick)
{
    public PerformanceDisplayMode DisplayMode { get; init; } = PerformanceDisplayMode.Cycle;
    public IReadOnlyList<MetricKind>? MetricOrder { get; init; }

    /// <summary>在保留既有指标集合语义的同时解析显示顺序。</summary>
    public IReadOnlyList<MetricKind> GetOrderedMetrics()
    {
        var selected = Metrics ?? [MetricKind.SystemMemory];
        var order = MetricOrder ?? [];
        return order.Where(selected.Contains).Distinct()
            .Concat(selected.Where(metric => !order.Contains(metric))).ToArray();
    }

    /// <summary>
    /// 采样间隔下限（毫秒）。旧范围（250–60000 毫秒）里真正可用的部分只占一小段，滑杆其余行程全是没人会选的取值，
    /// 因此收敛到 0.5–5 秒。
    /// Lower sampling-interval bound in milliseconds. In the previous 250–60000 ms range only a small slice was usable and
    /// the rest of the slider travel held values nobody would pick, so the range is narrowed to 0.5–5 seconds.
    /// </summary>
    public const int MinimumRefreshIntervalMilliseconds = 500;

    /// <summary>采样间隔上限（5 秒）；再慢就只剩一个偶尔跳动的数字。 / Upper sampling-interval bound (5 seconds); anything slower is a number that rarely moves.</summary>
    public const int MaximumRefreshIntervalMilliseconds = 5000;

    /// <summary>采样间隔的默认值（2.5 秒）。 / Default sampling interval (2.5 seconds).</summary>
    public const int DefaultRefreshIntervalMilliseconds = 2500;

    /// <summary>
    /// 采样间隔的步长（0.5 秒）。界面的滑杆按该步长吸附，因此写入设置的值也必须落到步长网格上，
    /// 否则界面会显示一个滑杆位置无法表达的读数。
    /// Step of the sampling interval (0.5 seconds). The slider snaps to this step, so the stored value must land on the same
    /// grid; otherwise the interface shows a reading its own slider position cannot express.
    /// </summary>
    public const int RefreshIntervalStepMilliseconds = 500;

    /// <summary>
    /// 性能组件的默认设置。点击打开任务管理器默认为开启：该开关此前一直存在，但点击被任务栏拖动逻辑吞掉，
    /// 因此没有任何用户能在它关闭的状态下做出有效选择。 / Defaults for the performance component. Opening Task Manager
    /// on click is on by default: the switch existed before but the click was swallowed by the taskbar drag logic, so no
    /// user could have made a meaningful choice while it was off.
    /// </summary>
    public static PerformanceComponentSettings Default { get; } = new([MetricKind.SystemMemory], DefaultRefreshIntervalMilliseconds, true);

    public PerformanceComponentSettings Normalize()
    {
        var metrics = (Metrics ?? [])
            .Where(Enum.IsDefined)
            .Distinct()
            .OrderBy(metric => metric)
            .ToArray();
        if (metrics.Length == 0)
            metrics = [MetricKind.SystemMemory];
        var order = MetricOrder;
        return new PerformanceComponentSettings(
            metrics,
            SnapRefreshIntervalMilliseconds(RefreshIntervalMilliseconds),
            OpenTaskManagerOnClick)
        {
            DisplayMode = Enum.IsDefined(DisplayMode) ? DisplayMode : PerformanceDisplayMode.Cycle,
            MetricOrder = order is null ? null : order.Where(metrics.Contains).Distinct()
                .Concat(metrics.Where(metric => !order.Contains(metric))).ToArray()
        };
    }

    /// <summary>把采样间隔吸附到界面步长网格并夹取到安全区间。 / Snaps the sampling interval onto the interface step grid and clamps it to the safe range.</summary>
    /// <param name="milliseconds">待换算的采样间隔（毫秒）。/ Sampling interval to normalize, in milliseconds.</param>
    public static int SnapRefreshIntervalMilliseconds(int milliseconds)
    {
        var snapped = (int)Math.Round(milliseconds / (double)RefreshIntervalStepMilliseconds, MidpointRounding.AwayFromZero)
                      * RefreshIntervalStepMilliseconds;
        return Math.Clamp(snapped, MinimumRefreshIntervalMilliseconds, MaximumRefreshIntervalMilliseconds);
    }
}
