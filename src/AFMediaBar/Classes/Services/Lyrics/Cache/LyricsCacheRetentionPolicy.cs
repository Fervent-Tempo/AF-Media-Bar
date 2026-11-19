// Defines the retry window for inconclusive lyric retrieval; confirmed results keep the normal LRU lifetime.
namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>区分暂时未取得结果与已确认的歌词结果，避免把网络失败永久缓存。</summary>
internal static class LyricsCacheRetentionPolicy
{
    internal static TimeSpan? Lifetime(int consecutiveFailures)
    {
        if (consecutiveFailures == 0)
            return null;

        // 首次失败冷却 2 秒，之后翻倍，30 秒封顶。
        return TimeSpan.FromSeconds(consecutiveFailures >= 5 ? 30 : 1 << consecutiveFailures);
    }
}
