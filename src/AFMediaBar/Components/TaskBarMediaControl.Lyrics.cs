using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>
/// 任务栏媒体控件的逐字擦亮：当前行随播放位置从左向右亮起。
/// Syllable highlighting for the taskbar media control: the active line lights up from left to right with playback.
///
/// 擦亮只写高亮层的裁剪矩形，不改文本、不改布局、不改尺寸请求，因此跑马灯宽度、自动尺寸指纹与字号缩放都不受影响；
/// 用户关闭擦亮或系统进入高对比度时只隐藏这一层，歌词时间轴与跟随滚动继续沿用同一轨迹。没有可用时间轴、暂停、断开或动效降级时
/// 才停止整条时间轴。
/// The highlight only writes the clip rectangle of the highlight layer and never touches text, layout, or the size request, so
/// the marquee width, the auto-size fingerprint, and the font scaling stay unaffected. Turning the highlight off or entering high
/// contrast only hides this layer; the lyric timeline and follow scroll keep the same trajectory. The whole timeline stops only when
/// no usable timing is available, playback is paused or disconnected, or motion is reduced.
/// </summary>
public partial class TaskBarMediaControl
{
    /// <summary>擦亮帧间隔：约 30 帧每秒，与歌词行内的时间精度相称。
    /// Highlight frame interval: about 30 frames per second, matching the timing precision inside a lyric line.</summary>
    private const int LyricHighlightFrameIntervalMilliseconds = 33;

    /// <summary>启用擦亮时底色层的不透明度：同一支前景色压暗，形成"已唱/未唱"的对比；数值取自设置，用户可在歌词页调整。
    /// Opacity of the base layer while highlighting is active: the same foreground dimmed, which contrasts sung and unsung text.
    /// The value comes from the settings and is adjustable on the lyrics page.</summary>
    private static double LyricHighlightBaseOpacity => SettingsManager.Current.LyricsUnsungOpacityPercent / 100d;

    /// <summary>裁剪宽度小于该值时不显示高亮层，避免行首出现一条几乎没有宽度的杂线。
    /// Below this clip width the highlight layer stays hidden, which keeps a hair-width line from appearing at the line start.</summary>
    private const double LyricHighlightMinimumWidth = 0.5;

    /// <summary>裁剪矩形在容器高度不可用时的兜底高度。/ Fallback clip height used when the container height is unavailable.</summary>
    private const double LyricHighlightFallbackHeight = 19;

    private readonly DispatcherTimer _lyricHighlightTimer;
    private LyricLine? _currentLyricLine;
    private LyricLine? _measuredLyricLine;
    private double _measuredLyricFontSize = double.NaN;
    private double _measuredLyricAvailableWidth = double.NaN;
    private string _measuredLyricText = string.Empty;
    private FontFamily? _measuredLyricFontFamily;
    private FontWeight _measuredLyricFontWeight;
    private double _measuredLyricGap = double.NaN;
    private double _activeLyricTextWidth;
    private bool _lyricTimelineActive;

