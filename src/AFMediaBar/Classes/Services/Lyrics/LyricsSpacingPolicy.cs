using System.Text;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 歌词间距的纯策略：双行行距的高度夹取，以及为中/日/韩文字插入窄空格实现的字距。
///
/// WPF 的 TextBlock 没有字距属性（`CharacterSpacing` 是 WinUI 的），因此字距在显示层实现：在符合条件的字符之间插入窄空格。
/// 插入后的字符串同时是擦亮、跑马灯与自动尺寸测量所用的字符串，因此两侧始终对齐；策略本身只做纯文本变换，
/// 宽度由调用方按当前字体实测后传入。
/// Pure policy for lyric spacing: height clamping for the two-line gap, and character spacing implemented by inserting narrow space
/// characters around CJK text.
///
/// WPF's TextBlock has no letter-spacing property (`CharacterSpacing` belongs to WinUI), so character spacing is implemented in the
/// display layer by inserting narrow spaces between eligible characters. The resulting string is also the one the highlight, the
/// marquee, and the auto-size measurement use, so both sides always agree; the policy itself is a pure text transform, and widths are
/// measured by the caller with the actual font.
/// </summary>
public static class LyricsSpacingPolicy
{
    /// <summary>一行文字在容器里至少需要的高度超出字号的余量（DIP）。/ Vertical overhead a text line needs beyond its font size, in DIP.</summary>
    public const double MinimumLineHeightOverheadDip = 3;

    /// <summary>
    /// 字距候选空格（从窄到宽：hair → thin → four-per-em → three-per-em → en）。
    /// 调用方按当前字体逐一实测宽度后交给 <see cref="ResolveSeparator"/> 选择。
    /// Candidate space characters for character spacing, narrowest first (hair, thin, four-per-em, three-per-em, en).
    /// The caller measures each one with the actual font and hands the widths to <see cref="ResolveSeparator"/>.
    /// </summary>
    public static IReadOnlyList<string> SeparatorCandidates { get; } = ["\u200A", "\u2009", "\u2005", "\u2004", "\u2002"];

    /// <summary>
    /// 把配置的行距夹进"两行加间距不超出可用高度"的区间；输入不可用时返回 0（退回两行紧邻）。
    /// Clamps the configured line gap so the two lines plus the gap stay within the available height; unusable inputs return zero,
    /// which falls back to two adjacent lines.
    /// </summary>
    /// <param name="configuredGapDip">设置里的行距（DIP，已按持久化区间归一化）。/ Configured gap in DIP (already normalized into the persisted range).</param>
    /// <param name="availableHeightDip">媒体文字区的可用高度（DIP）。/ Available height of the media-text area in DIP.</param>
    /// <param name="minimumLineHeightDip">单行可用的最低高度（DIP）。/ Minimum usable height of one line in DIP.</param>
    public static double ResolveLineGapDip(double configuredGapDip, double availableHeightDip, double minimumLineHeightDip)
    {
        if (!double.IsFinite(configuredGapDip) || configuredGapDip <= 0 ||
            !double.IsFinite(availableHeightDip) || availableHeightDip <= 0 ||
            !double.IsFinite(minimumLineHeightDip) || minimumLineHeightDip <= 0)
        {
            return 0;
        }

        var maximumGap = Math.Max(0, availableHeightDip - 2 * minimumLineHeightDip);
        return Math.Min(configuredGapDip, maximumGap);
    }

    /// <summary>
    /// 两行各分到的高度：可用高度去掉行距后平分；可用高度不可用时返回 0。
    /// Height each of the two lines gets: the available height minus the gap, split evenly; zero when the available height is unusable.
    /// </summary>
    /// <param name="availableHeightDip">可用高度（DIP）。/ Available height in DIP.</param>
    /// <param name="lineGapDip">已夹取的行距（DIP）。/ Clamped line gap in DIP.</param>
    public static double ResolveLineHeightDip(double availableHeightDip, double lineGapDip)
    {
        if (!double.IsFinite(availableHeightDip) || availableHeightDip <= 0)
        {
            return 0;
        }

        if (!double.IsFinite(lineGapDip) || lineGapDip <= 0)
        {
            return availableHeightDip / 2;
        }

        return Math.Max(0, (availableHeightDip - lineGapDip) / 2);
    }

