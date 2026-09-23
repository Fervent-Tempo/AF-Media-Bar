using System;
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>
/// 任务栏媒体控件的歌词间距：双行行距与横向字距。
/// Lyric spacing for the taskbar media control: the two-line gap and horizontal character spacing.
///
/// 行距由三行网格表达（行 / 行距 / 行），可用高度取自媒体文字区，并在"两行各留最低可读高度"的约束下夹取，
/// 因此调多大都不会把文字挤出任务栏。字距在显示层插入窄空格，候选空格按当前字体实测宽度选取，选择单调，
/// 且插入后的字符串就是擦亮、跑马灯与自动尺寸共同使用的字符串，两侧永远对齐。
/// The line gap is expressed by a three-row grid (line / gap / line); the available height comes from the media-text area and the gap
/// is clamped so both lines keep a minimum readable height, which is why no setting can push text out of the taskbar. Character spacing
/// inserts narrow spaces in the display layer: candidates are measured with the actual font, the choice is monotone, and the resulting
/// string is the very one the highlight, the marquee, and the auto-size all use, so both sides always agree.
/// </summary>
public partial class TaskBarMediaControl
{
    /// <summary>当前显示串使用的字距分隔符；为空表示没有字距。/ Separator used by the current display strings; null means no spacing.</summary>
    private string? _displaySeparator;

    /// <summary>字距分隔符的解析缓存（目标空隙、字号、字体、字重）；几何通道每次都会调用解析，因此比较不能分配字符串。
    /// Cache for the separator resolution (target gap, font size, family, weight); the resolver runs on every geometry pass, so the
    /// comparison must not allocate a string.</summary>
    private double _separatorCacheTarget = double.NaN;
    private double _separatorCacheFontSize = double.NaN;
    private string? _separatorCacheFamily;
    private FontWeight _separatorCacheWeight;

    /// <summary>解析缓存命中的分隔符。/ The separator held by the cache.</summary>
    private string? _separatorCacheSeparator;

    /// <summary>已用字距分隔符写进视觉树的显示串，供自动尺寸与跑马灯复用，避免每次重算。/ Display strings already written into the visual tree with the current separator, reused by auto-size and the marquee.</summary>
    private string _displayLyric = string.Empty;

    /// <summary>第二行歌词的显示串。/ Display string of the second lyric row.</summary>
    private string _displaySecondary = string.Empty;

    /// <summary>
    /// 按当前设置解析字距分隔符：目标空隙 = 字号 × 百分比。候选空格用当前字体逐一实测，取"不小于目标的最小者"，
    /// 滑杆因此单调且每一档都可见；字体缺这些字形时返回 null（退化为不插）。结果按（目标、字号、字体、字重）缓存。
    /// Resolves the character-spacing separator from the settings: the target gap is the font size times the percentage. Candidates are
    /// measured with the actual font and the smallest one not below the target wins, which keeps the slider monotone and every step
    /// visible; a font lacking the glyphs returns null (degrading to no spacing). The result is cached by target, size, family, weight.
    /// </summary>
    private string? ResolveCharacterSpacingSeparator()
    {
        var percent = LyricsCharacterSpacing.Normalize(SettingsManager.Current.LyricsCharacterSpacingPercent);
        if (percent <= 0)
        {
            return null;
        }

        var fontSize = SongLyrics.FontSize;
        if (!double.IsFinite(fontSize) || fontSize <= 0)
        {
            return null;
        }

        var target = fontSize * percent / 100.0;
        if (target == _separatorCacheTarget &&
            fontSize == _separatorCacheFontSize &&
            _separatorCacheWeight == SongLyrics.FontWeight &&
            string.Equals(SongLyrics.FontFamily.Source, _separatorCacheFamily, StringComparison.Ordinal))
        {
            return _separatorCacheSeparator;
        }

        var widths = new double[LyricsSpacingPolicy.SeparatorCandidates.Count];
        for (var index = 0; index < widths.Length; index++)
        {
            widths[index] = MeasureTextWidthExact(LyricsSpacingPolicy.SeparatorCandidates[index], SongLyrics);
        }

        _separatorCacheTarget = target;
        _separatorCacheFontSize = fontSize;
        _separatorCacheFamily = SongLyrics.FontFamily.Source;
        _separatorCacheWeight = SongLyrics.FontWeight;
        _separatorCacheSeparator = LyricsSpacingPolicy.ResolveSeparator(target, widths);
        return _separatorCacheSeparator;
    }

    /// <summary>
    /// 按当前可见状态重写两行歌词的显示串（原始文本保留在 <c>_activeLyric</c>、<c>_secondaryLyric</c> 里，
    /// 擦亮与跑马灯消费显示串），随后落一次行距布局。
    /// Rewrites both lyric rows' display strings for the current visibility (the raw texts stay in <c>_activeLyric</c> and
    /// <c>_secondaryLyric</c>, while the highlight and the marquee consume the display strings), then lands the row layout once.
    /// </summary>
    /// <param name="showLyrics">歌词面板是否可见。/ Whether the lyrics panel is visible.</param>
    /// <param name="showSecondary">第二行是否可见。/ Whether the second row is visible.</param>
    private void RefreshLyricDisplayTexts(bool showLyrics, bool showSecondary)
    {
        var separator = ResolveCharacterSpacingSeparator();
        _displaySeparator = separator;
        _displayLyric = showLyrics
            ? LyricsSpacingPolicy.ApplyCharacterSpacing(_activeLyric, separator)
            : string.Empty;
        _displaySecondary = showSecondary
            ? LyricsSpacingPolicy.ApplyCharacterSpacing(_secondaryLyric, separator)
            : string.Empty;
        SongLyrics.Text = _displayLyric;
        SongLyricsSecondary.Text = _displaySecondary;
        ApplyLyricRowLayout(showSecondary);
    }

