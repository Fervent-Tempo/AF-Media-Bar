// 按运行时观察序号选择来源；不执行系统调用，不持有会话或计时资源。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

internal sealed record MediaSelectionEntry(MediaSourceCandidate Source, string SourceId, long Added, long Playback);

/// <summary>媒体来源自动选择的纯排序策略。</summary>
public static class MediaAutoSwitchPolicy
{
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