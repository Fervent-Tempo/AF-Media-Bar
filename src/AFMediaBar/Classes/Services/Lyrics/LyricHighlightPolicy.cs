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
