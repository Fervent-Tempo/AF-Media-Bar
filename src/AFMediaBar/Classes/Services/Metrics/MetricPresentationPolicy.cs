using AFMediaBar.Classes.Models;
using System.Globalization;

namespace AFMediaBar.Classes.Services;

/// <summary>性能采样租约需要执行的状态转换。/ State transition required for a metric-sampling lease.</summary>
public enum MetricSubscriptionTransition
{
    /// <summary>保留当前状态。/ Keeps the current state.</summary>
    None,

    /// <summary>创建缺失的租约。/ Creates a missing lease.</summary>
    Subscribe,

    /// <summary>按新设置替换现有租约。/ Replaces an existing lease for changed settings.</summary>
    Renew,

    /// <summary>释放不再需要的租约。/ Releases a lease that is no longer needed.</summary>
    Unsubscribe
}

/// <summary>性能指标轮换和文本格式化纯策略。 / Pure policy for compact metric cycling and formatting.</summary>
public static class MetricPresentationPolicy
{
    public static int Advance(int currentIndex, int publishedSampleCount, int metricCount) =>
        metricCount <= 1 || publishedSampleCount <= 0 || publishedSampleCount % 3 != 0
            ? Math.Clamp(currentIndex, 0, Math.Max(0, metricCount - 1))
            : (Math.Clamp(currentIndex, 0, metricCount - 1) + 1) % metricCount;

    public static string Format(MetricKind metric, SystemMetricsSnapshot snapshot) => metric switch
    {
        MetricKind.SystemMemory => snapshot.SystemMemoryPercent is int memory ? $"MEM {memory}%" : "MEM —",
        MetricKind.SystemCpu => snapshot.SystemCpuPercent is int cpu ? $"CPU {cpu}%" : "CPU —",
        MetricKind.SystemGpu => snapshot.SystemGpuPercent is int gpu ? $"GPU {gpu}%" : "GPU —",
        MetricKind.ProcessMemory => snapshot.ProcessMemoryMegabytes is long process ? $"APP {process} MB" : "APP —",
        MetricKind.SystemNetwork => $"↑ {FormatRate(snapshot.NetworkUploadBytesPerSecond)}\n↓ {FormatRate(snapshot.NetworkDownloadBytesPerSecond)}",
        _ => "—"
    };

    public static string FormatRate(double? bytesPerSecond)
    {
        if (bytesPerSecond is not double value || !double.IsFinite(value) || value < 0)
            return "—";
        string[] units = ["B/s", "KiB/s", "MiB/s", "GiB/s"];
        var unit = 0;
        while (unit < units.Length - 1 && value >= 1024)
        {
            value /= 1024;
            unit++;
        }
        // 四舍五入跨过边界时也提升单位，避免显示 1024.0 KiB/s。
        if (unit < units.Length - 1 && Math.Round(value, unit == 0 ? 0 : 1) >= 1024)
        {
            value /= 1024;
            unit++;
        }
        return $"{value.ToString(unit == 0 ? "0" : "0.0", CultureInfo.CurrentCulture)} {units[unit]}";
    }

    /// <summary>
    /// 根据控件最终显隐、当前租约和设置是否变化决定租约转换；普通媒体快照不得续租，避免反复重置刷新节奏。
    /// Resolves a lease transition from final control visibility, current ownership, and whether settings changed; ordinary media snapshots must not
    /// renew a live lease, which would continually reset its cadence.
    /// </summary>
    public static MetricSubscriptionTransition ResolveSubscriptionTransition(
        bool shouldSubscribe,
        bool hasSubscription,
        bool forceRenew) => (shouldSubscribe, hasSubscription, forceRenew) switch
        {
            (false, true, _) => MetricSubscriptionTransition.Unsubscribe,
            (false, false, _) => MetricSubscriptionTransition.None,
            (true, false, _) => MetricSubscriptionTransition.Subscribe,
            (true, true, true) => MetricSubscriptionTransition.Renew,
            _ => MetricSubscriptionTransition.None
        };
}