    /// <summary>
    /// 记录当前行，供逐字擦亮读取；换行时立即收起旧擦亮。
    /// Records the active line for syllable highlighting and hides the previous reveal immediately on a line change.
    /// </summary>
    /// <param name="line">当前行；位置早于第一行时为 null / Active line, null before the first line.</param>
    private void SetCurrentLyricLine(LyricLine? line)
    {
        if (ReferenceEquals(_currentLyricLine, line))
        {
            return;
        }

        _currentLyricLine = line;
        _measuredLyricLine = null;
        // 换行必须先收起：新行的第一帧到来之前不能让上一行的亮区留在屏幕上。
        // A line change has to hide the layer first, so the previous line's reveal cannot stay on screen before the first frame
        // of the new one arrives.
        SongLyricsHighlightClip.Rect = new Rect(0, 0, 0, ResolveLyricClipHeight());
        SongLyricsHighlight.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 按当前状态刷新歌词时间轴与擦亮层。时间轴状态决定跟随滚动，用户开关只改变擦亮层，不重置滚动位置。
    /// Refreshes the lyric timeline and highlight layer for the current state. The timeline state decides follow scrolling, while the
    /// user switch changes only the highlight layer and never resets the scroll position.
    /// </summary>
    private void RefreshLyricHighlightPresentation()
    {
        var presentation = ResolveLyricHighlightPresentation();
        if (!presentation.AdvanceTimeline || ResolveCurrentLyricProgress() is null)
        {
            StopLyricHighlight();
            return;
        }

        var wasTimelineActive = _lyricTimelineActive;
        _lyricTimelineActive = true;
        if (!wasTimelineActive)
        {
            // 只有时间轴真的开始时才切到跟随式；仅开关擦亮层不会改写跑马灯 key 或回到行首。
            // Switch to follow mode only when the timeline actually starts. Toggling just the highlight layer does not rewrite the
            // marquee key or send the line back to its beginning.
            ReapplyLyricMarquee();
        }

        if (!presentation.ShowHighlight)
        {
            _lyricHighlightTimer.Stop();
            HideLyricHighlightLayer();
            SongLyrics.Opacity = 1;
            return;
        }

        SongLyrics.Opacity = LyricHighlightBaseOpacity;
        if (!_lyricHighlightTimer.IsEnabled)
            _lyricHighlightTimer.Start();
        AdvanceLyricHighlight();
    }

    /// <summary>
    /// 关闭擦亮并还原外观。
    /// Turns highlighting off and restores the appearance.
    /// </summary>
    private void StopLyricHighlight()
    {
        var wasActive = _lyricTimelineActive;
        _lyricHighlightTimer.Stop();
        _lyricTimelineActive = false;
        _measuredLyricLine = null;
        HideLyricHighlightLayer();
        SongLyrics.Opacity = 1;
        // 时间轴停止后这一行才离开跟随式；仅隐藏擦亮层不会走到这里，也不会改动滚动轨迹。
        // The line leaves follow mode only when its timeline stops. Merely hiding the highlight layer never comes through here and never
        // changes the scrolling trajectory.
        if (wasActive)
            ReapplyLyricMarquee();
    }

    /// <summary>
    /// 分别求解时间轴推进与擦亮层可见性。开关和高对比度只影响视觉层；暂停、不可见、后台剪枝与减少动效才停止时间轴。
    /// Separately resolves timeline advancement and highlight visibility. The switch and high contrast affect only the visual layer;
    /// pausing, invisibility, background pruning, and reduced motion stop the timeline itself.
    /// </summary>
    private LyricHighlightPolicy.PresentationState ResolveLyricHighlightPresentation() =>
        LyricHighlightPolicy.ResolvePresentationState(
            highlightEnabled: SettingsManager.Current.LyricsSyllableHighlightEnabled,
            hasCurrentLine: _currentLyricLine is not null,
            connected: _snapshot.IsConnected,
            playing: _snapshot.IsPlaying,
            lyricsVisible: SongLyricsPanel.Visibility == Visibility.Visible,
            controlVisible: IsVisible,
            isAdvancePruned: IsAdvancePruned,
            highContrast: SystemParameters.HighContrast,
            useContinuousMotion: CurrentMotion.UseContinuousMotion);

    private void AdvanceLyricHighlight()
    {
        var presentation = ResolveLyricHighlightPresentation();
        if (!presentation.AdvanceTimeline)
        {
            StopLyricHighlight();
            return;
        }

        var line = _currentLyricLine!;
        var position = TaskbarExperiencePolicy.GetPosition(_snapshot, DateTimeOffset.UtcNow);
        var progress = LyricHighlightPolicy.ResolveProgress(line, position);
        if (progress is null)
        {
            StopLyricHighlight();
            return;
        }

        if (!presentation.ShowHighlight)
        {
            _lyricHighlightTimer.Stop();
            HideLyricHighlightLayer();
            SongLyrics.Opacity = 1;
            return;
        }

        // 跟随式：这一行放不下，窗口正在跟着擦亮边界移动，因此窗口里的已唱段就是它的前缀，
        // 裁剪宽度直接取"窗口内前若干个字符的精确宽度"（含小数插值），不需要按整行比例折算。
        // Follow mode: this line does not fit and the window follows the reveal edge, so the sung run is a prefix of the window and the
        // clip width is simply the exact width of its leading characters, interpolated, instead of a fraction of the whole line.
        if (TryGetFollowWindow(out var sungWidth, out var windowWidth, out _, out _))
        {
            // 亮区最远只到容器右边缘：窗口比容器宽（尾部字更宽）时，按字符数换算出来的宽度可能超过可用宽度，
            // 那会让高亮画到显示范围之外。
            // The reveal never goes past the container's right edge: when the window is wider than its container (wider tail characters),
            // converting from a character count can exceed the available width, which would paint the highlight outside the visible area.
            var visibleWidth = double.IsFinite(SongLyrics.Width) ? Math.Min(sungWidth, SongLyrics.Width) : sungWidth;
            if (visibleWidth < LyricHighlightMinimumWidth)
            {
                SongLyricsHighlight.Visibility = Visibility.Collapsed;
                SongLyricsHighlightClip.Rect = new Rect(0, 0, 0, ResolveLyricClipHeight());
                return;
            }

            var followLeft = ResolveFollowInset(SongLyrics.TextAlignment, windowWidth);
            SongLyricsHighlightClip.Rect = new Rect(followLeft, 0, visibleWidth, ResolveLyricClipHeight());
            SongLyricsHighlight.Visibility = Visibility.Visible;
            return;
        }

        EnsureLyricTextWidth(line);
        var width = LyricHighlightPolicy.ResolveClipWidth(progress.Value, _activeLyricTextWidth);
        if (width < LyricHighlightMinimumWidth)
        {
            SongLyricsHighlight.Visibility = Visibility.Collapsed;
            SongLyricsHighlightClip.Rect = new Rect(0, 0, 0, ResolveLyricClipHeight());
            return;
        }

        // 裁剪矩形在文本自己的坐标系里，元素之外的位移（例如 SongInfoStackPanel 的入场动画）会带着两层一起走，
        // 亮区与字形始终对齐；跑马灯不再移动元素，它改写的是文字本身。
        // The clip rectangle lives in the text's own coordinate space, so a transform outside the element (such as the entrance
        // animation on SongInfoStackPanel) carries both layers along and the reveal stays aligned with the glyphs. The marquee no
        // longer moves the element: it rewrites the text itself.
        //
        // 居中或右对齐时字形本身有左内缩，裁剪必须从字形起点开始，否则短句的擦亮会整体偏左。
        // Centred or right-aligned text insets the glyph run, and the clip has to start where the glyphs do; otherwise the reveal
        // of a short line sits too far to the left.
        var left = ResolveLyricInset(SongLyrics.TextAlignment);
        SongLyricsHighlightClip.Rect = new Rect(left, 0, width, ResolveLyricClipHeight());
        SongLyricsHighlight.Visibility = Visibility.Visible;
    }

    /// <summary>只隐藏擦亮视觉层，不改变时间轴或跑马灯状态。/ Hides only the reveal layer without changing the timeline or marquee state.</summary>
    private void HideLyricHighlightLayer()
    {
        SongLyricsHighlightClip.Rect = new Rect(0, 0, 0, ResolveLyricClipHeight());
        SongLyricsHighlight.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 当前歌词行已经唱到的比例（0–1），供跑马灯判断"擦亮是否贴近右边界"。没有音节时间轴或位置不可解时返回 null。
    /// Reveal progress of the active lyric line (0–1), which the marquee uses to decide whether the reveal has reached the right margin.
    /// Returns null without a syllable timeline or when the position cannot be resolved.
    /// </summary>
    private double? ResolveCurrentLyricProgress()
    {
        if (_currentLyricLine is not { } line)
            return null;

        var position = TaskbarExperiencePolicy.GetPosition(_snapshot, DateTimeOffset.UtcNow);
        return LyricHighlightPolicy.ResolveProgress(line, position);
    }

    /// <summary>
    /// 让跑马灯按"这一行现在有没有推进中的歌词时间轴"重新决策。擦亮开关不参与该决策。
    /// Lets the marquee re-decide whether this line has an advancing lyric timeline. The highlight switch does not participate.
    /// </summary>
    private void ReapplyLyricMarquee() => ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));

