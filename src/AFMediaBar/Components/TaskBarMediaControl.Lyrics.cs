using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;

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

    private void InitializeWebLyrics()
    {
        _lyricsWebRenderer = new LyricsWebViewRenderer(LyricsWebView);
        _lyricsWebRenderer.Ready += OnWebLyricsReady;
        _lyricsWebTimer.Tick += (_, _) => UpdateWebLyricsPresentation();
        Loaded += OnWebLyricsLoaded;
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
        var next = LyricsPresentationProjector.Project(_snapshot, SettingsManager.Current, DateTimeOffset.UtcNow);
        _lyricsFrame = next;

        var showWebLyrics = next.IsVisible && _lyricsWebRenderer?.IsReady == true;
        SongMetadataPanel.Visibility = showWebLyrics ? Visibility.Collapsed : Visibility.Visible;
        // WebView2 must stay in the visible visual tree while it initializes. Hiding this
        // host until IsReady would prevent navigation from completing on some systems.
        SongLyricsPanel.Opacity = showWebLyrics ? 1 : 0;
        SongLyricsPanel.IsHitTestVisible = showWebLyrics;
        // The web engine decides whether a frame is a progress patch or a line transition.
        // Sending false on every 50 ms progress frame interrupts its in-flight roll animation.
        _lyricsWebRenderer?.Present(next, allowTransition && MotionPolicy.ResolveCurrent().UseTransitions);
        RefreshWebLyricsTimer();
    }

    private void RefreshWebLyricsTimer()
    {
        var shouldRun = IsLoaded &&
                        !IsAdvancePruned &&
                        _lyricsFrame.IsVisible &&
                        _lyricsFrame.IsPlaying;
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
    }
}
