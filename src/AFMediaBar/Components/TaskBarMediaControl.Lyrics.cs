using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.Web.WebView2.Wpf;

namespace AFMediaBar.Components;

/// <summary>
/// 任务栏媒体控件的 Web 歌词桥。WPF 只负责宿主尺寸与可见性，歌词排版、切行和真实逐字动画均由 Web 端完成。
/// Web lyrics bridge for the taskbar media control. WPF owns only host sizing and visibility; the web side owns typography,
/// line transitions, and genuine syllable-timed animation.
/// </summary>
public partial class TaskBarMediaControl
{
    private static readonly TimeSpan LyricsFrameInterval = TimeSpan.FromMilliseconds(50);

    private readonly DispatcherTimer _lyricsWebTimer = new(DispatcherPriority.Render)
    {
        Interval = LyricsFrameInterval
    };
    private LyricsWebViewRenderer? _lyricsWebRenderer;
    private LyricsPresentationFrame _lyricsFrame = LyricsPresentationFrame.Hidden;
    private Brush _lyricsForeground = Brushes.White;
    private bool _lyricsNeedsContrastShadow;
    private bool _lyricsUsesLightText = true;

    /// <summary>上一次尺寸请求所用的歌词文本，用来在换行时立即重新发布尺寸（见 UpdateWebLyricsPresentation）。
    /// Lyric texts used by the last size request, so a line change can republish the size immediately (see UpdateWebLyricsPresentation).</summary>
    private string _lastSizeLyricText = string.Empty;
    private string _lastSizeSecondaryText = string.Empty;

    /// <summary>已经为 WebView2 图形层故障重建过几次歌词视图（见 RecoverWebLyricsGraphics）。
    /// How many times the lyric view has been rebuilt after a WebView2 graphics fault (see RecoverWebLyricsGraphics).</summary>
    private int _lyricsGraphicsRecoveries;

    /// <summary>重建预算用尽后置位：停用 Web 歌词、退回元数据，避免同一缺陷反复击穿应用。
    /// Set once the rebuild budget is spent: the web lyrics stay off and the bar falls back to metadata so the same defect cannot take the app down again.</summary>
    private bool _webLyricsGraphicsDisabled;

    private void InitializeWebLyrics()
    {
        _lyricsWebRenderer = new LyricsWebViewRenderer(LyricsWebView);
        _lyricsWebRenderer.Ready += OnWebLyricsReady;
        _lyricsWebTimer.Tick += (_, _) => UpdateWebLyricsPresentation();
        Loaded += OnWebLyricsLoaded;
        WebLyricsGraphicsRecovery.RecoveryRequested += OnWebLyricsGraphicsRecoveryRequested;
    }

    private async void OnWebLyricsLoaded(object sender, RoutedEventArgs e)
    {
        if (_lyricsWebRenderer is null)
            return;

        ApplyWebLyricsStyle();
        await _lyricsWebRenderer.InitializeAsync();
        UpdateWebLyricsPresentation(allowTransition: false);
    }

    private void OnWebLyricsReady(object? sender, EventArgs e)
    {
        UpdateWebLyricsPresentation(allowTransition: false);
        RaiseDesiredSizeChanged(isForcedRefresh: true);
    }