    /// <summary>单行的最低高度：字号 + 余量；字号不可用时只用余量。/ Minimum height of one line: font size plus overhead, or the overhead alone when the size is unusable.</summary>
    /// <param name="fontSizeDip">歌词字号（DIP）。/ Lyric font size in DIP.</param>
    public static double ResolveMinimumLineHeightDip(double fontSizeDip) =>
        double.IsFinite(fontSizeDip) && fontSizeDip > 0 ? fontSizeDip + MinimumLineHeightOverheadDip : MinimumLineHeightOverheadDip;

    /// <summary>
    /// 在符合条件的位置插入字距分隔符：两字符中至少一个是 CJK（含全角标点、假名与谚文）且两侧都不是空白时插入；
    /// 拉丁字母、数字与已有空白周围不插入，因此英文单词保持原样。
    /// Inserts the separator at eligible positions: between two characters when at least one of them is CJK (full-width punctuation,
    /// kana, and Hangul included) and neither side is whitespace; nothing is inserted around Latin letters, digits, or existing
    /// whitespace, so English words stay intact.
    /// </summary>
    /// <param name="text">原始文本。/ Source text.</param>
    /// <param name="separator">分隔符；为空时返回原文。/ The separator; an empty one returns the text unchanged.</param>
    public static string ApplyCharacterSpacing(string? text, string? separator)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(separator) || text.Length < 2)
        {
            return text ?? string.Empty;
        }

        var builder = new StringBuilder(text.Length + text.Length / 2);
        builder.Append(text[0]);
        for (var index = 1; index < text.Length; index++)
        {
            var left = text[index - 1];
            var right = text[index];
            if (!char.IsWhiteSpace(left) && !char.IsWhiteSpace(right) && (IsCjk(left) || IsCjk(right)))
            {
                builder.Append(separator);
            }

            builder.Append(right);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 按实测宽度选出字距分隔符：取"不小于目标的最小者"，保证滑杆单调且每档都可见；全部候选都小于目标时取最宽者；
    /// 全为零宽（字体缺这些字形）时返回 null，调用方退化为不插。
    /// Picks the separator from measured widths: the smallest one not below the target, which keeps the slider monotone and every
    /// step visible; when every candidate is below the target the widest wins; when all are zero-width (the font lacks the glyphs)
    /// null is returned and the caller degrades to no spacing.
    /// </summary>
    /// <param name="targetDip">目标空隙（DIP）。/ Target gap in DIP.</param>
    /// <param name="measured">与 <see cref="SeparatorCandidates"/> 一一对应的实测宽度。/ Measured widths matching <see cref="SeparatorCandidates"/> one to one.</param>
    public static string? ResolveSeparator(double targetDip, IReadOnlyList<double>? measured)
    {
        if (!double.IsFinite(targetDip) || targetDip <= 0 ||
            measured is null || measured.Count != SeparatorCandidates.Count)
        {
            return null;
        }

        string? smallestNotBelow = null;
        var smallestNotBelowWidth = double.MaxValue;
        string? widest = null;
        var widestWidth = 0d;
        for (var index = 0; index < measured.Count; index++)
        {
            var width = measured[index];
            if (!double.IsFinite(width) || width <= 0)
            {
                continue;
            }

            if (width > widestWidth)
            {
                widestWidth = width;
                widest = SeparatorCandidates[index];
            }

            if (width >= targetDip && width < smallestNotBelowWidth)
            {
                smallestNotBelowWidth = width;
                smallestNotBelow = SeparatorCandidates[index];
            }
        }

        return smallestNotBelow ?? widest;
    }

    /// <summary>是否是 CJK 字符（含全角标点、假名与谚文）。/ Whether the character is CJK (full-width punctuation, kana, and Hangul included).</summary>
    private static bool IsCjk(char value) => value switch
    {
        >= '\u1100' and <= '\u11FF' => true,   // Hangul Jamo
        >= '\u2E80' and <= '\u2EFF' => true,   // CJK radicals
        >= '\u3000' and <= '\u303F' => true,   // CJK punctuation
        >= '\u3040' and <= '\u30FF' => true,   // Hiragana and Katakana
        >= '\u3400' and <= '\u4DBF' => true,   // CJK Extension A
        >= '\u4E00' and <= '\u9FFF' => true,   // CJK Unified Ideographs
        >= '\uAC00' and <= '\uD7AF' => true,   // Hangul syllables
        >= '\uF900' and <= '\uFAFF' => true,   // CJK compatibility ideographs
        >= '\uFF00' and <= '\uFFEF' => true,   // Full-width forms
        _ => false
    };
}
