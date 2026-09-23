using System.Globalization;

namespace AFMediaBar.Classes.Services;

/// <summary>跑马灯的像素恒速节奏。/ Pixel-constant marquee timing.</summary>
public static class MarqueeTiming
{
    /// <summary>推进的帧间隔。/ Frame interval.</summary>
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>恒定滚动速度（DIP/秒）。/ Constant scroll speed in DIP per second.</summary>
    public const double ScrollSpeedDipPerSecond = 60;

    /// <summary>每帧推进距离（DIP）。/ Distance advanced per frame in DIP.</summary>
    public static double DipPerFrame => ScrollSpeedDipPerSecond * FrameInterval.TotalMilliseconds / 1000;

    /// <summary>开始滚动前的停留时间。/ Lead-in pause before scrolling.</summary>
    public static readonly TimeSpan LeadInDuration = TimeSpan.FromMilliseconds(660);
}

/// <summary>
/// 把跑马灯切分点对齐到完整文本元素，避免拆开代理对、组合记号或变体选择符。
/// Snaps marquee split points to whole text elements so surrogate pairs, combining marks, and variation selectors stay intact.
/// </summary>
public static class MarqueeTextBoundary
{
    /// <summary>向前对齐到不小于给定索引的文本元素起点。/ Snaps forward to the text-element start at or after the index.</summary>
    public static int SnapForward(string? text, int index)
    {
        if (string.IsNullOrEmpty(text) || index <= 0)
            return 0;
        if (index >= text.Length)
            return text.Length;
        if (!MightBeInsideTextElement(text[index]))
            return index;

        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var found = Array.BinarySearch(boundaries, index);
        if (found >= 0)
            return index;
        var next = ~found;
        return next < boundaries.Length ? boundaries[next] : text.Length;
    }

    /// <summary>返回第一个完整文本元素。/ Returns the first whole text element.</summary>
    public static string FirstElement(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : StringInfo.GetNextTextElement(text);

    private static bool MightBeInsideTextElement(char value)
    {
        if (char.IsSurrogate(value))
            return true;

        return CharUnicodeInfo.GetUnicodeCategory(value) is
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark or
            UnicodeCategory.Format;
    }
}

/// <summary>
/// 超宽标题和歌手的轮转策略。只负责字符串窗口、文本元素边界及前缀宽度插值，不承载歌词进度。
/// Rotation policy for overlong titles and artists. It owns string windows, text-element boundaries, and prefix-width
/// interpolation; it never carries lyric progress.
/// </summary>
public static class MarqueeRotationPolicy
{
    /// <summary>轮转接缝的视觉间隔。/ Visual gap at the rotation seam.</summary>
    public const string Separator = "   ";

    /// <summary>解析已回绕并对齐到文本元素边界的窗口起点。/ Resolves a wrapped window start snapped to a text-element boundary.</summary>
    public static int ResolveWindowStart(string? content, int offset)
    {
        if (string.IsNullOrEmpty(content))
            return 0;

        var text = content + Separator;
        return MarqueeTextBoundary.SnapForward(text, NormalizeOffset(offset, text.Length));
    }

    /// <summary>构造轮转后的窗口字符串。/ Builds the rotated window string.</summary>
    public static string BuildWindow(string? content, int offset)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var text = content + Separator;
        var start = ResolveWindowStart(content, offset);
        return start == 0 ? text : text[start..] + text[..start];
    }

    /// <summary>把偏移回绕到窗口长度内。/ Wraps an offset into the window length.</summary>
    public static int NormalizeOffset(int offset, int windowLength) =>
        windowLength <= 0 ? 0 : ((offset % windowLength) + windowLength) % windowLength;

    /// <summary>返回原文加接缝后的窗口长度。/ Returns the content-plus-seam window length.</summary>
    public static int ResolveWindowLength(int contentLength) => contentLength <= 0 ? 0 : contentLength + Separator.Length;

    /// <summary>返回用于宽度测量的原文加接缝。/ Returns content plus its seam for width measurement.</summary>
    public static string BuildSource(string? content) =>
        string.IsNullOrEmpty(content) ? string.Empty : content + Separator;

    /// <summary>
    /// 在前缀宽度表中按小数字符位置插值；越界位置夹到已测范围。
    /// Interpolates a fractional character position in a prefix-width table, clamped to its measured range.
    /// </summary>
    public static double ResolveWidthAt(double[]? prefixWidths, int measuredCharacters, double position)
    {
        if (prefixWidths is null || prefixWidths.Length == 0 || !double.IsFinite(position))
            return 0;

        var last = Math.Clamp(measuredCharacters, 0, prefixWidths.Length - 1);
        if (last <= 0)
            return prefixWidths[0];

        var clamped = Math.Clamp(position, 0, last);
        var index = (int)Math.Floor(clamped);
        var next = Math.Min(index + 1, last);
        return next == index
            ? prefixWidths[index]
            : prefixWidths[index] + (clamped - index) * (prefixWidths[next] - prefixWidths[index]);
    }
}
