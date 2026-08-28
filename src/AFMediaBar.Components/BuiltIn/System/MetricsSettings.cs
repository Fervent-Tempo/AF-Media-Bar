using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.System;

public enum MetricKind { SystemMemory = 0, SystemCpu = 1, SystemGpu = 2, ProcessMemory = 3 }

public sealed record MetricsSettings(
    MetricKind Metric = MetricKind.SystemMemory,
    bool OpenTaskManagerOnClick = false,
    int RefreshIntervalMilliseconds = 2500,
    IReadOnlyList<MetricKind>? CycleMetrics = null) : IComponentSettings
{
    public string TypeId => ComponentTypeIds.Metrics;
    public IReadOnlyList<MetricKind> EffectiveCycleMetrics => CycleMetrics is { Count: > 0 } ? CycleMetrics : [Metric];
}
