using System.Globalization;
using System.Text.RegularExpressions;

namespace AFMediaBar.Services.Lyrics;

/// <summary>
/// 单条带时间轴的歌词行。 / A single timestamped lyric line.
/// </summary>
public readonly record struct LrcLine(TimeSpan Time, string Text);

/// <summary>
/// 解析 LRC 文本为按时间排序的歌词行，并按播放位置定位当前行。
/// Parses LRC text into time-ordered lines and locates the active line for a playback position.
/// </summary>
public static class LrcParser
{
    // [mm:ss.xx] / [mm:ss:xxx]；支持一行多个时间戳。分钟 1-3 位、秒 1-2 位、小数 1-3 位。
    private static readonly Regex TimestampRegex = new(
        @"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]",
        RegexOptions.Compiled);

    public static IReadOnlyList<LrcLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return Array.Empty<LrcLine>();
        }

        var lines = new List<LrcLine>();
        foreach (var rawLine in lrc.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var matches = TimestampRegex.Matches(line);
            if (matches.Count == 0)
            {
                continue;
            }

            var text = TimestampRegex.Replace(line, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (Match match in matches)
            {
                var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var fractionText = match.Groups[3].Value;
                var fraction = fractionText.Length == 0
                    ? 0
                    : int.Parse(fractionText, CultureInfo.InvariantCulture) /
                        Math.Pow(10, fractionText.Length);
                lines.Add(new LrcLine(
                    TimeSpan.FromSeconds(minutes * 60 + seconds + fraction),
                    text));
            }
        }

        lines.Sort(static (a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    /// <summary>
    /// 返回播放位置对应的行下标；位置早于第一行时返回 -1。
    /// Returns the index of the line active at the position, or -1 before the first line.
    /// </summary>
    public static int FindIndex(IReadOnlyList<LrcLine> lines, TimeSpan position)
    {
        var index = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Time <= position)
            {
                index = i;
            }
            else
            {
                break;
            }
        }

        return index;
    }
}
