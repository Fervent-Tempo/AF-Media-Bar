using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 歌词缓存失效策略：哪些设置变化需要把已缓存的取词结果丢掉并重新取词。
/// Lyric-cache invalidation policy: which settings changes have to drop the cached retrieval results and fetch again.
///
/// 取词结果按曲目缓存（含"未命中"），因此只改呈现的设置不该触发重新取词：拖动未唱部分不透明度的滑杆每走一格都重发请求，
/// 既不必要又会打到第三方接口。反过来，改变"从哪些来源取词"、"多严格才算匹配"或"要不要过滤信息行"之后，当前这一首必须
/// 重新走一遍取词链，否则用户会以为设置没生效。
/// Retrieval results are cached per track, including misses, so a presentation-only change must not refetch: dragging the
/// unsung-opacity slider would otherwise re-request on every step, which is pointless and hits third-party endpoints. Conversely,
/// changing which sources are used, how strict a match has to be, or whether info lines are filtered has to send the current
/// track through the chain again, or the setting looks like it does nothing.
/// </summary>
public static class LyricsCacheInvalidationPolicy
{
    /// <summary>
    /// 判断一次设置变更是否需要清空歌词缓存。
    /// Decides whether one settings change has to clear the lyric cache.
    /// </summary>
    /// <param name="propertyName">变更的属性名，重置事件为 null / The changed property name, null for a reset event.</param>
    /// <param name="resetScope">重置范围，普通变更为 null / The reset scope, null for an ordinary change.</param>
    /// <returns>需要重新取词时为 true / True when the lyrics have to be fetched again.</returns>
    public static bool ShouldClearCache(string? propertyName, SettingsResetScope? resetScope)
    {
        // 重置歌词页或全部设置都可能改到取词相关字段，因此一律重新取词。
        // Resetting the lyrics page or everything can change a retrieval-related field, so both refetch.
        if (resetScope is SettingsResetScope.Lyrics or SettingsResetScope.All)
        {
            return true;
        }

        return propertyName is
            nameof(AppSettings.LyricsSource) or
            nameof(AppSettings.LyricsMatchStrictness) or
            nameof(AppSettings.LyricsInfoLineFilterEnabled) or
            // 并发与采纳设置同样决定"这一次取词用哪些请求、采纳哪个结果"，改了它们旧结果已经不属于当前配置。
            // The concurrency and adoption settings equally decide which requests a retrieval sends and which result is
            // adopted, so results fetched under the old values no longer belong to the current configuration.
            nameof(AppSettings.LyricsAdoptionMode) or
            nameof(AppSettings.LyricsConcurrencyBatchSize) or
            nameof(AppSettings.LyricsAdoptionDeadlineMilliseconds);
    }
}