    /// <summary>
    /// 几何或设置变化后的间距重算：字体、字号或可用高度变了就重选分隔符/重算行距，只有在结果真的变化时才写视觉树，
    /// 因此尺寸动画的每一帧与每次设置刷新都不会引起多余的布局失效。
    /// Recomputes spacing after a geometry or settings change: a different font, size, or available height re-picks the separator and
    /// re-derives the gap, and the visual tree is written only when the result actually changes, so neither a frame of a size animation
    /// nor a settings refresh causes redundant layout invalidation.
    /// </summary>
    private void ApplyLyricSpacing()
    {
        var showLyrics = SongLyricsPanel.Visibility == Visibility.Visible;
        var showSecondary = showLyrics && SongLyricsSecondaryContainer.Visibility == Visibility.Visible;
        var separator = showLyrics ? ResolveCharacterSpacingSeparator() : null;
        if (!string.Equals(separator, _displaySeparator, StringComparison.Ordinal))
        {
            // 字号或字体变了（例如厚度刻度动画、字号百分比），分隔符也得跟着换：重写显示串，窗口由紧随其后的
            // ApplyMarqueeLayout 重算。
            // The size or font changed (a thickness-scale animation, a font-size percentage), so the separator has to follow: rewrite the
            // display strings; the window is recomputed by the ApplyMarqueeLayout that always follows this pass.
            _displaySeparator = separator;
            _displayLyric = showLyrics
                ? LyricsSpacingPolicy.ApplyCharacterSpacing(_activeLyric, separator)
                : string.Empty;
            _displaySecondary = showSecondary
                ? LyricsSpacingPolicy.ApplyCharacterSpacing(_secondaryLyric, separator)
                : string.Empty;
            SongLyrics.Text = _displayLyric;
            SongLyricsSecondary.Text = _displaySecondary;
        }

        // 行高与行距每次几何通过都重新断言：布局引擎在尺寸动画的每一帧都会把容器高度写回"半个文字区"，
        // 只有紧跟其后的这一步能把行距再次落到网格上；依赖属性写入相同值时不会失效，因此重写是零成本的。
        // The row heights are asserted on every geometry pass: the layout engine writes the container height back to "half the text area" on
        // every frame of a size animation, and only this step, running right after it, can land the gap on the grid again; writing an
        // identical value to a dependency property never invalidates, so re-asserting costs nothing.
        ApplyLyricRowLayout(showSecondary);
    }

    /// <summary>
    /// 把双行行距落到三行网格上：单行跨三行（与原两行布局的居中效果一致），双行占第一、三行，间距行走中间。
    /// 行距为 0 时中间行高为 0，网格退化成原先的上下平分。
    /// Lands the two-line gap on the three-row grid: a single line spans all three rows (centred exactly as the old two-row layout was),
    /// two lines take the first and third rows, and the gap row sits in between. A zero gap makes the middle row zero-high and the grid
    /// degrades to the previous even split.
    /// </summary>
    /// <param name="showSecondary">第二行是否可见。/ Whether the second row is visible.</param>
    private void ApplyLyricRowLayout(bool showSecondary)
    {
        var (gap, lineHeight) = ResolveLyricLineSpacing(showSecondary);
        ApplyLyricRowLayout(showSecondary, gap, lineHeight);
    }

    /// <summary>按已求出的行距落网格；可用高度不可用时只改行跨度，不动高度。/ Lands the grid with a resolved gap; an unusable available height changes only the row span, never a height.</summary>
    /// <param name="showSecondary">第二行是否可见。/ Whether the second row is visible.</param>
    /// <param name="gap">已夹取的行距（DIP）。/ Clamped gap in DIP.</param>
    /// <param name="lineHeight">每行的可见高度（DIP）；0 表示高度不可用。/ Visible height of each row in DIP; zero means the height is unusable.</param>
    private void ApplyLyricRowLayout(bool showSecondary, double gap, double lineHeight)
    {
        Grid.SetRowSpan(SongLyricsContainer, showSecondary ? 1 : 3);
        SongLyricsGapRow.Height = new GridLength(showSecondary ? gap : 0);
        // 单行不动高度：容器跨三行并沿用布局引擎写的高度，居中结果与原布局完全一致。
        // A single line leaves the height alone: the container spans all three rows and keeps the height the layout engine wrote, which
        // centres it exactly like the previous layout.
        if (showSecondary && lineHeight > 0)
        {
            SongLyricsContainer.Height = lineHeight;
        }
    }

    /// <summary>
    /// 双行布局的行距与单行高度：可用高度取媒体文字区，请求值按"两行各留最低可读高度"夹取。
    /// Gap and per-line height of the two-line layout: the available height comes from the media-text area and the requested value is
    /// clamped so both lines keep a minimum readable height.
    /// </summary>
    /// <param name="showSecondary">第二行是否可见。/ Whether the second row is visible.</param>
    /// <returns>行距与每行高度；不可用时为 0。/ The gap and per-line height, or zeros when unavailable.</returns>
    private (double Gap, double LineHeight) ResolveLyricLineSpacing(bool showSecondary)
    {
        if (!showSecondary)
        {
            return (0, 0);
        }

        var available = SongInfoStackPanel.Height;
        if (!double.IsFinite(available) || available <= 0)
        {
            return (0, 0);
        }

        var requested = LyricsLineGap.Normalize(SettingsManager.Current.LyricsLineGapDip);
        var minimum = LyricsSpacingPolicy.ResolveMinimumLineHeightDip(SongLyrics.FontSize);
        var gap = LyricsSpacingPolicy.ResolveLineGapDip(requested, available, minimum);
        return (gap, LyricsSpacingPolicy.ResolveLineHeightDip(available, gap));
    }
}
