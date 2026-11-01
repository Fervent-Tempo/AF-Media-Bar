namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 确认无歌词的纯逻辑：明确的纯音乐标记 + 其余可呈现行全部是信息行（署名）时，整份结果不是歌词。
/// Pure no-lyrics decision: an explicit instrumental marker plus only info/credit lines in the presentable rows
/// means the payload has no lyric body.
///
/// 用于修复 #267：网易云的纯音乐载荷是"若干署名行 + 纯音乐，请欣赏"，署名行超过一半时信息行过滤会因防误删保护
/// 放弃过滤，占位行再被丢弃，剩余署名就会被当作歌词。这里只在没有正文时才确认，正文检查优先。
/// Fixes #267: a NetEase instrumental payload is several credit lines plus "纯音乐，请欣赏"; when credits exceed half,
/// the anti-deletion guard abandons info-line filtering, the placeholder is then dropped, and the credits would be shown
/// as lyrics. This policy only confirms when there is no body, so a real lyric line always wins.
/// </summary>
internal static class LyricNoLyricsPolicy
{
    /// <summary>
    /// 判断结果是否应确认无歌词。
    /// Decides whether the parsed payload should be treated as confirmed no lyrics.
    /// </summary>
    /// <param name="kinds">逐行的占位分类 / Per-line placeholder classification.</param>
    /// <param name="infoLines">逐行的信息行判定；不可用时为 null / Per-line info-line flags, or null when unavailable.</param>
    /// <returns>存在纯音乐标记且没有正文时为 true / True when an instrumental marker exists and no body remains.</returns>
    public static bool ConfirmsNoLyrics(IReadOnlyList<LyricPlaceholderKind> kinds, IReadOnlyList<bool>? infoLines)
    {
        if (kinds.Count == 0 || (infoLines is not null && infoLines.Count != kinds.Count))
        {
            return false;
        }

        var hasInstrumentalMarker = false;
        for (var index = 0; index < kinds.Count; index++)
        {
            switch (kinds[index])
            {
                case LyricPlaceholderKind.Instrumental:
                    hasInstrumentalMarker = true;
                    break;
                case LyricPlaceholderKind.Transient:
                    // 临时状态只能说明这一次没取到，不能确认歌曲没有歌词。
                    // A transient state only says this attempt failed; it must never confirm the track has no lyrics.
                    return false;
                case LyricPlaceholderKind.Blank:
                case LyricPlaceholderKind.NoLyricsPlaceholder:
                    break;
                case LyricPlaceholderKind.None:
                    if (infoLines is null || !infoLines[index])
                    {
                        return false;
                    }

                    break;
            }
        }

        return hasInstrumentalMarker;
    }
}
