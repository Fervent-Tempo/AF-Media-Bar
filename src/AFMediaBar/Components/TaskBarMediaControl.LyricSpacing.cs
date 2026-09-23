using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>
/// 任务栏媒体控件的歌词间距：双行行距与横向字距。
/// Lyric spacing for the taskbar media control: the two-line gap and horizontal character spacing.
///
/// 行距由三行网格表达（行 / 行距 / 行），可用高度取自媒体文字区，并在"两行各留最低可读高度"的约束下夹取，
/// 因此调多大都不会把文字挤出任务栏。
/// The line gap is expressed by a three-row grid (line / gap / line); the available height comes from the media-text area and the gap
/// is clamped so both lines keep a minimum readable height, which is why no setting can push text out of the taskbar.
///
/// 字距是**连续的**：显示串在符合条件的位置带上 <see cref="LyricsSpacingPolicy.GapMarker"/>，渲染时每个标记被替换成一个
/// 空档 Run，其字号按"目标宽度 ÷ 实测宽度"缩放，于是空隙恰好等于目标宽度，而不是只在几个空格字符的固有宽度里挑一个。
/// 只缩不放，行高与基线不变；字距生效期间歌词行改用 Ideal 文本排版以获得亚像素字距（Display 会把推进量化到整设备像素），
/// 关闭字距时恢复 Display 并回到单 Run 的原始路径，默认外观逐像素不变。
/// Character spacing is **continuous**: the display string carries a <see cref="LyricsSpacingPolicy.GapMarker"/> at eligible positions,
/// and rendering replaces every marker with a spacer run whose font size is scaled by `target width / measured width`, so the gap equals
/// the target exactly instead of picking among the intrinsic widths of a few space characters. Scaling down only, which keeps the line
/// height and baseline unchanged. While spacing is active the lyric rows switch to Ideal text formatting so the advances can land on
/// sub-pixel positions (Display quantises them to whole device pixels); switching spacing off restores Display and the original
/// single-run path, so the default look stays pixel-identical.
///
/// 因为标记串与渲染用的 Inlines 是两套内容模型（`TextBlock.Text` 读不回 Inlines），跑马灯与测量改走本文件的读写助手：
/// 每次写入都记录实际内容，读回时给出记录值；宽度则由 <see cref="LyricsSpacingPolicy.MeasureSpacingAware"/> 按同一模型换算。
/// Markers and the Inlines used for rendering are two content models (`TextBlock.Text` does not read back Inlines), so the marquee and
/// the measurements go through the read/write helpers in this file: every write records the actual content and readers get that value,
/// while widths are converted by <see cref="LyricsSpacingPolicy.MeasureSpacingAware"/> in exactly the same model.
/// </summary>
public partial class TaskBarMediaControl
{
    /// <summary>第一行歌词当前的显示串（含标记），供自动尺寸与跑马灯复用。/ Current display string of the first lyric row (markers included), reused by auto-size and the marquee.</summary>
    private string _displayLyric = string.Empty;

    /// <summary>第二行歌词的显示串。/ Display string of the second lyric row.</summary>
    private string _displaySecondary = string.Empty;

    /// <summary>第一行元素上实际写入的内容（显示串本身或它的跑马灯窗口）。/ Content actually written onto the first row (the display string or its marquee window).</summary>
    private string? _trackedLyricText;

    /// <summary>第二行元素上实际写入的内容。/ Content actually written onto the second row.</summary>
    private string? _trackedSecondaryText;

    /// <summary>上一次应用的锚定空档，用于判断是否需要重建 Inlines。/ The spacer applied last, used to decide whether the inlines have to be rebuilt.</summary>
    private LyricsSpacingPolicy.Spacer? _appliedSpacer;

    /// <summary>空档解析缓存（目标宽度、字号、字体、字重）；几何通道每次都会解析，比较不能分配字符串。
    /// Spacer-resolution cache (target width, font size, family, weight); the geometry pass resolves every time, so the comparison must
    /// not allocate a string.</summary>
    private bool _spacerCacheValid;
    private double _spacerCacheTarget = double.NaN;
    private double _spacerCacheFontSize = double.NaN;
    private string? _spacerCacheFamily;
    private FontWeight _spacerCacheWeight;
    private LyricsSpacingPolicy.Spacer? _spacerCacheValue;

