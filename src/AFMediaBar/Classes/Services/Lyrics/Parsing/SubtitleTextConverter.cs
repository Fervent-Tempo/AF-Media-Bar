// 字幕转歌词：SRT / ASS(SSA) 的时间轴是「起止区间」，先归一化成 LRC 行再交给仓库现有的解析与合并链。
using System.Net;
using System.Text.RegularExpressions;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 把字幕文本（SubRip 的 .srt、SubStation Alpha 的 .ass/.ssa）转成 LRC 文本。
/// Converts subtitle text (.srt from SubRip, .ass/.ssa from SubStation Alpha) into LRC text.
///
/// 只做格式归一化：取每条的起始时间与文本，丢掉序号、样式块与显示标记，其余交给 <see cref="LyricsTextParser"/>。
/// 这样译文合并、信息行过滤、补齐末行结束时间等既有能力都能直接复用，不需要第二套解析链。
/// This only normalizes the format: it keeps each cue's start time and text, drops counters, style blocks, and display
/// markup, and leaves the rest to <see cref="LyricsTextParser"/>, so translation merging, credit filtering, and end-time
/// completion keep working without a second parsing chain.
/// </summary>
public static class SubtitleTextConverter
{
    /// <summary>字幕里可能出现的同一行内的多个时间戳分隔写法：SRT 用逗号，ASS 用点，小数位数 1~3 位。
    /// The time spellings both formats use: SRT separates milliseconds with a comma, ASS with a dot, and fractions carry 1-3 digits.</summary>
    private static readonly Regex Clock = new(
        @"^\s*(?<h>\d{1,3}):(?<m>\d{1,2}):(?<s>\d{1,2})(?:[.,](?<f>\d{1,3}))?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>ASS 的样式覆盖块：{\fad(200,200)\pos(320,40)}、karaoke 的 {\k20} 都在花括号里。
    /// ASS override blocks: {\fad(200,200)\pos(320,40)} and karaoke tags such as {\k20} all live inside braces.</summary>
    private static readonly Regex OverrideBlock = new(@"\{[^}]*\}", RegexOptions.Compiled);

    /// <summary>SRT 里常见的 HTML 标记：&lt;i&gt;、&lt;font color="..."&gt;。
    /// HTML tags common in SRT: &lt;i&gt; and &lt;font color="..."&gt;.</summary>
    private static readonly Regex HtmlTag = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>
    /// 转成 LRC 文本；不是可识别的字幕时返回 null，调用方按「没有歌词」处理。
    /// Returns LRC text, or null when the input is not a recognizable subtitle so the caller can treat it as absent.
    /// </summary>
    public static string? TryConvert(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lines = LooksLikeAss(text) ? ConvertAss(text) : ConvertSrt(text);
        return lines.Count > 0 ? string.Join('\n', lines) : null;
    }

    /// <summary>ASS 一定有 [Events] 段与 Dialogue 行；SRT 两者都没有。
    /// An ASS file always carries an [Events] section and Dialogue lines; an SRT carries neither.</summary>
    private static bool LooksLikeAss(string text) =>
        text.Contains("[Events]", StringComparison.OrdinalIgnoreCase) &&
        text.Contains("Dialogue:", StringComparison.OrdinalIgnoreCase);

    /// <summary>SRT：可选序号行 + `起 --> 止` + 一到多行文本，空行分段。
    /// SRT: an optional counter line, `start --> end`, one or more text lines, and a blank line between cues.</summary>
    private static List<string> ConvertSrt(string text)
    {
        var lines = new List<string>();
        var cue = new List<string>();
        double? start = null;

        void Flush()
        {
            var content = Clean(string.Join(' ', cue));
            if (start is { } begin && content.Length > 0) lines.Add(FormatLrcLine(begin, content));
            cue.Clear();
            start = null;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            if (line.Contains("-->", StringComparison.Ordinal))
            {
                var halves = line.Split("-->", 2);
                if (TryParseClock(halves[0], out var begin))
                {
                    Flush();
                    start = begin;
                    continue;
                }
            }

            // 时间轴之前的序号行不进正文：此时还没有起始时间。
            if (start is not null) cue.Add(line);
        }

        Flush();
        return lines;
    }

    /// <summary>
    /// ASS/SSA：只读 [Events] 段，按 Format 行给出的字段位置取 Start 与 Text。
    /// Text 是最后一个字段，本身可能含逗号，所以按字段数切分，剩余部分整体作为文本。
    /// </summary>
    private static List<string> ConvertAss(string text)
    {
        var lines = new List<string>();
        var inEvents = false;
        var fieldCount = 10;
        var startIndex = 1;
        var textIndex = 9;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('['))
            {
                inEvents = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inEvents) continue;

            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                var names = line["Format:".Length..].Split(',').Select(name => name.Trim()).ToArray();
                fieldCount = names.Length;
                startIndex = Array.FindIndex(names, name => name.Equals("Start", StringComparison.OrdinalIgnoreCase));
                textIndex = Array.FindIndex(names, name => name.Equals("Text", StringComparison.OrdinalIgnoreCase));
                continue;
            }

            // Comment 行是注释，不参与歌词。
            if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)) continue;
            if (fieldCount <= 0 || startIndex < 0 || textIndex < 0) continue;

            var fields = line["Dialogue:".Length..].Split(',', fieldCount);
            if (fields.Length <= Math.Max(startIndex, textIndex)) continue;
            if (!TryParseClock(fields[startIndex], out var begin)) continue;

            var content = Clean(fields[textIndex]);
            if (content.Length > 0) lines.Add(FormatLrcLine(begin, content));
        }

        return lines;
    }

    /// <summary>去掉样式块与标记、把 ASS 的软换行压成空格；两种格式共用的清理。
    /// Drops override blocks and markup and folds ASS soft breaks into spaces; shared by both formats.</summary>
    private static string Clean(string text)
    {
        var cleaned = OverrideBlock.Replace(text, string.Empty);
        cleaned = HtmlTag.Replace(cleaned, string.Empty);
        cleaned = cleaned.Replace("\\N", " ").Replace("\\n", " ").Replace("\\h", " ");
        return WebUtility.HtmlDecode(cleaned).Trim();
    }

    /// <summary>`H:MM:SS,mmm` 与 `H:MM:SS.cc` 共用一套解析：小数位按位数折算。
    /// One parser for `H:MM:SS,mmm` and `H:MM:SS.cc`: the fraction is scaled by its digit count.</summary>
    private static bool TryParseClock(string value, out double seconds)
    {
        seconds = 0;
        var match = Clock.Match(value);
        if (!match.Success) return false;

        var fraction = match.Groups["f"].Value;
        var milliseconds = fraction.Length == 0 ? 0 : int.Parse(fraction) * (fraction.Length switch
        {
            1 => 100,
            2 => 10,
            _ => 1
        });

        seconds = int.Parse(match.Groups["h"].Value) * 3600d
                + int.Parse(match.Groups["m"].Value) * 60d
                + int.Parse(match.Groups["s"].Value)
                + milliseconds / 1000d;
        return true;
    }

    /// <summary>LRC 时间戳是「分:秒.百分秒」，分钟不进位到小时，长字幕也能落在同一行内。
    /// LRC stamps read `minutes:seconds.hundredths`; minutes never carry into hours so long files stay on one line.</summary>
    private static string FormatLrcLine(double seconds, string content)
    {
        var total = TimeSpan.FromSeconds(seconds);
        return $"[{(int)total.TotalMinutes:00}:{total.Seconds:00}.{total.Milliseconds / 10:00}]{content}";
    }
}
