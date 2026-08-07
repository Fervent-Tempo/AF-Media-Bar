namespace TaskbarPlayer.Models;

internal readonly record struct MetricSettings(
    bool ShowSystemMemory,
    bool ShowSystemCpu,
    bool ShowProcessMemory)
{
    internal static MetricSettings Default { get; } = new(true, false, false);

    internal int SelectedCount =>
        (ShowSystemMemory ? 1 : 0) +
        (ShowSystemCpu ? 1 : 0) +
        (ShowProcessMemory ? 1 : 0);
}
