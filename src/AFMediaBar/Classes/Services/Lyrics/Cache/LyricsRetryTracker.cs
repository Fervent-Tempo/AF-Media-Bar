// 按曲目记录自动取词的连续失败次数；与结果缓存分开，过期和内存剪枝不重置重试预算。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Classes.Services.Lyrics;

internal sealed class LyricsRetryTracker<TKey>(int capacity) where TKey : notnull
{
    private const int MaximumFailures = 10;
    private readonly LruCache<TKey, int> _failures = new(capacity);

    internal bool CanRequest(TKey key, bool isPlaying) =>
        !_failures.TryGetValue(key, out var failures) || failures == 0 ||
        isPlaying && failures < MaximumFailures;

    internal void RecordResult(TKey key, LyricsResult? result, string title)
    {
        _failures.TryGetValue(key, out var failures);
        var next = result is null ? Math.Min(failures + 1, MaximumFailures) : 0;
        _failures.Set(key, next);
        if (next == MaximumFailures && failures < MaximumFailures)
            AppLogService.Current?.Info("Lyrics", $"自动取词已停止: title=\"{title}\" consecutiveFailures={MaximumFailures}");
    }

    internal void Clear() => _failures.Clear();
}
