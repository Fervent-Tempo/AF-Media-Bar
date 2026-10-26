#if DEBUG
namespace AFMediaBar.Classes.Services.Diagnostics;

/// <summary>对同一物理适配器引擎的所有进程求和。</summary>
internal sealed record GpuEngineTotal(string Adapter, string Engine, double Percent);

/// <summary>同一引擎的 AF 及 WebView2 负载；身份无法确认时保留为空。</summary>
internal sealed record OwnedGpuEngineUsage(string Adapter, string Engine, double? AfPercent, double? WebViewPercent);

/// <summary>采样时刻可确认的 GPU 进程身份；无法确认时仅保留 PID。</summary>
internal sealed record GpuProcessIdentityUsage(int Pid, string? Name, DateTimeOffset? StartedAt,
    string Adapter, string Engine, double Percent);

/// <summary>保留 PDH 的适配器与引擎身份；不同引擎的百分比不能相加。</summary>
internal sealed record GpuProcessEngineUsage(int Pid, string Adapter, string Engine, double Percent)
{
    internal static bool CanAssociateProcess(DateTimeOffset startedAt, DateTimeOffset capturedAt) => startedAt <= capturedAt;

    internal static OwnedGpuEngineUsage[] SummarizeOwned(GpuProcessEngineUsage[] entries, DebugProcessResources[] resources,
        DateTimeOffset capturedAt, int mainPid)
    {
        var known = resources.Where(item => CanAssociateProcess(item.StartedAt, capturedAt)).Select(item => item.Pid).ToHashSet();
        var reused = resources.Where(item => !CanAssociateProcess(item.StartedAt, capturedAt)).Select(item => item.Pid).ToHashSet();
        return entries.GroupBy(item => (item.Adapter, item.Engine))
            .Where(group => group.Any(item => item.Pid == mainPid || known.Contains(item.Pid) || reused.Contains(item.Pid)))
            .Select(group => new OwnedGpuEngineUsage(group.Key.Adapter, group.Key.Engine,
            known.Contains(mainPid) ? group.Where(item => item.Pid == mainPid).Sum(item => item.Percent) : null,
            group.Any(item => reused.Contains(item.Pid)) ? null : group.Where(item => item.Pid != mainPid && known.Contains(item.Pid)).Sum(item => item.Percent)))
            .Where(item => item.AfPercent != 0 || item.WebViewPercent != 0).ToArray();
    }

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