    /// <summary>该元素是否是受字距影响的歌词行（两行都算；高亮层由第一行同步写入，不单独跟踪）。/ Whether the element is a lyric row affected by spacing (either row; the highlight layer is written alongside the first row and is not tracked separately).</summary>
    private bool IsLyricSpacingText(TextBlock element) =>
        ReferenceEquals(element, SongLyrics) || ReferenceEquals(element, SongLyricsSecondary);

    /// <summary>
    /// 读回一个元素的当前内容：歌词行走记录值（Inlines 下 `Text` 是过期的），其余元素直接用 `Text`。
    /// Reads back one element's current content: lyric rows use the recorded value (under Inlines the `Text` property is stale), every
    /// other element reads `Text` directly.
    /// </summary>
    /// <param name="element">文本元素。/ Text element.</param>
    private string GetMarqueeContent(TextBlock element)
    {
        if (ReferenceEquals(element, SongLyrics))
        {
            return _trackedLyricText ?? element.Text ?? string.Empty;
        }

        if (ReferenceEquals(element, SongLyricsSecondary))
        {
            return _trackedSecondaryText ?? element.Text ?? string.Empty;
        }

        return element.Text ?? string.Empty;
    }

    /// <summary>
    /// 写入一个元素的当前内容：歌词行同时记录值并按当前设置重建渲染，其余元素直接写 `Text`。
    /// Writes one element's current content: lyric rows also record it and rebuild their rendering for the current settings, every
    /// other element gets a plain `Text` write.
    /// </summary>
    /// <param name="element">文本元素。/ Text element.</param>
    /// <param name="text">要显示的内容。/ Content to display.</param>
    private void SetMarqueeContent(TextBlock element, string text)
    {
        if (ReferenceEquals(element, SongLyrics))
        {
            _trackedLyricText = text;
            ApplyLyricElementContent(element, text);
            // 高亮层与底色层必须永远同文：跑马灯写的是窗口，高亮层若停在整行，亮区就会与字形错位。
            // The highlight layer has to carry the same content as the base layer at all times: the marquee writes a window, and a
            // highlight layer left on the whole line would drift away from the glyphs.
            ApplyLyricElementContent(SongLyricsHighlight, text);
            return;
        }

        if (ReferenceEquals(element, SongLyricsSecondary))
        {
            _trackedSecondaryText = text;
            ApplyLyricElementContent(element, text);
            return;
        }

        element.Text = text;
    }

    /// <summary>
    /// 把一个字符串按当前设置写成一个元素的渲染内容：有字距标记时构建"文本段 + 缩放空档 Run"的 Inlines 并切到 Ideal，
    /// 否则走单 Run 的原始路径并保持 Display。
    /// Writes one string as an element's rendered content for the current settings: with gap markers it builds inlines of text segments
    /// plus scaled spacer runs and switches to Ideal, otherwise it takes the original single-run path and keeps Display.
    /// </summary>
    /// <param name="element">文本元素。/ Text element.</param>
    /// <param name="text">显示串（可含标记）。/ Display string, possibly carrying markers.</param>
    private void ApplyLyricElementContent(TextBlock element, string text)
    {
        var spacer = ResolveLyricSpacer();
        if (spacer is not { } resolved || text.IndexOf(LyricsSpacingPolicy.GapMarker) < 0)
        {
            TextOptions.SetTextFormattingMode(element, TextFormattingMode.Display);
            element.Text = text;
            return;
        }

        TextOptions.SetTextFormattingMode(element, TextFormattingMode.Ideal);
        var baseFontSize = element.FontSize;
        if (!double.IsFinite(baseFontSize) || baseFontSize <= 0)
        {
            baseFontSize = 12;
        }

        // 只缩不放：空档 Run 永远不大于基准字号，行高与基线因此与原渲染完全一致。
        // Scaling down only: the spacer run never exceeds the base font size, so line height and baseline match the original rendering.
        var spacerFontSize = baseFontSize * resolved.Scale;
        element.Inlines.Clear();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != LyricsSpacingPolicy.GapMarker)
            {
                continue;
            }

            if (index > start)
            {
                element.Inlines.Add(new Run(text[start..index]));
            }

