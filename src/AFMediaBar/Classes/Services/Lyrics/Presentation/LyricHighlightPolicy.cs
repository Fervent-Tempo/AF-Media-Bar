using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 逐字擦亮的纯策略：仅依据真实逐字时间轴，把播放位置换算成当前行的已唱进度。
/// Pure syllable-highlight policy: converts playback position into sung progress using only a real word timeline.
///
/// 只有真实逐字时间轴才产生进度；行级歌词不会按整行时长猜测逐字位置。
/// Only a real syllable timeline produces progress; line-level lyrics never guess a word position from the line window.
/// </summary>
public static class LyricHighlightPolicy
{
    /// <summary>
    /// 歌词时间轴与擦亮层的呈现决策。时间轴决定长歌词如何滚动，擦亮层只是叠在同一轨迹上的视觉效果；
    /// 因此关闭擦亮不得关闭时间轴或改用另一种滚动方式。
    /// Presentation decision for the lyric timeline and highlight layer. The timeline decides how a long lyric scrolls, while the
    /// highlight is only a visual layer over that same trajectory, so hiding it must not disable the timeline or select another scroll mode.
    /// </summary>
    /// <param name="AdvanceTimeline">是否按歌词时间轴推进。/ Whether the lyric timeline advances.</param>
    /// <param name="ShowHighlight">是否显示擦亮层。/ Whether the highlight layer is shown.</param>
    public readonly record struct PresentationState(bool AdvanceTimeline, bool ShowHighlight);

    /// <summary>
    /// 分别决定时间轴是否推进、擦亮层是否显示。用户开关只参与后者，保证开关前后的滚动轨迹完全相同。
    /// Separately decides whether the timeline advances and whether the highlight layer is shown. The user switch participates only
    /// in the latter, keeping the scrolling trajectory identical on both sides of the switch.
    /// </summary>
    public static PresentationState ResolvePresentationState(
        bool highlightEnabled,
        bool hasCurrentLine,
        bool connected,
        bool playing,
        bool lyricsVisible,
        bool controlVisible,
        bool isAdvancePruned,
        bool highContrast,
        bool useContinuousMotion)
    {
        var advanceTimeline = hasCurrentLine &&
                              connected &&
                              playing &&
                              lyricsVisible &&
                              controlVisible &&
                              !isAdvancePruned &&
                              useContinuousMotion;
        return new PresentationState(
            AdvanceTimeline: advanceTimeline,
            ShowHighlight: advanceTimeline && highlightEnabled && !highContrast);
    }

    /// <summary>
    /// 计算当前行的擦亮进度。
    /// Computes the highlight progress of the active line.
    /// </summary>
    /// <param name="line">当前行 / The active line.</param>
    /// <param name="positionSeconds">播放位置（秒）/ Playback position in seconds.</param>
    /// <returns>0 到 1 的进度；该行没有可用窗口时返回 null / Progress between 0 and 1, or null when the line has no usable window.</returns>
    public static double? ResolveProgress(LyricLine line, double positionSeconds)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!double.IsFinite(positionSeconds))
        {
            return null;
        }

        var words = CollectUsableWords(line.Words);
        if (words.Count > 0)
        {
            return ResolveWordProgress(words, positionSeconds);
        }

        return null;
    }

    private static List<LyricWord> CollectUsableWords(IReadOnlyList<LyricWord> words)
    {
        var usable = new List<LyricWord>(words.Count);
        foreach (var word in words)
        {
            if (!string.IsNullOrEmpty(word.Text) && word.End > word.Start)
            {
                usable.Add(word);
            }
        }

        return usable;
    }

    private static double? ResolveWordProgress(List<LyricWord> words, double positionSeconds)
    {
        var totalCharacters = 0d;
        foreach (var word in words)
        {
            totalCharacters += word.Text.Length;
        }

        if (totalCharacters <= 0)
        {
            return null;
        }

        if (positionSeconds <= words[0].Start)
        {
            return 0;
        }

        if (positionSeconds >= words[^1].End)
        {
            return 1;
        }

        var sungCharacters = 0d;
        foreach (var word in words)
        {
            if (positionSeconds >= word.End)
            {
                sungCharacters += word.Text.Length;
                continue;
            }

            if (positionSeconds > word.Start)
            {
                // 音节内部按时间线性插值：字符数权重让中英文混排的擦亮速度看起来一致。
                // Inside one syllable the progress is interpolated linearly; the character weight keeps the wipe speed
                // consistent between CJK and Latin text.
                sungCharacters += word.Text.Length * (positionSeconds - word.Start) / (word.End - word.Start);
            }

            break;
        }

        return Math.Clamp(sungCharacters / totalCharacters, 0, 1);
    }

}
