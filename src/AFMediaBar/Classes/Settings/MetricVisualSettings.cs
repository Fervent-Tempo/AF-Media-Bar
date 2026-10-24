// 两种性能表面各持有一份不可变样式；不改变既有指标选择、顺序和采样语义。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Settings;

/// <summary>性能指标的绘制方式；编号用于持久化。</summary>
public enum MetricVisualStyle
{
    Numeric = 0,
    Bar = 1,
    Ring = 2,
    Line = 3,
    Area = 4
}

/// <summary>每个指标独立选择样式，网络上下行共用一个选项。</summary>
public readonly record struct MetricVisualSettings
{
    public MetricVisualStyle SystemMemory { get; init; }
    public MetricVisualStyle SystemCpu { get; init; }
    public MetricVisualStyle SystemGpu { get; init; }
    public MetricVisualStyle ProcessMemory { get; init; }
    public MetricVisualStyle SystemNetwork { get; init; }

    public static MetricVisualSettings PanelDefault => new()
    {
        SystemMemory = MetricVisualStyle.Area,
        SystemCpu = MetricVisualStyle.Area,
        SystemGpu = MetricVisualStyle.Area
    };

    public MetricVisualStyle Get(MetricKind metric) => metric switch
    {
        MetricKind.SystemMemory => SystemMemory,
        MetricKind.SystemCpu => SystemCpu,
        MetricKind.SystemGpu => SystemGpu,
        MetricKind.ProcessMemory => ProcessMemory,
        _ => SystemNetwork
    };

    public MetricVisualSettings With(MetricKind metric, MetricVisualStyle style) => metric switch
    {
        MetricKind.SystemMemory => this with { SystemMemory = style },
        MetricKind.SystemCpu => this with { SystemCpu = style },
        MetricKind.SystemGpu => this with { SystemGpu = style },
        MetricKind.ProcessMemory => this with { ProcessMemory = style },
        _ => this with { SystemNetwork = style }
    };

    public MetricVisualSettings Normalize(MetricVisualSettings defaults = default)
    {
        var result = this;
        foreach (var metric in Enum.GetValues<MetricKind>())
            if (!Enum.IsDefined(result.Get(metric))) result = result.With(metric, defaults.Get(metric));
        return result;
    }
}