            element.Inlines.Add(new Run(resolved.Glyph) { FontSize = spacerFontSize });
            start = index + 1;
        }

        if (start < text.Length)
        {
            element.Inlines.Add(new Run(text[start..]));
        }
    }

    /// <summary>
    /// 按当前设置解析锚定空档：目标空隙 = 字号 × 百分比；候选用当前字体逐一实测，取"不小于目标的最小者"并把它的字号缩放到
    /// 恰好等于目标。结果按（目标、字号、字体、字重）缓存，比较不分配字符串。
    /// Resolves the anchor spacer from the settings: the target gap is the font size times the percentage; candidates are measured with
    /// the actual font and the smallest one not below the target wins, its font size scaled to land exactly on the target. The result is
    /// cached by target, size, family, and weight, and the comparison allocates nothing.
    /// </summary>
    private LyricsSpacingPolicy.Spacer? ResolveLyricSpacer()
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
        if (_spacerCacheValid &&
            target == _spacerCacheTarget &&
            fontSize == _spacerCacheFontSize &&
            _spacerCacheWeight == SongLyrics.FontWeight &&
            string.Equals(SongLyrics.FontFamily.Source, _spacerCacheFamily, StringComparison.Ordinal))
        {
            return _spacerCacheValue;
        }

        var widths = new double[LyricsSpacingPolicy.SpacerCandidates.Count];
        for (var index = 0; index < widths.Length; index++)
        {
            widths[index] = MeasureTextWidthExact(LyricsSpacingPolicy.SpacerCandidates[index], SongLyrics);
        }

        _spacerCacheValid = true;
        _spacerCacheTarget = target;
        _spacerCacheFontSize = fontSize;
        _spacerCacheFamily = SongLyrics.FontFamily.Source;
        _spacerCacheWeight = SongLyrics.FontWeight;
        _spacerCacheValue = LyricsSpacingPolicy.ResolveSpacer(target, widths);
        return _spacerCacheValue;
    }

    /// <summary>当前字距的目标空隙（DIP）；未启用或字号不可用时为 0。/ The current target gap in DIP, or zero when spacing is off or the font size is unusable.</summary>
    private double ResolveCharacterSpacingDip()
    {
        var percent = LyricsCharacterSpacing.Normalize(SettingsManager.Current.LyricsCharacterSpacingPercent);
        if (percent <= 0)
        {
            return 0;
        }

        var fontSize = SongLyrics.FontSize;
        return double.IsFinite(fontSize) && fontSize > 0 ? fontSize * percent / 100.0 : 0;
    }

    /// <summary>
    /// 间距感知的测宽：歌词行按"分段实测 + 标记数 × 目标空隙"换算（与渲染严格同构），其余元素仍是普通精确测量。
    /// Spacing-aware measurement: lyric rows convert as "segments plus markers times the target gap" (exactly how rendering lays out),
    /// while every other element keeps the plain exact measurement.
    /// </summary>
    /// <param name="text">显示串（可含标记）。/ Display string, possibly carrying markers.</param>
    /// <param name="element">测量所用的文本元素（决定字体）。/ Text element the measurement uses (it decides the font).</param>
    private double MeasureMarqueeWidth(string? text, TextBlock element)
    {
        var gap = IsLyricSpacingText(element) ? ResolveCharacterSpacingDip() : 0;
        if (gap <= 0)
        {
            return MeasureTextWidthExact(text ?? string.Empty, element);
        }

        return LyricsSpacingPolicy.MeasureSpacingAware(text, gap, segment => MeasureTextWidthExact(segment, element));
    }

    /// <summary>
    /// 自动尺寸用的测宽：歌词行走间距感知测量并加上跑马灯为折返预留的 4 DIP，其余元素与原来完全一致。
    /// Auto-size measurement: lyric rows use the spacing-aware width plus the four DIP the marquee reserves for its turn-around;
    /// every other element behaves exactly as before.
    /// </summary>
    /// <param name="text">显示串（可含标记）。/ Display string, possibly carrying markers.</param>
    /// <param name="element">测量所用的文本元素。/ Text element the measurement uses.</param>
    private double MeasureAutoSizeWidth(string? text, TextBlock element) =>
        IsLyricSpacingText(element)
            ? MeasureMarqueeWidth(text, element) + 4
            : MeasureTextWidth(text ?? string.Empty, element);

    /// <summary>
    /// 按当前可见状态重写两行歌词的显示串（原始文本保留在 <c>_activeLyric</c>、<c>_secondaryLyric</c> 里），
    /// 随后落一次行距布局。
    /// Rewrites both lyric rows' display strings for the current visibility (the raw texts stay in <c>_activeLyric</c> and
    /// <c>_secondaryLyric</c>), then lands the row layout once.
    /// </summary>
    /// <param name="showLyrics">歌词面板是否可见。/ Whether the lyrics panel is visible.</param>
    /// <param name="showSecondary">第二行是否可见。/ Whether the second row is visible.</param>
    private void RefreshLyricDisplayTexts(bool showLyrics, bool showSecondary)
    {
        var spacing = showLyrics && ResolveCharacterSpacingDip() > 0;
        _displayLyric = showLyrics
            ? spacing ? LyricsSpacingPolicy.ApplyCharacterSpacing(_activeLyric) : _activeLyric
            : string.Empty;
        _displaySecondary = showSecondary
            ? spacing ? LyricsSpacingPolicy.ApplyCharacterSpacing(_secondaryLyric) : _secondaryLyric
            : string.Empty;
        _appliedSpacer = showLyrics ? ResolveLyricSpacer() : null;
        // 高亮层由 SetMarqueeContent 与底色层同步写入（Inlines 下 Text 绑定读不回），这里不再单独写一次。
        // The highlight layer is written together with the base layer inside SetMarqueeContent (the Text binding cannot mirror inlines),
        // so it is not written separately here.
        SetMarqueeContent(SongLyrics, _displayLyric);
        SetMarqueeContent(SongLyricsSecondary, _displaySecondary);
        ApplyLyricRowLayout(showSecondary);
    }

    /// <summary>
    /// 几何或设置变化后的间距重算：字号、字体或百分比变了就重选空档并重建 Inlines，行距每次几何通过都重新断言
    /// （布局引擎在尺寸动画的每一帧都会把容器高度写回"半个文字区"；依赖属性写入相同值不会失效，因此重写是零成本的）。
    /// Recomputes spacing after a geometry or settings change: a different font, size, or percentage re-picks the spacer and rebuilds the
    /// inlines, while the row layout is asserted on every geometry pass (the layout engine writes the container height back to "half the
    /// text area" on every frame of a size animation; writing an identical value to a dependency property never invalidates, so
    /// re-asserting costs nothing).
    /// </summary>
    private void ApplyLyricSpacing()
    {
        var showLyrics = SongLyricsPanel.Visibility == Visibility.Visible;
        var showSecondary = showLyrics && SongLyricsSecondaryContainer.Visibility == Visibility.Visible;
        var spacer = showLyrics ? ResolveLyricSpacer() : null;
        if (_appliedSpacer != spacer)
        {
            var markersChanged = (_appliedSpacer is null) != (spacer is null);
            _appliedSpacer = spacer;
            if (markersChanged)
            {
                // 标记的"有/无"变了：显示串本身必须重构（会顶掉跑马灯窗口，下一次推进立即恢复），行距布局一并落好。
                // Whether markers exist changed: the display strings themselves have to be rebuilt (this replaces a marquee window, which
                // the next advance restores), and the row layout lands along with it.
                RefreshLyricDisplayTexts(showLyrics, showSecondary);
            }
            else
            {
                // 只有缩放比变了（字号、字体或百分比）：内容不动，只重建渲染，避免打断正在滚动的窗口。
                // Only the scale changed (size, family, or percentage): rebuild the rendering without touching the content, so an advancing
                // window is not interrupted.
                if (_trackedLyricText is { } lyric)
                {
                    SetMarqueeContent(SongLyrics, lyric);
                }

                if (_trackedSecondaryText is { } secondary)
                {
                    SetMarqueeContent(SongLyricsSecondary, secondary);
                }

                ApplyLyricRowLayout(showSecondary);
            }

            // 字距改变文字宽度：主动重发尺寸请求。暂停中的播放器不会送来新快照，若不在这里发布，媒体栏会停在旧长度上，
            // 更宽的字距就被省略号截断。
            // Spacing changes the text width, so the size request is republished here. A paused player sends no new snapshot, and without
            // this publication the bar would keep its old length and the wider spacing would be cut off by the ellipsis.
            RaiseDesiredSizeChanged();
            return;
        }

        ApplyLyricRowLayout(showSecondary);
    }

    /// <summary>
    /// 把双行行距落到五行网格上：上留白 / 第一行 / 行距 / 第二行 / 下留白。两行文本框之间的距离就是设置值，
    /// 因此 0 表示两行真正贴紧；整个两行块在文字区里垂直居中（数学上与原版"各占一半并居中"在默认值下等价）。
    /// 单行时只占中间一行，其余留白把它居中——与原版单行的居中结果一致。
    /// Lands the two-line gap on a five-row grid: top lead / first line / gap / second line / bottom lead. The distance between the two
    /// line boxes is exactly the setting, so zero really means the two lines touch; the whole block stays vertically centred in the
    /// media-text area (which is mathematically identical to the previous "one half each, centred" layout at its default value). A single
    /// line occupies the middle row only, with the leads centring it, matching the previous single-line centring.
    /// </summary>
    /// <param name="showSecondary">第二行是否可见。/ Whether the second row is visible.</param>
    private void ApplyLyricRowLayout(bool showSecondary)
    {
        var available = SongInfoStackPanel.Height;
        if (!double.IsFinite(available) || available <= 0)
        {
            // 高度不可用：只把网格退化成"中间一行"，不写任何高度。
            // Without a usable height the grid degrades to a single middle row and no height is written at all.
            SetLyricRowHeights(0, 1, 0, 0, 0, GridUnitType.Star);
            return;
        }

        if (!showSecondary)
        {
            var primary = SongLyricsContainer.Height;
            if (!double.IsFinite(primary) || primary <= 0)
            {
                primary = available / 2;
            }

            var lead = Math.Max(0, (available - primary) / 2);
            SetLyricRowHeights(lead, primary, 0, 0, lead, GridUnitType.Pixel);
            SongLyricsContainer.Height = primary;
            return;
        }

        var lineHeight = ResolveLyricLineHeight();
        var requested = LyricsLineGap.Normalize(SettingsManager.Current.LyricsLineGapDip);
        var minimum = LyricsSpacingPolicy.ResolveMinimumLineHeightDip(SongLyrics.FontSize);
        var gap = LyricsSpacingPolicy.ResolveLineGapDip(requested, available, minimum);
        var blockLead = LyricsSpacingPolicy.ResolveTightLeadDip(available, lineHeight, gap);
        SetLyricRowHeights(blockLead, lineHeight, gap, lineHeight, blockLead, GridUnitType.Pixel);
        SongLyricsContainer.Height = lineHeight;
    }

    /// <summary>一次写入五行的行高；中间行的单位可指定（排版前用星号行兜底）。/ Writes all five row heights in one go; the middle line's unit is selectable (a star row backstops the pre-layout state).</summary>
    private void SetLyricRowHeights(double topLead, double primary, double gap, double secondary, double bottomLead, GridUnitType primaryUnit)
    {
        SongLyricsTopSpacer.Height = new GridLength(topLead);
        SongLyricsPrimaryRow.Height = new GridLength(primary, primaryUnit);
        SongLyricsGapRow.Height = new GridLength(gap);
        SongLyricsSecondaryRow.Height = new GridLength(secondary);
        SongLyricsBottomSpacer.Height = new GridLength(bottomLead);
    }

    /// <summary>
    /// 单行的实际行框高度：优先取渲染后的 <c>ActualHeight</c>（Display 与 Ideal 两种排版下都准确），
    /// 首帧尚未排版时按字号估算。行高只受字体影响，不受行距布局影响，因此不会与行高写入形成反馈。
    /// The rendered line-box height: the post-layout <c>ActualHeight</c> when available (accurate under both Display and Ideal
    /// formatting), otherwise an estimate from the font size. The line height depends only on the font, never on the row heights written
    /// here, so there is no feedback loop.
    /// </summary>
    private double ResolveLyricLineHeight()
    {
        var actual = SongLyrics.ActualHeight;
        if (double.IsFinite(actual) && actual > 0)
        {
            return actual;
        }

        var fontSize = SongLyrics.FontSize;
        return double.IsFinite(fontSize) && fontSize > 0 ? fontSize * 1.2 : 14;
    }
}