    private void UpdateWebLyricsPresentation(bool allowTransition = true)
    {
        var previous = _lyricsFrame;
        var next = LyricsPresentationProjector.Project(_snapshot, SettingsManager.Current, DateTimeOffset.UtcNow);
        _lyricsFrame = next;

        var showWebLyrics = next.IsVisible && !_webLyricsGraphicsDisabled && _lyricsWebRenderer?.IsReady == true;
        SongMetadataPanel.Visibility = showWebLyrics ? Visibility.Collapsed : Visibility.Visible;
        // WebView2 must stay in the visible visual tree while it initializes. Hiding this
        // host until IsReady would prevent navigation from completing on some systems.
        SongLyricsPanel.Opacity = showWebLyrics ? 1 : 0;
        SongLyricsPanel.IsHitTestVisible = showWebLyrics;
        // The web engine decides whether a frame is a progress patch or a line transition.
        // Sending false on every 50 ms progress frame interrupts its in-flight roll animation.
        if (LyricsPresentationRefreshPolicy.ShouldPresent(next.IsVisible, previous, next))
            _lyricsWebRenderer?.Present(next, allowTransition && MotionPolicy.ResolveCurrent().UseTransitions);
        RefreshWebLyricsTimer();

        // 行变化后立即重发尺寸请求：自动模式下媒体栏长度跟着歌词内容走，若等到下一次快照才更新，
        // 新的一句会先按旧宽度渲染、右端短暂出现省略号（随后才恢复）。固定长度模式不依赖这条路径。
        // Re-issue the size request as soon as the line changes: in auto mode the bar length follows the content, and waiting for the
        // next snapshot would render the new line at the previous width and briefly show an ellipsis at its right edge. A fixed box does
        // not depend on this path.
        var secondaryText = string.IsNullOrEmpty(next.CurrentTranslation) ? next.Next : next.CurrentTranslation;
        if (!string.Equals(_lastSizeLyricText, next.Current, StringComparison.Ordinal) ||
            !string.Equals(_lastSizeSecondaryText, secondaryText, StringComparison.Ordinal))
        {
            _lastSizeLyricText = next.Current;
            _lastSizeSecondaryText = secondaryText;
            // 跳过宿主的长度过渡：过渡期间新句会按旧宽度渲染、右端被省略号截断，而歌词换行是离散切换。
            // Skip the host's length transition: during it the new line renders at the previous width and gets clipped with an
            // ellipsis, while a lyric line change is a discrete switch.
            RaiseDesiredSizeChanged(skipTransition: true);
        }
    }

    private void RefreshWebLyricsTimer()
    {
        // 注意用快照的播放状态而不是 _lyricsFrame.IsPlaying：隐藏帧是同一个 Hidden 单例，它的 IsPlaying 恒为 false，
        // 拿它判断会让"等待第一句"期间的定时器永远起不来。
        // Note it reads the snapshot's playback state instead of _lyricsFrame.IsPlaying: a hidden frame is the same
        // Hidden singleton whose IsPlaying is always false, so judging by it would keep the timer from ever starting
        // while waiting for the first line.
        var shouldRun = LyricsPresentationRefreshPolicy.ShouldRunTimer(
            IsLoaded,
            IsAdvancePruned,
            _lyricsFrame.IsVisible,
            _snapshot.IsPlaying,
            SettingsManager.Current.LyricsEnabled,
            _snapshot.IsConnected,
            _snapshot.Lyrics?.Document.Lines?.Count ?? 0);
        if (shouldRun)
        {
            if (!_lyricsWebTimer.IsEnabled)
                _lyricsWebTimer.Start();
        }
        else
        {
            _lyricsWebTimer.Stop();
        }
    }

    private void ApplyWebLyricsStyle()
    {
        if (_lyricsWebRenderer is null)
            return;

        var appearance = SettingsManager.Current.Appearance.Normalize();
        var alignment = SettingsManager.Current.LyricsTextAlignment switch
        {
            LyricsTextAlignment.Left => "Left",
            LyricsTextAlignment.Right => "Right",
            _ => "Center"
        };
        var primary = ToCssColor(_lyricsForeground, 1);
        var secondaryOpacity = SystemParameters.HighContrast ? 1 : 0.68;
        var unsungOpacity = SystemParameters.HighContrast
            ? 1
            : LyricsUnsungOpacity.Normalize(SettingsManager.Current.LyricsUnsungOpacityPercent) / 100d;
        var textShadow = _lyricsNeedsContrastShadow
            ? _lyricsUsesLightText
                ? "1px 1px 1px rgba(0, 0, 0, 0.85)"
                : "1px 1px 1px rgba(255, 255, 255, 0.85)"
            : "none";

        _lyricsWebRenderer.ApplyStyle(new LyricsWebStyle(
            appearance.ResolveFontFamilySource(SystemFonts.MessageFontFamily.Source),
            appearance.ResolveLatinFontFamily(SystemFonts.MessageFontFamily.Source),
            appearance.ResolveCjkFontFamily(SystemFonts.MessageFontFamily.Source),
            _layoutEngine?.LyricsFontSize ?? 12,
            appearance.FontWeight,
            LyricsCharacterSpacing.Normalize(SettingsManager.Current.LyricsCharacterSpacingPercent),
            LyricsLineGap.Normalize(SettingsManager.Current.LyricsLineGapPercent),
            alignment,
            primary,
            ToCssColor(_lyricsForeground, secondaryOpacity),
            ToCssColor(_lyricsForeground, secondaryOpacity),
            ToCssColor(_lyricsForeground, unsungOpacity),
            primary,
            textShadow));
    }

