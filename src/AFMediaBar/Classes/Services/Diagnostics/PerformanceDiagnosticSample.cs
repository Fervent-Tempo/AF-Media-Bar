#if DEBUG
namespace AFMediaBar.Classes.Services.Diagnostics;

/// <summary>固定事件字段，不包含歌词、歌曲或用户路径。</summary>
internal sealed record PerformanceDiagnosticEvent(string Category, string Name, DateTimeOffset Time,
    long Timestamp, double? DurationMs = null, int? BrowserProcessId = null, string? Instance = null, int? Generation = null);
#endif
