// 按运行时观察序号选择来源；不执行系统调用，不持有会话或计时资源。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

internal sealed record MediaSelectionEntry(MediaSourceCandidate Source, string SourceId, long Added, long Playback);

/// <summary>媒体来源自动选择的纯排序策略。</summary>
public static class MediaAutoSwitchPolicy
{
    /// <summary>分层取源的顺序；未登记的来源不在其中，只在三层都空时兜底。/ Tier order; unregistered sources are absent and only serve as the fallback.</summary>
    private static readonly MediaSourceCategory[] CategoryPriority =
        [MediaSourceCategory.Music, MediaSourceCategory.Video, MediaSourceCategory.Browser];

    internal static MediaSourceCandidate? Select(IReadOnlyList<MediaSelectionEntry> entries,
        MediaSelectionSettings settings, string? selectedKey, string? focusedSessionKey)
    {
        var current = entries.FirstOrDefault(entry => entry.Source.Key == selectedKey);
        var available = entries.Where(entry => !entry.Source.IsRecovering).ToArray();
        if (settings.Mode == MediaSelectionMode.PreferredApplication && settings.PreferredSourceId is { } preferred)
        {
            var preferredEntries = entries.Where(entry =>
                string.Equals(entry.SourceId, preferred, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (preferredEntries.Length > 0)
                return preferredEntries.FirstOrDefault(entry => entry.Source.Key == selectedKey)?.Source ??
                    Rank(preferredEntries, entry => entry.Added, selectedKey, focusedSessionKey)?.Source;
        }

        if (settings.Mode == MediaSelectionMode.LatestSession)
            return Rank(available, entry => entry.Added, selectedKey, focusedSessionKey)?.Source ?? current?.Source;

        if (settings.Mode == MediaSelectionMode.MusicApplications)
        {
            // 逐层取第一个有候选的类别：纯音频播放器 → 通用播放器 → 浏览器。层内优先正在播放的，
            // 全部暂停时保持该层里当前选中的那个，避免同层之间来回跳。
            // Take the first non-empty tier, best tier first: pure audio, then general players, then browsers. Inside a tier,
            // whatever is playing wins; with everything paused the current selection of that tier is kept.
            foreach (var category in CategoryPriority)
            {
                var tier = available.Where(entry => MediaSourceNameFormatter.GetCategory(entry.SourceId) == category).ToArray();
                if (tier.Length == 0) continue;
                var tierPlaying = tier.Where(entry => entry.Source.IsPlaying).ToArray();
                return tierPlaying.Length > 0
                    ? Rank(tierPlaying, entry => entry.Playback, selectedKey, focusedSessionKey)?.Source
                    : tier.FirstOrDefault(entry => entry.Source.Key == selectedKey)?.Source ??
                      Rank(tier, _ => 0, selectedKey, focusedSessionKey)?.Source;
            }
            // 三层都没有候选：落到下面的通用逻辑，让未登记的来源也有机会。
        }

        var playing = available.Where(entry => entry.Source.IsPlaying).ToArray();
        return playing.Length > 0
            ? Rank(playing, entry => entry.Playback, selectedKey, focusedSessionKey)?.Source
            : current?.Source ?? Rank(available, _ => 0, selectedKey, focusedSessionKey)?.Source;
    }

    private static MediaSelectionEntry? Rank(IEnumerable<MediaSelectionEntry> entries,
        Func<MediaSelectionEntry, long> sequence, string? selectedKey, string? focusedSessionKey) => entries
        .OrderByDescending(sequence)
        .ThenByDescending(entry => entry.Source.Key == selectedKey)
        .ThenByDescending(entry => focusedSessionKey is not null && entry.Source.SessionKey == focusedSessionKey)
        .ThenBy(entry => entry.Source.Key, StringComparer.Ordinal)
        .FirstOrDefault();
}