    private static string ToCssColor(Brush brush, double opacity)
    {
        var color = brush is SolidColorBrush solid ? solid.Color : Colors.White;
        var alpha = Math.Clamp(color.A / 255d * opacity, 0, 1);
        return $"rgba({color.R}, {color.G}, {color.B}, {alpha:0.###})";
    }

    /// <summary>
    /// Web 歌词视图为文字留出的水平内边距总和（DIP）：`.layout` 左右各 4px（共 8）加 `.lyrics-pane` 左 2px、右 4px（共 6），
    /// 合计 14px；再加 2px 余量，吸收字体度量取整（实测 WebView 的 clientWidth 会向上取整到物理像素）与亚像素排版。
    /// 文本可用宽度 = 宿主宽度 − 该值，自动尺寸 MUST 把它补回来；否则最长的一行在任务栏仍有空闲空间时也会被省略号裁掉右侧。
    /// Total horizontal inset the web lyrics view reserves for text, in DIP: `.layout` pads 4px on both sides (8) and
    /// `.lyrics-pane` adds 2px left and 4px right (6), fourteen in total, plus a two-pixel allowance that absorbs font-metric
    /// rounding (the WebView's clientWidth rounds up to physical pixels) and sub-pixel layout. The usable text width is the host
    /// width minus this value, so the auto-size MUST add it back; otherwise the longest line loses its right edge to the ellipsis
    /// even though the taskbar still has free room.
    /// </summary>
    private const double WebLyricsHorizontalInsetDip = 16;

    private double MeasureWebLyricWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var sourceSize = Math.Max(1, SongArtist.FontSize);
        var targetSize = _layoutEngine?.LyricsFontSize ?? sourceSize;
        var width = MeasureTextWidthExact(text, SongArtist) * targetSize / sourceSize + WebLyricsHorizontalInsetDip;

        // Web 歌词用 CSS letter-spacing 拉开字距：每个字符（含空格与末尾字符）都会多出
        // "字号 × 百分比" 的推进量。自动尺寸必须补上同样的宽度，否则拉大字距后整行会超出
        // 申请到的文字区，右侧被省略号截断——即使任务栏还有空闲空间。
        // The web lyrics widen with CSS letter-spacing: every character (spaces and the final one included) advances by
        // "font size × percent". The auto-size has to add the same amount, otherwise a wider spacing overflows the text
        // area that was requested without it and the right side is clipped with an ellipsis even though the taskbar still
        // has free room. Measured against the real page: width = base + characters × percent × font size.
        var percent = LyricsCharacterSpacing.Normalize(SettingsManager.Current.LyricsCharacterSpacingPercent);
        if (percent > 0)
        {
            width += text.Length * targetSize * percent / 100.0;
        }

