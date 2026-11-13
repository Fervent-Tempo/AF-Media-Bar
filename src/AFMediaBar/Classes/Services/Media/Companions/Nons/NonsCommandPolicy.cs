// 队列命令的纯策略：版本变化后只在目标仍存在时重试一次，避免旧列表误作用到新队列。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Media.Companions.Nons;

internal static class NonsCommandPolicy
{
    public static bool ContainsTrack(IReadOnlyList<PlayerCompanionTrack> queue, string trackId) =>
        queue.Any(track => string.Equals(track.TrackId, trackId, StringComparison.Ordinal));

    public static bool ShouldRetryAfterStale(bool targetStillPresent, bool alreadyRetried) =>
        targetStillPresent && !alreadyRetried;
}