    private void EnsureLyricTextWidth(LyricLine line)
    {
        var fontSize = SongLyrics.FontSize;
        var availableWidth = double.IsFinite(SongLyrics.Width) ? SongLyrics.Width : double.NaN;
        // 显示串也是缓存键：改字距不会换行，但会改写元素的内容，不重测就会拿着旧宽度去擦亮（亮区与字形错位）。
        // 歌词行读记录值——Inlines 下 `Text` 读不回实际内容；宽度按间距感知模型换算，与渲染严格同构；
        // 字距本身也在键里，因为同一个显示串在不同字距下的渲染宽度不同。
        // The display string is part of the cache key as well: changing the character spacing keeps the same line but rewrites the
        // element's content, and without a re-measure the reveal would use the old width and drift away from the glyphs. Lyric rows read
        // the recorded value (under inlines `Text` does not read back), and the width converts in the spacing-aware model so it matches
        // rendering exactly; the gap itself belongs to the key too, because one display string renders at different widths per gap.
        var text = GetMarqueeContent(SongLyrics);
        var gap = ResolveCharacterSpacingDip();
        if (ReferenceEquals(_measuredLyricLine, line) &&
            Math.Abs(_measuredLyricFontSize - fontSize) < 0.01 &&
            string.Equals(_measuredLyricText, text, StringComparison.Ordinal) &&
            Equals(_measuredLyricFontFamily, SongLyrics.FontFamily) &&
            _measuredLyricFontWeight == SongLyrics.FontWeight &&
            Math.Abs(_measuredLyricGap - gap) < 0.001 &&
            (double.IsNaN(availableWidth) && double.IsNaN(_measuredLyricAvailableWidth) ||
             Math.Abs(_measuredLyricAvailableWidth - availableWidth) < 0.01))
        {
            return;
        }

        _measuredLyricLine = line;
        _measuredLyricFontSize = fontSize;
        _measuredLyricAvailableWidth = availableWidth;
        _measuredLyricText = text;
        _measuredLyricFontFamily = SongLyrics.FontFamily;
        _measuredLyricFontWeight = SongLyrics.FontWeight;
        _measuredLyricGap = gap;
        _activeLyricTextWidth = MeasureMarqueeWidth(text, SongLyrics);
    }

