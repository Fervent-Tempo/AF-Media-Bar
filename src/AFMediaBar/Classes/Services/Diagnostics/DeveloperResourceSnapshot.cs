// 按需在后台采样 AF 主进程资源；只保留计数值，采样结束即释放进程句柄，不启动计时器。
using System.Diagnostics;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Diagnostics;

internal sealed record DeveloperResourceSnapshot(DateTimeOffset CapturedAt, long Timestamp, long WorkingSetBytes,
    long PrivateBytes, long ManagedBytes, int Handles, int Threads, int Gen0, int Gen1, int Gen2)
{
    internal static Task<DeveloperResourceSnapshot> CaptureAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var process = Process.GetCurrentProcess();
        return new DeveloperResourceSnapshot(DateTimeOffset.Now, Stopwatch.GetTimestamp(), process.WorkingSet64,
            process.PrivateMemorySize64, GC.GetTotalMemory(false), process.HandleCount, process.Threads.Count,
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    }, token);

    internal string Format() => Translations.Format("Developer.Resources.Snapshot", CapturedAt,
        WorkingSetBytes / 1048576d, PrivateBytes / 1048576d, ManagedBytes / 1048576d,
        Handles, Threads, Gen0, Gen1, Gen2);

    internal string FormatComparison(DeveloperResourceSnapshot baseline) => Format() + Environment.NewLine +
        Translations.Format("Developer.Resources.Delta", baseline.CapturedAt,
            Stopwatch.GetElapsedTime(baseline.Timestamp, Timestamp).TotalSeconds,
            (WorkingSetBytes - baseline.WorkingSetBytes) / 1048576d,
            (PrivateBytes - baseline.PrivateBytes) / 1048576d,
            (ManagedBytes - baseline.ManagedBytes) / 1048576d,
            Handles - baseline.Handles, Threads - baseline.Threads,
            Gen0 - baseline.Gen0, Gen1 - baseline.Gen1, Gen2 - baseline.Gen2) + Environment.NewLine +
        Translations.Get("Developer.Resources.Scope");
}
