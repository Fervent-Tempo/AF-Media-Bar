using System.Text;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 歌词间距的纯策略：双行行距的高度夹取，以及为中/日/韩文字实现连续字距。
///
/// WPF 的 TextBlock 没有字距属性（`CharacterSpacing` 是 WinUI 的），因此字距在显示层实现：先在符合条件的字符之间插入
/// <see cref="GapMarker"/> 标记，渲染时把每个标记换成"按目标宽度缩放字号的空档 Run"——字形宽度随字号线性变化，
/// 缩放因此能把空隙连续地调到任意宽度，而不是只在几个空格字符的固有宽度里挑一个。
/// 标记字符还让跑马灯与擦亮继续按"字符"工作；宽度则由 <see cref="MeasureSpacingAware"/> 与渲染严格同构地换算。
/// Pure policy for lyric spacing: height clamping for the two-line gap, and continuous character spacing for CJK text.
///
/// WPF's TextBlock has no letter-spacing property (`CharacterSpacing` belongs to WinUI), so character spacing is implemented in the
/// display layer: a <see cref="GapMarker"/> is inserted between eligible characters, and rendering replaces every marker with a
/// spacer run whose font size is scaled to the target width — glyph advances scale linearly with the font size, so scaling tunes the
/// gap continuously instead of choosing among the intrinsic widths of a few space characters. The marker also keeps the marquee and
/// the highlight working in characters, while <see cref="MeasureSpacingAware"/> converts widths exactly the way rendering lays out.
/// </summary>
public static class LyricsSpacingPolicy
{
    /// <summary>一行文字在容器里至少需要的高度超出字号的余量（DIP）。/ Vertical overhead a text line needs beyond its font size, in DIP.</summary>
    public const double MinimumLineHeightOverheadDip = 3;

    /// <summary>
    /// 显示串里的字距标记：它本身不决定观感，渲染时被换成按目标宽度缩放的空档 Run。
    /// 选 U+200A 是因为它不可见、不与歌词内容冲突，也仍是空白字符（不参与断行、不影响字形对齐）。
    /// Gap marker inside display strings: it does not decide the look itself, because rendering replaces it with a spacer run scaled
    /// to the target width. U+200A is invisible, never collides with lyric content, and is still whitespace (no line breaking, no
    /// effect on glyph alignment).
    /// </summary>
    public const char GapMarker = '\u200A';

    /// <summary>
    /// 锚定空档候选（从窄到宽）。渲染时取"实测宽度不小于目标的最小者"，再把它的字号缩放为
    /// <c>目标宽度 / 实测宽度</c>，因此实际空隙总是恰好等于目标；只缩不放，行高与基线不受影响。
    /// Anchor space candidates, narrowest first. Rendering takes the smallest one whose measured width is not below the target and
    /// scales its font size by `target / measured`, so the actual gap always equals the target exactly; scaling down only, which
    /// leaves line height and baseline untouched.
    /// </summary>
    public static IReadOnlyList<string> SpacerCandidates { get; } = ["\u200A", "\u2006", "\u2009", "\u2005", "\u2004", "\u2002"];