    /// <summary>
    /// 计算字形起点相对文本元素左边缘的内缩。
    /// Computes how far the glyph run is inset from the text element's left edge.
    /// </summary>
    private double ResolveLyricInset(TextAlignment alignment)
    {
        var availableWidth = double.IsFinite(SongLyrics.Width) ? SongLyrics.Width : _activeLyricTextWidth;
        var slack = Math.Max(0, availableWidth - _activeLyricTextWidth);
        return alignment switch
        {
            TextAlignment.Center => slack / 2,
            TextAlignment.Right => slack,
            _ => 0
        };
    }

    /// <summary>
    /// 跟随式窗口的内缩：窗口几乎总是比容器宽（起点为 0），只有在整行唱完、窗口滑到末尾而尾巴比容器短时才有留白，
    /// 因此这里用窗口自己的宽度换算，而不是用整行的宽度。
    /// Inset of a follow window: the window is almost always wider than its container (which means a zero inset), and only once the
    /// whole line has been sung and the window has slid to a tail shorter than the container does any slack appear, so this converts
    /// from the window's own width rather than from the whole line's.
    /// </summary>
    /// <param name="alignment">该行的对齐方式。/ Alignment of the line.</param>
    /// <param name="windowWidth">窗口整体宽度（DIP）。/ Total window width in DIP.</param>
    private double ResolveFollowInset(TextAlignment alignment, double windowWidth)
    {
        if (!double.IsFinite(SongLyrics.Width) || !double.IsFinite(windowWidth))
            return 0;

        var slack = Math.Max(0, SongLyrics.Width - windowWidth);
        return alignment switch
        {
            TextAlignment.Center => slack / 2,
            TextAlignment.Right => slack,
            _ => 0
        };
    }

    private double ResolveLyricClipHeight()
    {
        var height = SongLyricsHighlight.ActualHeight;
        if (!double.IsFinite(height) || height <= 0)
        {
            height = SongLyricsContainer.ActualHeight;
        }

        return double.IsFinite(height) && height > 0 ? height : LyricHighlightFallbackHeight;
    }
}
