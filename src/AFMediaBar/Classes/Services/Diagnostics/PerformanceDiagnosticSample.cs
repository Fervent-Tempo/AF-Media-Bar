#if DEBUG
namespace AFMediaBar.Classes.Services.Diagnostics;

/// <summary>对同一物理适配器引擎的所有进程求和。</summary>
internal sealed record GpuEngineTotal(string Adapter, string Engine, double Percent);

/// <summary>保留 PDH 的适配器与引擎身份；不同引擎的百分比不能相加。</summary>
internal sealed record GpuProcessEngineUsage(int Pid, string Adapter, string Engine, double Percent)
{
    internal static bool TryParse(string? instance, double percent, out GpuProcessEngineUsage? usage)
    {
        usage = null;
        if (instance is null || !double.IsFinite(percent) || !instance.StartsWith("pid_", StringComparison.OrdinalIgnoreCase)) return false;
        var pidEnd = instance.IndexOf('_', 4);
        var luid = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        var engine = instance.IndexOf("_eng_", StringComparison.OrdinalIgnoreCase);
        if (pidEnd < 0 || luid < 0 || engine <= luid || !int.TryParse(instance.AsSpan(4, pidEnd - 4), out var pid)) return false;
        usage = new(pid, instance[luid..engine], instance[(engine + 1)..], Math.Max(0, percent));
        return true;
    }
}

/// <summary>固定事件字段，不包含歌词、歌曲或用户路径。</summary>
internal sealed record PerformanceDiagnosticEvent(string Category, string Name, DateTimeOffset Time,
    long Timestamp, double? DurationMs = null, int? BrowserProcessId = null, string? Instance = null, int? Generation = null);
#endif
