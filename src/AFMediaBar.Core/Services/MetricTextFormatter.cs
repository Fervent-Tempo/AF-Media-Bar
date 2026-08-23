using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 把指标快照按用户选择顺序格式化为紧凑文本，不负责采样或 WPF 呈现。
/// Formats metric snapshots in user-selected order without sampling data or rendering WPF controls.
/// </summary>
public static class MetricTextFormatter
{
    public static string Format(SystemMetricsSnapshot sample, MetricKind metric)
    {
        return metric switch
        {
            MetricKind.SystemMemory => $"MEM {sample.SystemMemoryPercent}%",
            MetricKind.SystemCpu => $"CPU {(sample.SystemCpuPercent is int cpu ? $"{cpu}%" : "--%")}",
            MetricKind.SystemGpu => $"GPU {(sample.SystemGpuPercent is int gpu ? $"{gpu}%" : "--%")}",
            MetricKind.ProcessMemory => sample.ProcessMemoryMegabytes < 1000
                ? $"APP {sample.ProcessMemoryMegabytes}M"
                : $"APP {sample.ProcessMemoryMegabytes / 1024d:0.0}G",
            _ => string.Empty
        };
    }

    public static string Format(
        SystemMetricsSnapshot sample,
        MetricSettings settings,
        int selectedIndex)
    {
        if (settings.ShowSystemMemory && selectedIndex-- == 0)
        {
            return $"MEM {sample.SystemMemoryPercent}%";
        }

        if (settings.ShowSystemCpu && selectedIndex-- == 0)
        {
            return $"CPU {(sample.SystemCpuPercent is int cpu ? $"{cpu}%" : "--%")}";
        }

        if (settings.ShowSystemGpu && selectedIndex-- == 0)
        {
            return $"GPU {(sample.SystemGpuPercent is int gpu ? $"{gpu}%" : "--%")}";
        }

        if (!settings.ShowProcessMemory)
        {
            return string.Empty;
        }

        var appMemory = sample.ProcessMemoryMegabytes < 1000
            ? $"{sample.ProcessMemoryMegabytes}M"
            : $"{sample.ProcessMemoryMegabytes / 1024d:0.0}G";
        return $"APP {appMemory}";
    }
}
