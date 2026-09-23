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
        var lineChanged = _lyricsFrame.IsVisible && next.IsVisible &&
                          string.Equals(_lyricsFrame.TrackId, next.TrackId, StringComparison.Ordinal) &&
                          _lyricsFrame.CurrentLineIndex != next.CurrentLineIndex;
        _lyricsFrame = next;

        var showWebLyrics = next.IsVisible && _lyricsWebRenderer?.IsReady == true;
        SongMetadataPanel.Visibility = showWebLyrics ? Visibility.Collapsed : Visibility.Visible;
        SongLyricsPanel.Visibility = showWebLyrics ? Visibility.Visible : Visibility.Collapsed;
        _lyricsWebRenderer?.Present(next, allowTransition && lineChanged && MotionPolicy.ResolveCurrent().UseTransitions);
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

    private double MeasureWebLyricWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var sourceSize = Math.Max(1, SongArtist.FontSize);
        var targetSize = _layoutEngine?.LyricsFontSize ?? sourceSize;
        return MeasureTextWidthExact(text, SongArtist) * targetSize / sourceSize + 4;
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