    /// <summary>
    /// 解析出的锚定空档：用哪个字符、字号缩放比是多少。渲染宽度 = 实测宽度 × <see cref="Scale"/> = 目标宽度。
    /// A resolved spacer: which character to use and by which factor to scale its font size. The rendered width is the measured width
    /// times <see cref="Scale"/>, which equals the target width.
    /// </summary>
    /// <param name="Glyph">锚定字符。/ The anchor character.</param>
    /// <param name="Scale">字号缩放比（基准字号 × 该值 = Run 的字号）。/ Font-size scale (base size times this value is the run's size).</param>
    public readonly record struct Spacer(string Glyph, double Scale);

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
    /// 在符合条件的位置插入字距标记：两字符中至少一个是 CJK（含全角标点、假名与谚文）且两侧都不是空白时插入；
    /// 拉丁字母、数字与已有空白周围不插入，因此英文单词保持原样。
    /// Inserts the gap marker at eligible positions: between two characters when at least one of them is CJK (full-width punctuation,
    /// kana, and Hangul included) and neither side is whitespace; nothing is inserted around Latin letters, digits, or existing
    /// whitespace, so English words stay intact.
    /// </summary>
    /// <param name="text">原始文本。/ Source text.</param>
    public static string ApplyCharacterSpacing(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 2)
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
                builder.Append(GapMarker);
            }

            builder.Append(right);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 按实测宽度解析锚定空档：取"不小于目标的最小者"，再把它的字号缩放到目标宽度（缩放比 ≤ 1）。
    /// 全部候选都小于目标时退化为最宽的那个（缩放比 &gt; 1，只有异常字体才会遇到）；
    /// 全为零宽（字体缺这些字形）时返回 null，调用方退化为不插。
    /// Resolves the anchor spacer from measured widths: the smallest one not below the target wins, and its font size is scaled so the
    /// rendered width equals the target (a scale of at most one). When every candidate is below the target the widest one is used
    /// instead (a scale above one, only reachable with unusual fonts); when all are zero-width (the font lacks the glyphs) null is
    /// returned and the caller degrades to no spacing.
    /// </summary>
    /// <param name="targetDip">目标空隙（DIP）。/ Target gap in DIP.</param>
    /// <param name="measured">与 <see cref="SpacerCandidates"/> 一一对应的实测宽度。/ Measured widths matching <see cref="SpacerCandidates"/> one to one.</param>
    public static Spacer? ResolveSpacer(double targetDip, IReadOnlyList<double>? measured)
    {
        if (!double.IsFinite(targetDip) || targetDip <= 0 ||
            measured is null || measured.Count != SpacerCandidates.Count)
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
                widest = SpacerCandidates[index];
            }

            if (width >= targetDip && width < smallestNotBelowWidth)
            {
                smallestNotBelowWidth = width;
                smallestNotBelow = SpacerCandidates[index];
            }
        }

        if (smallestNotBelow is not null)
        {
            return new Spacer(smallestNotBelow, targetDip / smallestNotBelowWidth);
        }

        // 所有候选都比目标窄：用最宽者放大，空隙仍然恰好等于目标（异常字体才会走到这里）。
        // Every candidate is narrower than the target: the widest is scaled up, still landing exactly on the target (only unusual fonts
        // reach this branch).
        return widest is null ? null : new Spacer(widest, targetDip / widestWidth);
    }

    /// <summary>
    /// 间距感知的宽度：按 <see cref="GapMarker"/> 拆段分别测宽，再加上"标记数 × 目标空隙"。
    /// 与渲染严格同构——渲染把每个标记换成一个恰好目标宽的空档 Run，因此两者永远不会漂移。
    /// Spacing-aware width: measures each run of text between <see cref="GapMarker"/>s and adds one target gap per marker. This is
    /// exactly how rendering lays out — every marker becomes a spacer run of exactly the target width — so the two never drift apart.
    /// </summary>
    /// <param name="text">显示串（可含标记）。/ Display string, possibly carrying markers.</param>
    /// <param name="gapDip">每个标记的空隙（DIP）。/ Gap per marker in DIP.</param>
    /// <param name="measureSegment">按当前字体测量一段无标记文字的宽度。/ Measures one marker-free segment with the actual font.</param>
    public static double MeasureSpacingAware(string? text, double gapDip, Func<string, double> measureSegment)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        if (!double.IsFinite(gapDip) || gapDip <= 0 || text.IndexOf(GapMarker) < 0)
        {
            return measureSegment(text);
        }

        var total = 0d;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != GapMarker)
            {
                continue;
            }

            total += measureSegment(text[start..index]) + gapDip;
            start = index + 1;
        }

        total += measureSegment(text[start..]);
        return total;
    }

    /// <summary>显示串里有多少个字距标记。/ How many gap markers a display string carries.</summary>
    /// <param name="text">显示串。/ Display string.</param>
    public static int CountGaps(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        foreach (var value in text)
        {
            if (value == GapMarker)
            {
                count++;
            }
        }

        return count;
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
