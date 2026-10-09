using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
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
    private static readonly TimeSpan _lyricsFrameInterval = TimeSpan.FromMilliseconds(50);

    private readonly DispatcherTimer _lyricsWebTimer = new(DispatcherPriority.Render)
    {
        Interval = _lyricsFrameInterval
    };
    private readonly DispatcherTimer _lyricsReleaseTimer = new(DispatcherPriority.Background)
    {
        Interval = LyricsWebViewLifetimePolicy.NoLyricsGracePeriod
    };
    private readonly DispatcherTimer _lyricsRetryTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(2)
    };
    private LyricsWebViewRenderer? _lyricsWebRenderer;
    private bool _lyricsHostLoaded;
    private int _lyricsConversionRefreshQueued;
    private int _lyricsRendererGeneration;
    private int _lyricsCreationFailures;
    private int _lyricsSuspendGeneration;
    private bool _lyricsSuspendRequested;
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
    private bool _lyricsGraphicsRecoveryQueued;

    private void InitializeWebLyrics()
    {
        _lyricsWebTimer.Tick += (_, _) => UpdateWebLyricsPresentation();
        _lyricsReleaseTimer.Tick += OnWebLyricsReleaseTimerTick;
        _lyricsRetryTimer.Tick += OnWebLyricsRetryTimerTick;
        Loaded += OnWebLyricsLoaded;
        Unloaded += OnWebLyricsUnloaded;
    }

    private void OnWebLyricsLoaded(object sender, RoutedEventArgs e)
    {
        if (_lyricsHostLoaded)
            return;

        _lyricsHostLoaded = true;
        SettingsManager.LyricsSettingsChanged += OnWebLyricsSettingsChanged;
        LyricsChineseConverter.Updated += OnChineseConversionUpdated;
        WebLyricsGraphicsRecovery.RecoveryRequested += OnWebLyricsGraphicsRecoveryRequested;
        UpdateWebLyricsPresentation(allowTransition: false);
    }

    private void OnWebLyricsUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_lyricsHostLoaded)
            return;

        _lyricsHostLoaded = false;
        SettingsManager.LyricsSettingsChanged -= OnWebLyricsSettingsChanged;
        LyricsChineseConverter.Updated -= OnChineseConversionUpdated;
        WebLyricsGraphicsRecovery.RecoveryRequested -= OnWebLyricsGraphicsRecoveryRequested;
        StopWebLyrics();
    }

    private void OnChineseConversionUpdated()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished ||
            Interlocked.Exchange(ref _lyricsConversionRefreshQueued, 1) != 0)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _lyricsConversionRefreshQueued, 0);
            // 通知不携带旧曲目或文本，恢复时按最新快照与转换方向重新投影。
            if (_lyricsHostLoaded)
                UpdateWebLyricsPresentation(allowTransition: false);
        });
    }

    private void OnWebLyricsSettingsChanged(object? sender, EventArgs e)
    {
        // A settings change must release an idle host even while snapshot delivery is paused.
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                Dispatcher.BeginInvoke(() => OnWebLyricsSettingsChanged(sender, e));
            return;
        }
        if (_lyricsHostLoaded)
            UpdateWebLyricsPresentation(allowTransition: false);
    }

    private void OnWebLyricsReady(object? sender, EventArgs e)
    {
        if (!_lyricsHostLoaded || !ReferenceEquals(sender, _lyricsWebRenderer))
            return;

        _lyricsRetryTimer.Stop();
        UpdateWebLyricsPowerState();
        UpdateWebLyricsPresentation(allowTransition: false);
        RaiseDesiredSizeChanged(isForcedRefresh: true);
    }

    private void OnWebLyricsFailed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _lyricsWebRenderer))
            return;

        var generation = _lyricsRendererGeneration;
        Dispatcher.BeginInvoke(() =>
        {
            if (_lyricsHostLoaded && generation == _lyricsRendererGeneration &&
                ReferenceEquals(sender, _lyricsWebRenderer))
                HandleWebLyricsFailure();
        });
    }

    private async Task EnsureWebLyricsAsync()
    {
        var generation = ++_lyricsRendererGeneration;
        LyricsWebViewRenderer? renderer = null;
        WebView2CompositionControl? webView = null;
        try
        {
            // A disposed composition control cannot reenter WPF layout; every new renderer owns a fresh control.
            webView = new WebView2CompositionControl
            {
                MinWidth = 1,
                MinHeight = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false
            };
            renderer = new LyricsWebViewRenderer(webView);
            _lyricsWebRenderer = renderer;
            renderer.Ready += OnWebLyricsReady;
            renderer.Failed += OnWebLyricsFailed;
            LyricsWebViewHost.Child = webView;
            ApplyWebLyricsStyle();

            var initialized = await renderer.InitializeAsync();
            if (generation != _lyricsRendererGeneration || !ReferenceEquals(renderer, _lyricsWebRenderer))
                return;

            if (!initialized)
            {
                HandleWebLyricsFailure();
                return;
            }

            UpdateWebLyricsPresentation(allowTransition: false);
        }
        catch (Exception ex)
        {
            AppLogService.Current?.Error("Lyrics", "Failed to create the web lyrics host.", ex);
            if (generation == _lyricsRendererGeneration)
                HandleWebLyricsFailure();
            else if (renderer is null)
                webView?.Dispose();
        }
    }

    private void HandleWebLyricsFailure()
    {
        ReleaseWebLyricsRenderer();
        _lyricsCreationFailures++;
        if (_lyricsHostLoaded && _lyricsCreationFailures == 1)
            _lyricsRetryTimer.Start();
    }

    private void OnWebLyricsRetryTimerTick(object? sender, EventArgs e)
    {
        _lyricsRetryTimer.Stop();
        if (_lyricsHostLoaded)
            UpdateWebLyricsPresentation(allowTransition: false);
    }

    private void OnWebLyricsReleaseTimerTick(object? sender, EventArgs e)
    {
        _lyricsReleaseTimer.Stop();
        if (LyricsWebViewLifetimePolicy.Resolve(
                _lyricsHostLoaded, SettingsManager.Current.LyricsEnabled, _webLyricsGraphicsDisabled,
                _snapshot.IsConnected, _snapshot.Lyrics?.Document.Lines?.Count ?? 0,
                _lyricsWebRenderer is not null) == LyricsWebViewLifetimeAction.ReleaseAfterGrace)
            ReleaseWebLyricsRenderer();
    }

    private void ApplyWebLyricsLifetime()
    {
        var action = LyricsWebViewLifetimePolicy.Resolve(
            _lyricsHostLoaded, SettingsManager.Current.LyricsEnabled, _webLyricsGraphicsDisabled,
            _snapshot.IsConnected, _snapshot.Lyrics?.Document.Lines?.Count ?? 0,
            _lyricsWebRenderer is not null);
        switch (action)
        {
            case LyricsWebViewLifetimeAction.Create:
                _lyricsReleaseTimer.Stop();
                if (_backgroundPruneLevel < MemoryPruneLevel.DisplayOff && !_isHostVisibilitySuspended &&
                    !_lyricsRetryTimer.IsEnabled && _lyricsCreationFailures < 2)
                    _ = EnsureWebLyricsAsync();
                break;
            case LyricsWebViewLifetimeAction.Retain:
                _lyricsReleaseTimer.Stop();
                break;
            case LyricsWebViewLifetimeAction.ReleaseAfterGrace:
                if (!_lyricsReleaseTimer.IsEnabled)
                    _lyricsReleaseTimer.Start();
                break;
            case LyricsWebViewLifetimeAction.ReleaseNow:
                _lyricsReleaseTimer.Stop();
                _lyricsRetryTimer.Stop();
                ReleaseWebLyricsRenderer();
                _lyricsCreationFailures = 0;
                break;
            default:
                _lyricsReleaseTimer.Stop();
                if (!_snapshot.IsConnected || !SettingsManager.Current.LyricsEnabled)
                    _lyricsCreationFailures = 0;
                break;
        }
    }

    private void UpdateWebLyricsPowerState()
    {
        if (_lyricsWebRenderer is not { IsReady: true } renderer ||
            LyricsWebViewHost.Child is not WebView2CompositionControl webView)
            return;

        // The host owns visibility while following Shell motion. Keep the current composition
        // frame instead of replacing lyrics with metadata in the middle of the taskbar slide.
        if (_isHostVisibilitySuspended && _backgroundPruneLevel < MemoryPruneLevel.DisplayOff)
        {
            renderer.PauseDelivery();
            return;
        }

        var hidden = _backgroundPruneLevel >= MemoryPruneLevel.DisplayOff;
        if (hidden)
        {
            renderer.PauseDelivery();
            // Hidden keeps the 1x1 layout guard while making the WPF view genuinely invisible to WebView2.
            webView.Visibility = Visibility.Hidden;
            SongLyricsPanel.Opacity = 0;
            SongMetadataPanel.Visibility = Visibility.Visible;
            if (_backgroundPruneLevel >= MemoryPruneLevel.DisplayOff && !_lyricsSuspendRequested)
            {
                _lyricsSuspendRequested = true;
                var generation = ++_lyricsSuspendGeneration;
                _ = SuspendWebLyricsAfterLayoutAsync(renderer, generation);
            }
            return;
        }

        var needsRefresh = webView.Visibility != Visibility.Visible || _lyricsSuspendRequested;
        if (_lyricsSuspendRequested)
        {
            _lyricsSuspendRequested = false;
            _lyricsSuspendGeneration++;
            renderer.Resume();
        }
        webView.Visibility = Visibility.Visible;
        renderer.ResumeDelivery();
        if (needsRefresh)
            UpdateWebLyricsPresentation(allowTransition: false);
    }

    private async Task SuspendWebLyricsAfterLayoutAsync(LyricsWebViewRenderer renderer, int generation)
    {
        try
        {
            // Let WPF propagate Visibility.Hidden to the WebView2 controller before TrySuspendAsync.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (generation != _lyricsSuspendGeneration || !ReferenceEquals(renderer, _lyricsWebRenderer) ||
                _backgroundPruneLevel < MemoryPruneLevel.DisplayOff)
                return;

            var suspended = await renderer.TrySuspendAsync();
            if (generation != _lyricsSuspendGeneration || !ReferenceEquals(renderer, _lyricsWebRenderer) ||
                _backgroundPruneLevel < MemoryPruneLevel.DisplayOff)
            {
                if (suspended && ReferenceEquals(renderer, _lyricsWebRenderer))
                    renderer.Resume();
            }
        }
        catch (Exception ex)
        {
            AppLogService.Current?.Warn("Lyrics", $"Web lyrics suspend handoff failed: {ex.Message}");
        }
    }

    private void UpdateWebLyricsPresentation(bool allowTransition = true)
    {
        // Resource release still applies while the host's presentation is frozen.
        ApplyWebLyricsLifetime();
        if (_isHostVisibilitySuspended)
            return;

        // Keep the outgoing live lyrics until the connection clip has fully retracted.
        if (_restTransitionKeepsOutgoingText)
            return;

        var previous = _lyricsFrame;
        var next = LyricsPresentationProjector.Project(_snapshot, SettingsManager.Current, DateTimeOffset.UtcNow);
        _lyricsFrame = next;

        var showWebLyrics = next.IsVisible && !_webLyricsGraphicsDisabled &&
                            _backgroundPruneLevel < MemoryPruneLevel.DisplayOff && !_isHostVisibilitySuspended &&
                            _lyricsWebRenderer?.IsReady == true;
        SongMetadataPanel.Visibility = showWebLyrics ? Visibility.Collapsed : Visibility.Visible;
        // WebView2 must stay in the visible visual tree while it initializes. Hiding this
        // host until IsReady would prevent navigation from completing on some systems.
        SongLyricsPanel.Opacity = showWebLyrics ? 1 : 0;
        SongLyricsPanel.IsHitTestVisible = showWebLyrics;
        // The web engine decides whether a frame is a progress patch or a line transition.
        // Sending false on every 50 ms progress frame interrupts its in-flight roll animation.
        if (!IsBackgroundPruned && LyricsPresentationRefreshPolicy.ShouldPresent(next.IsVisible, previous, next))
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
        var shouldRun = !_webLyricsGraphicsDisabled && LyricsPresentationRefreshPolicy.ShouldRunTimer(
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
        _lyricsReleaseTimer.Stop();
        _lyricsRetryTimer.Stop();
        ReleaseWebLyricsRenderer();
        _lyricsCreationFailures = 0;
    }

    private void ReleaseWebLyricsRenderer()
    {
        _lyricsRendererGeneration++;
        _lyricsSuspendGeneration++;
        _lyricsSuspendRequested = false;
        var renderer = _lyricsWebRenderer;
        _lyricsWebRenderer = null;
        if (renderer is not null)
        {
            renderer.Ready -= OnWebLyricsReady;
            renderer.Failed -= OnWebLyricsFailed;
        }

        // A disposed composition control must not participate in another WPF layout pass.
        LyricsWebViewHost.Child = null;
        renderer?.Dispose();
        SongLyricsPanel.Opacity = 0;
        SongLyricsPanel.IsHitTestVisible = false;
        SongMetadataPanel.Visibility = Visibility.Visible;
    }

    /// <summary>全局异常处理识别出 WebView2 图形层故障后的回调：在新一帧里执行重建，避免在异常展开的调用栈里改动可视树。
    /// Called after the global exception handler identifies a WebView2 graphics fault: the rebuild runs on a fresh
    /// dispatcher frame so the visual tree is never mutated inside the unwinding exception stack.</summary>
    private void OnWebLyricsGraphicsRecoveryRequested()
    {
        if (_webLyricsGraphicsDisabled || _lyricsGraphicsRecoveryQueued || _lyricsWebRenderer is null)
        {
            return;
        }

        _lyricsGraphicsRecoveryQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _lyricsGraphicsRecoveryQueued = false;
            if (_lyricsWebRenderer is not null && !_webLyricsGraphicsDisabled)
                RecoverWebLyricsGraphics();
        });
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
            // A transparent WebView still participates in WPF layout and may throw the same
            // graphics fault again. Detach it before disposing the renderer.
            StopWebLyrics();
            SongLyricsPanel.Visibility = Visibility.Collapsed;
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

        ReleaseWebLyricsRenderer();
        _lyricsCreationFailures = 0;
        UpdateWebLyricsPresentation(allowTransition: false);
    }
}
