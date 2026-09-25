using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 解析阶段的行草稿：结束时间可以缺失，由 <see cref="LyricTimingPolicy"/> 统一补齐。
/// A parsed line draft whose end time may be absent; <see cref="LyricTimingPolicy"/> completes it uniformly.
/// </summary>
/// <param name="Start">起始秒数 / Start in seconds.</param>
/// <param name="End">结束秒数，缺失为 null / End in seconds, null when the source has none.</param>
/// <param name="Text">行文本 / Line text.</param>
public readonly record struct LyricLineDraft(
    double Start,
    double? End,
    string Text,
    IReadOnlyList<LyricWord>? Words = null,
    string? Translation = null,
    string? Romanization = null,
    bool IsBackground = false);

/// <summary>
/// 把行草稿规范化为可直接呈现的行集合：排序、补齐结束时间、把音节夹进行窗口。
/// Normalizes line drafts into renderable lines: sorted, with completed end times and syllables clamped into the window.
///
/// 结束时间缺失时取下一行起点（最后一行取曲目时长，仍不可用时取固定的尾部时长），这样"当前行"永远有一个有限窗口。
/// A missing end falls back to the next line's start, the track duration for the last line, and finally a fixed tail,
/// so the active line always has a finite window.
/// </summary>
public static class LyricTimingPolicy
{
    /// <summary>末行既没有结束时间也没有曲目时长时使用的尾部时长（秒）。
    /// Tail length in seconds used when the last line has neither an end time nor a track duration.</summary>
    public const double DefaultTailSeconds = 5;

    /// <summary>
    /// 规范化行草稿。
    /// Normalizes line drafts.
    /// </summary>
    /// <param name="drafts">解析得到的行草稿 / Line drafts produced by the parser.</param>
    /// <param name="durationSeconds">曲目时长（秒），未知时传 0 或非有限值 / Track duration in seconds; pass 0 or a non-finite value when unknown.</param>
    /// <returns>按起始时间升序排列、结束时间已补齐的行集合 / Lines ordered by start time with completed end times.</returns>
    public static IReadOnlyList<LyricLine> Resolve(IReadOnlyList<LyricLineDraft> drafts, double durationSeconds)
    {
        if (drafts.Count == 0)
        {
            return [];
        }

        var trackDuration = double.IsFinite(durationSeconds) && durationSeconds > 0 ? durationSeconds : 0;

        // 空白行不占呈现位置，但它仍参与了下一行起点的计算，因此只在这里丢弃，不参与"结束时间补齐"之外的处理。
        // Blank lines take no display slot, yet they still take part in the next-line start that completes the timing.
        var ordered = drafts
            .Where(static draft => double.IsFinite(draft.Start) && !string.IsNullOrWhiteSpace(draft.Text))
            .OrderBy(static draft => draft.Start)
            .ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        var lines = new List<LyricLine>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var draft = ordered[i];
            var start = draft.Start;
            var end = ResolveEnd(draft, ordered, i, trackDuration);
            lines.Add(new LyricLine(start, end, draft.Text.Trim())
            {
                Words = ClampWords(draft.Words, start, end),
                Translation = string.IsNullOrWhiteSpace(draft.Translation) ? null : draft.Translation.Trim(),
                Romanization = string.IsNullOrWhiteSpace(draft.Romanization) ? null : draft.Romanization.Trim(),
                IsBackground = draft.IsBackground
            });
        }

        return lines;
    }

    private static double ResolveEnd(
        LyricLineDraft draft,
        IReadOnlyList<LyricLineDraft> ordered,
        int index,
        double trackDuration)
    {
        var start = draft.Start;
        if (draft.End is { } explicitEnd && explicitEnd > start)
        {
            return explicitEnd;
        }

        if (index + 1 < ordered.Count)
        {
            var nextStart = ordered[index + 1].Start;
            if (nextStart > start)
            {
                return nextStart;
            }
        }

        if (trackDuration > start)
        {
            return trackDuration;
        }

        return start + DefaultTailSeconds;
    }

    private static IReadOnlyList<LyricWord> ClampWords(IReadOnlyList<LyricWord>? words, double start, double end)
    {
        if (words is not { Count: > 0 })
        {
            return [];
        }

        var ordered = words
            .Where(static word => !string.IsNullOrEmpty(word.Text) && double.IsFinite(word.Start) && double.IsFinite(word.End))
            .OrderBy(static word => word.Start)
            .ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        // QRC 一类的逐字文本把音节时间写成相对行起的偏移：整条音节时间轴落在行起之前且自身有长度时，
        // 按相对偏移还原，否则音节会被夹成零长度而丢掉逐字信息。
        // Some syllable formats (QRC among them) write syllable times as offsets from the line start: when the whole
        // timeline sits before the line start yet has a positive span, it is restored as relative offsets, otherwise
        // clamping would collapse every syllable to zero length and lose the per-syllable information.
        var offset = ordered[^1].End <= start && ordered[^1].End > ordered[0].Start ? start : 0d;

        var clamped = new List<LyricWord>(ordered.Count);
        foreach (var word in ordered)
        {
            var wordStart = Math.Max(start, Math.Min(end, word.Start + offset));
            var wordEnd = Math.Max(start, Math.Min(end, word.End + offset));
            if (wordEnd < wordStart)
            {
                (wordStart, wordEnd) = (wordEnd, wordStart);
            }

            // 夹取后没有长度的音节不再参与进度，否则擦亮会被一个落在行外的片段拖住。
            // A syllable with no length after clamping no longer drives the progress, otherwise a span outside the window would
            // stall the highlight.
            if (wordEnd <= wordStart)
            {
                continue;
            }

            clamped.Add(new LyricWord(wordStart, wordEnd, word.Text));
        }

        return clamped;
    }
}