        return width;
    }

    private void SetWebLyricsAppearance(Brush foreground, bool needsContrastShadow, bool usesLightText)
    {
        _lyricsForeground = foreground;
        _lyricsNeedsContrastShadow = needsContrastShadow;
        _lyricsUsesLightText = usesLightText;
        ApplyWebLyricsStyle();
    }

    private void StopWebLyrics()
    {
        _lyricsWebTimer.Stop();
        _lyricsWebRenderer?.Dispose();
        _lyricsWebRenderer = null;
        WebLyricsGraphicsRecovery.RecoveryRequested -= OnWebLyricsGraphicsRecoveryRequested;
    }

    /// <summary>全局异常处理识别出 WebView2 图形层故障后的回调：在新一帧里执行重建，避免在异常展开的调用栈里改动可视树。
    /// Called after the global exception handler identifies a WebView2 graphics fault: the rebuild runs on a fresh
    /// dispatcher frame so the visual tree is never mutated inside the unwinding exception stack.</summary>
    private void OnWebLyricsGraphicsRecoveryRequested()
    {
        if (_webLyricsGraphicsDisabled)
        {
            return;
        }

        Dispatcher.BeginInvoke(RecoverWebLyricsGraphics);
    }

    /// <summary>
    /// 重建 Web 歌词视图：故障出在 WebView2 合成控件内部（D3D 设备丢失时 `SetGraphicItem` 空引用），
    /// 控件本身无法复位，只能整只换掉——把旧控件移出可视树、释放，再放一个全新的合成控件并重新初始化。
    /// 预算用尽后停用 Web 歌词，退回元数据，保证应用不会再被同一缺陷击穿。
    /// Rebuilds the web lyrics view: the fault lives inside the WebView2 composition control (a null reference from
    /// `SetGraphicItem` when the D3D device is gone) and the control cannot be reset, so it is replaced as a whole —
    /// the old one leaves the tree and is disposed, a fresh composition control is inserted and initialized. Once the
    /// budget is spent the web lyrics stay off and the bar falls back to metadata, so the same defect cannot take the
    /// application down again.
    /// </summary>
    private void RecoverWebLyricsGraphics()
    {
        if (!WebView2GraphicsFaultPolicy.CanRecover(_lyricsGraphicsRecoveries))
        {
            _webLyricsGraphicsDisabled = true;
            StopWebLyrics();
            ApplyWebLyricsStyle();
            UpdateWebLyricsPresentation(allowTransition: false);
            RaiseDesiredSizeChanged(isForcedRefresh: true);
            AppLogService.Current?.Warn(
                "Lyrics",
                $"WebView2 图形层故障已达重建上限（{WebView2GraphicsFaultPolicy.MaximumRecoveries} 次），本次会话停用 Web 歌词并退回元数据");
            return;
        }

        _lyricsGraphicsRecoveries++;
        AppLogService.Current?.Warn(
            "Lyrics",
            $"WebView2 图形层故障，正在重建歌词视图（第 {_lyricsGraphicsRecoveries}/{WebView2GraphicsFaultPolicy.MaximumRecoveries} 次）");

        var previousRenderer = _lyricsWebRenderer;
        var panel = SongLyricsPanel;
        var oldControl = LyricsWebView;
        var index = panel.Children.IndexOf(oldControl);
        if (previousRenderer is not null)
        {
            previousRenderer.Ready -= OnWebLyricsReady;
        }

        // 先把旧控件移出可视树再释放：Dispose 之后它不能再参与任何一次布局。
        // Remove the old control from the visual tree before disposing it: a disposed one must not take part in any layout pass.
        if (index >= 0)
        {
            panel.Children.RemoveAt(index);
        }

        previousRenderer?.Dispose();

        var replacement = new WebView2CompositionControl
        {
            MinWidth = 1,
            MinHeight = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false
        };
        panel.Children.Insert(index >= 0 ? index : 0, replacement);
        LyricsWebView = replacement;

        _lyricsWebRenderer = new LyricsWebViewRenderer(replacement);
        _lyricsWebRenderer.Ready += OnWebLyricsReady;
        ApplyWebLyricsStyle();
        _ = _lyricsWebRenderer.InitializeAsync();
    }
}
