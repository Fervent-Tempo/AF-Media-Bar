namespace TaskbarPlayer.Models;

internal readonly record struct MetricSettings(
    bool Enabled,
    bool ShowSystemMemory,
    bool ShowSystemCpu,
    bool ShowSystemGpu,
    bool ShowProcessMemory,
    bool LowGpuMode,
    bool AudioMonitorEnabled)
{
    internal static MetricSettings Default { get; } = new(
        true,
        true,
        false,
        false,
        false,
        false,
        false);

    internal int SelectedCount => Enabled
        ? (ShowSystemMemory ? 1 : 0) +
            (ShowSystemCpu ? 1 : 0) +
            (ShowSystemGpu ? 1 : 0) +
            (ShowProcessMemory ? 1 : 0)
        : 0;
}
