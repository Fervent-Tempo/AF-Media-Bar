// BarLyricsPresenter 独占单个控件的呈现资源；由控件加载、挂起与卸载路径驱动，不接管业务服务。
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.Web.WebView2.Wpf;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private sealed class BarLyricsPresenter : IDisposable
    {
        private readonly TaskBarMediaControl _owner;
        internal BarLyricsPresenter(TaskBarMediaControl owner) { _owner = owner; }


        private bool _disposed;
        private readonly string _diagnosticHostId = Guid.NewGuid().ToString("N");
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            OnWebLyricsUnloaded(_owner, new RoutedEventArgs());
            _owner.Loaded -= OnWebLyricsLoaded;
            _owner.Unloaded -= OnWebLyricsUnloaded;
            StopWebLyrics();
        }

        internal static readonly TimeSpan _lyricsFrameInterval = TimeSpan.FromMilliseconds(50);

        internal readonly DispatcherTimer _lyricsWebTimer = new(DispatcherPriority.Render)
        {
            Interval = _lyricsFrameInterval
        };
        internal readonly DispatcherTimer _lyricsReleaseTimer = new(DispatcherPriority.Background)
        {
            Interval = LyricsWebViewLifetimePolicy.NoLyricsGracePeriod
        };
        internal readonly DispatcherTimer _lyricsRetryTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        internal LyricsWebViewRenderer? _lyricsWebRenderer;
        internal bool _lyricsHostLoaded;
        internal int _lyricsConversionRefreshQueued;
        internal int _lyricsRendererGeneration;
        internal int _lyricsCreationFailures;
        internal int _lyricsSuspendGeneration;
        internal bool _lyricsSuspendRequested;
        internal LyricsPresentationFrame _lyricsFrame = LyricsPresentationFrame.Hidden;
        internal LyricsPresentationFrame _lastEffectiveLyricsFrame = LyricsPresentationFrame.Hidden;
        internal Brush _lyricsForeground = Brushes.White;
        internal bool _lyricsNeedsContrastShadow;
        internal bool _lyricsUsesLightText = true;

        /// <summary>上一次尺寸请求所用的歌词文本，用来在换行时立即重新发布尺寸（见 UpdateWebLyricsPresentation）。
        /// Lyric texts used by the last size request, so a line change can republish the size immediately (see UpdateWebLyricsPresentation).</summary>
        internal string _lastSizeLyricText = string.Empty;
        internal string _lastSizeSecondaryText = string.Empty;

        /// <summary>已经为 WebView2 图形层故障重建过几次歌词视图（见 RecoverWebLyricsGraphics）。
        /// How many times the lyric view has been rebuilt after a WebView2 graphics fault (see RecoverWebLyricsGraphics).</summary>
        internal int _lyricsGraphicsRecoveries;
        private bool _graphicsRecoveryPending;

        /// <summary>重建预算用尽后置位：停用 Web 歌词、退回元数据，避免同一缺陷反复击穿应用。
        /// Set once the rebuild budget is spent: the web lyrics stay off and the bar falls back to metadata so the same defect cannot take the app down again.</summary>
        internal bool _webLyricsGraphicsDisabled;
        internal WebLyricsRecoveryQueue? _lyricsGraphicsRecoveryQueue;

        internal void InitializeWebLyrics()
        {
            _lyricsGraphicsRecoveryQueue = new(_owner.Dispatcher);
            _lyricsWebTimer.Tick += (_, _) => UpdateWebLyricsPresentation();
            _lyricsReleaseTimer.Tick += OnWebLyricsReleaseTimerTick;
            _lyricsRetryTimer.Tick += OnWebLyricsRetryTimerTick;
            _owner.Loaded += OnWebLyricsLoaded;
            _owner.Unloaded += OnWebLyricsUnloaded;
        }

        internal void OnWebLyricsLoaded(object sender, RoutedEventArgs e)
        {
            if (_disposed) return;
            if (_lyricsHostLoaded)
                return;

            _lyricsHostLoaded = true;
            _webLyricsGraphicsDisabled |= WebLyricsGraphicsRecovery.Session.IsDisabled;
            SettingsManager.LyricsSettingsChanged += OnWebLyricsSettingsChanged;
            LyricsChineseConverter.Updated += OnChineseConversionUpdated;
            WebLyricsGraphicsRecovery.Session.RecoveryRequested += OnWebLyricsGraphicsRecoveryRequested;
            UpdateWebLyricsPresentation(allowTransition: false);
        }

        internal void OnWebLyricsUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_lyricsHostLoaded)
                return;

            _lyricsHostLoaded = false;
            SettingsManager.LyricsSettingsChanged -= OnWebLyricsSettingsChanged;
            LyricsChineseConverter.Updated -= OnChineseConversionUpdated;
            WebLyricsGraphicsRecovery.Session.RecoveryRequested -= OnWebLyricsGraphicsRecoveryRequested;
            StopWebLyrics();
        }

        internal void OnChineseConversionUpdated()
        {
            if (_owner.Dispatcher.HasShutdownStarted || _owner.Dispatcher.HasShutdownFinished ||
                Interlocked.Exchange(ref _lyricsConversionRefreshQueued, 1) != 0)
                return;

            _owner.Dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _lyricsConversionRefreshQueued, 0);
                // 通知不携带旧曲目或文本，恢复时按最新快照与转换方向重新投影。
                if (_lyricsHostLoaded)
                    OnWebLyricsSettingsChanged(null, EventArgs.Empty);
            });
        }

        internal void OnWebLyricsSettingsChanged(object? sender, EventArgs e)
        {
            // A settings change must release an idle host even while snapshot delivery is paused.
            if (!_owner.Dispatcher.CheckAccess())
            {
                if (!_owner.Dispatcher.HasShutdownStarted && !_owner.Dispatcher.HasShutdownFinished)
                    _owner.Dispatcher.BeginInvoke(() => OnWebLyricsSettingsChanged(sender, e));
                return;
            }
            // 允许过渡：有的设置会改掉这一行显示的文字（隐藏日语读音标注、繁简转换在后台就绪），
            // 直接落位就是一次"硬刷新"。改外观的设置不会换字，规划结果仍是进度补丁，不会平白起动画。
            // 没有改字时连帧都不重发：歌词里没有读音标注时切那个开关，画面上就该什么都不发生。
            // Allow the transition: some settings rewrite the text of the line on screen (hiding Japanese
            // reading annotations, the Chinese conversion becoming ready), and snapping it into place is the
            // hard refresh. Appearance-only settings leave the text alone and still plan a progress patch.
            // With no text change the frame is not even re-sent: toggling that switch on lyrics without any
            // reading annotation has to leave the bar exactly as it is.
            if (!_lyricsHostLoaded)
                return;
            var projected = LyricsPresentationProjector.Project(_owner._snapshot, _owner.PresentationSettings, DateTimeOffset.UtcNow);
            if (VisibleContentChanged(_lyricsFrame, projected))
                UpdateWebLyricsPresentation(allowTransition: true);
        }

        internal void OnWebLyricsReady(object? sender, EventArgs e)
        {
            if (!_lyricsHostLoaded || !ReferenceEquals(sender, _lyricsWebRenderer))
                return;

            if (_graphicsRecoveryPending)
            {
                _graphicsRecoveryPending = false;
                AppLogService.Current?.Info("Lyrics", $"WebView2 歌词视图重建完成: generation={_lyricsRendererGeneration} recovery={_lyricsGraphicsRecoveries}");
            }
            _lyricsRetryTimer.Stop();
            UpdateWebLyricsPowerState();
            UpdateWebLyricsPresentation(allowTransition: false);
            _owner.RaiseDesiredSizeChanged(isForcedRefresh: true);
        }

        internal void OnWebLyricsFailed(object? sender, EventArgs e)
        {
            if (!_lyricsHostLoaded || _owner.Dispatcher.HasShutdownStarted || _owner.Dispatcher.HasShutdownFinished ||
                !ReferenceEquals(sender, _lyricsWebRenderer))
                return;

            var generation = _lyricsRendererGeneration;
            _owner.Dispatcher.BeginInvoke(() =>
            {
                if (_lyricsHostLoaded && !_owner.Dispatcher.HasShutdownStarted && generation == _lyricsRendererGeneration &&
                    ReferenceEquals(sender, _lyricsWebRenderer))
                    HandleWebLyricsFailure();
            });
        }

        internal async Task EnsureWebLyricsAsync()
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
                renderer = new LyricsWebViewRenderer(webView, AppLogService.Current, _diagnosticHostId, generation);
                _lyricsWebRenderer = renderer;
                renderer.Ready += OnWebLyricsReady;
                renderer.Failed += OnWebLyricsFailed;
                _owner.LyricsWebViewHost.Child = webView;
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

        internal void HandleWebLyricsFailure()
        {
            ReleaseWebLyricsRenderer();
            _lyricsCreationFailures++;
            if (_lyricsHostLoaded && _lyricsCreationFailures == 1)
                _lyricsRetryTimer.Start();
            else if (_lyricsHostLoaded && _lyricsCreationFailures >= 2)
                WebLyricsGraphicsRecovery.Session.ReportDegraded();
        }

        internal void OnWebLyricsRetryTimerTick(object? sender, EventArgs e)
        {
            _lyricsRetryTimer.Stop();
            if (_lyricsHostLoaded)
                UpdateWebLyricsPresentation(allowTransition: false);
        }

        internal void OnWebLyricsReleaseTimerTick(object? sender, EventArgs e)
        {
            _lyricsReleaseTimer.Stop();
            if (LyricsWebViewLifetimePolicy.Resolve(
                    _lyricsHostLoaded, _owner.PresentationSettings.LyricsEnabled || _owner.VerticalTextRequired, _webLyricsGraphicsDisabled,
                    _owner._snapshot.IsConnected, _owner.VerticalTextRequired ? 1 : _owner._snapshot.Lyrics?.Document.Lines?.Count ?? 0,
                    _lyricsWebRenderer is not null) == LyricsWebViewLifetimeAction.ReleaseAfterGrace)
                ReleaseWebLyricsRenderer();
        }

        internal void ApplyWebLyricsLifetime()
        {
            if (_disposed) return;
            _webLyricsGraphicsDisabled |= WebLyricsGraphicsRecovery.Session.IsDisabled;
            var action = LyricsWebViewLifetimePolicy.Resolve(
                _lyricsHostLoaded, _owner.PresentationSettings.LyricsEnabled || _owner.VerticalTextRequired, _webLyricsGraphicsDisabled,
                _owner._snapshot.IsConnected, _owner.VerticalTextRequired ? 1 : _owner._snapshot.Lyrics?.Document.Lines?.Count ?? 0,
                _lyricsWebRenderer is not null);
            switch (action)
            {
                case LyricsWebViewLifetimeAction.Create:
                    _lyricsReleaseTimer.Stop();
                    if (_owner._backgroundPruneLevel < MemoryPruneLevel.DisplayOff && !_owner._isHostVisibilitySuspended &&
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
                    if (!_owner._snapshot.IsConnected || !_owner.PresentationSettings.LyricsEnabled)
                        _lyricsCreationFailures = 0;
                    break;
            }
            if (_lyricsHostLoaded && WebLyricsGraphicsRecovery.Session.IsDisabled)
                WebLyricsGraphicsRecovery.Session.ReportDegraded();
        }

        internal void UpdateWebLyricsPowerState()
        {
            if (_disposed) return;
            if (_lyricsWebRenderer is not { IsReady: true } renderer ||
                _owner.LyricsWebViewHost.Child is not WebView2CompositionControl webView)
                return;

            // The host owns visibility while following Shell motion. Keep the current composition
            // frame instead of replacing lyrics with metadata in the middle of the taskbar slide.
            if (_owner._isHostVisibilitySuspended && _owner._backgroundPruneLevel < MemoryPruneLevel.DisplayOff)
            {
                renderer.PauseDelivery();
                return;
            }

            var hidden = _owner._backgroundPruneLevel >= MemoryPruneLevel.DisplayOff;
            if (hidden)
            {
                renderer.PauseDelivery();
                // Hidden keeps the 1x1 layout guard while making the WPF view genuinely invisible to WebView2.
                webView.Visibility = Visibility.Hidden;
                _owner.SongLyricsPanel.Opacity = 0;
                _owner.SongMetadataPanel.Visibility = Visibility.Visible;
                if (_owner._backgroundPruneLevel >= MemoryPruneLevel.DisplayOff && !_lyricsSuspendRequested)
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
                if (!renderer.IsReady) return;
            }
            webView.Visibility = Visibility.Visible;
            renderer.ResumeDelivery();
            if (needsRefresh)
                UpdateWebLyricsPresentation(allowTransition: false);
        }

        internal async Task SuspendWebLyricsAfterLayoutAsync(LyricsWebViewRenderer renderer, int generation)
        {
            try
            {
                // Let WPF propagate Visibility.Hidden to the WebView2 controller before TrySuspendAsync.
                await _owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                if (generation != _lyricsSuspendGeneration || !ReferenceEquals(renderer, _lyricsWebRenderer) ||
                    _owner._backgroundPruneLevel < MemoryPruneLevel.DisplayOff)
                    return;

                var suspended = await renderer.TrySuspendAsync();
                if (generation != _lyricsSuspendGeneration || !ReferenceEquals(renderer, _lyricsWebRenderer) ||
                    _owner._backgroundPruneLevel < MemoryPruneLevel.DisplayOff)
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

        /// <param name="skipUnchangedContent">
        /// 可见内容与上一帧完全相同时不再下发。设置变更走这条：转换方向、读音标注开关这类改动要么换字
        /// （交给过渡），要么什么都没变——连帧都不该重发，与 <see cref="OnWebLyricsSettingsChanged"/>
        /// 的守卫是同一条规则。50ms 进度帧与渲染器就绪/故障恢复路径不带这个标记，照旧下发。
        /// Skip the send when the visible content matches the previous frame exactly. Settings changes take
        /// this path: a conversion direction or annotation toggle either rewrites the text (let it transition)
        /// or changes nothing at all — no frame should go out, the same rule the OnWebLyricsSettingsChanged
        /// guard follows. The 50 ms progress frames and the renderer readiness/recovery paths do not carry
        /// the flag and keep sending as before.
        /// </param>
        internal void UpdateWebLyricsPresentation(bool allowTransition = true, bool skipUnchangedContent = false)
        {
            if (_disposed) return;
            // Resource release still applies while the host's presentation is frozen.
            ApplyWebLyricsLifetime();
            if (_owner._isHostVisibilitySuspended)
                return;

            // Keep the outgoing live lyrics until the connection clip has fully retracted.
            if (_owner._restTransitionKeepsOutgoingText)
                return;

            var previous = _lastEffectiveLyricsFrame;
            var next = LyricsPresentationProjector.Project(_owner._snapshot, _owner.PresentationSettings, DateTimeOffset.UtcNow);
            _lyricsFrame = next;
            _owner.RefreshTaskbarTextBudget(next);
            var isMetadata = _owner._isVertical && !next.IsVisible && _owner._snapshot.IsConnected;
            var presentation = isMetadata ? _owner.BuildVerticalMetadataFrame() : _owner.VisibleLyricsFrame(next);
            _lastEffectiveLyricsFrame = presentation;
            // 设置变更可能改掉这一行显示的文字（隐藏日语读音标注、繁简转换在后台就绪）。只有"之前和现在都在显示
            // 歌词"时才让这类变化走过渡：开启/关闭歌词、首次就绪与故障恢复仍然立即落位，与改动前一致。
            // A settings change can rewrite the line on screen (hidden reading annotations, a conversion that just
            // became ready). Only let that animate while lyrics were and still are on screen; enabling or disabling
            // lyrics, first readiness, and fault recovery keep snapping into place exactly as before.
            var animateTransition = allowTransition && previous.IsVisible && presentation.IsVisible;

            var showWebLyrics = presentation.IsVisible && !_webLyricsGraphicsDisabled &&
                                _owner._backgroundPruneLevel < MemoryPruneLevel.DisplayOff && !_owner._isHostVisibilitySuspended &&
                                _lyricsWebRenderer?.IsReady == true;
            var metadataVisibility = showWebLyrics ? Visibility.Collapsed : Visibility.Visible;
            if (_owner.SongMetadataPanel.Visibility != metadataVisibility)
            {
                _owner.SongMetadataPanel.Visibility = metadataVisibility;
                _owner.ApplyMarqueeLayout(Math.Max(0, _owner.SongInfoStackPanel.Width));
            }
            // WebView2 must stay in the visible visual tree while it initializes. Hiding this
            // host until IsReady would prevent navigation from completing on some systems.
            _owner.SongLyricsPanel.Opacity = showWebLyrics ? 1 : 0;
            _owner.SongLyricsPanel.IsHitTestVisible = showWebLyrics;
            // The web engine decides whether a frame is a progress patch or a line transition.
            // Sending false on every 50 ms progress frame interrupts its in-flight roll animation.
            // 设置变更链路（简繁转换方向、日语读音标注开关等）带 skipUnchangedContent：可见内容一字未变时
            // 这里直接不下发。纵向元数据帧携带播放状态且不带进度，仍按原路径走。
            // The settings-change chain (Chinese conversion direction, Japanese reading-annotation toggles) passes
            // skipUnchangedContent: when the visible content is character-for-character identical nothing is sent
            // here. The vertical metadata frame carries playback state and no progress, so it keeps its path.
            var contentUnchanged = skipUnchangedContent && !isMetadata && !VisibleContentChanged(previous, presentation);
            if (!_owner.IsBackgroundPruned && !contentUnchanged &&
                (isMetadata || LyricsPresentationRefreshPolicy.ShouldPresent(presentation.IsVisible, previous, presentation)))
                _lyricsWebRenderer?.Present(presentation, animateTransition && MotionPolicy.ResolveCurrent().UseTransitions, isMetadata);
            RefreshWebLyricsTimer();

            // 文本或逐字时间轴可用性变化后立即重发尺寸请求；没有时间轴时长行需要恢复自然宽度。
            // 自动模式下媒体栏长度跟着歌词内容走，若等到下一次快照才更新，
            // 新的一句会先按旧宽度渲染、右端短暂出现省略号（随后才恢复）。固定长度模式不依赖这条路径。
            // Re-issue the size request as soon as the line changes: in auto mode the bar length follows the content, and waiting for the
            // next snapshot would render the new line at the previous width and briefly show an ellipsis at its right edge. A fixed box does
            // not depend on this path.
            var secondaryText = string.IsNullOrEmpty(presentation.CurrentTranslation) ? presentation.Next : presentation.CurrentTranslation;
            if (!string.Equals(_lastSizeLyricText, next.Current, StringComparison.Ordinal) ||
                !string.Equals(_lastSizeSecondaryText, secondaryText, StringComparison.Ordinal) ||
                (previous.ScrollProgress is null) != (presentation.ScrollProgress is null))
            {
                _lastSizeLyricText = next.Current;
                _lastSizeSecondaryText = secondaryText;
                // 跳过宿主的长度过渡：过渡期间新句会按旧宽度渲染、右端被省略号截断，而歌词换行是离散切换。
                // Skip the host's length transition: during it the new line renders at the previous width and gets clipped with an
                // ellipsis, while a lyric line change is a discrete switch.
                _owner.RaiseDesiredSizeChanged(skipTransition: true);
            }
        }

        /// <summary>
        /// 两帧显示的内容是否不同：用户看得见的文本、行结构，以及"有没有逐字擦亮"这一模式。进度数值本身永远不算，
        /// 否则每一帧都会被当成变化。设置变更靠它决定要不要重发，重发时会走换行过渡。
        /// Whether the two frames show different content: the visible text, the row structure, and whether syllable
        /// highlighting is in play at all. The progress value itself never counts, or every frame would look changed.
        /// Settings changes use it to decide whether anything has to be re-sent, and a re-send rolls like a line change.
        /// </summary>
        private static bool VisibleContentChanged(LyricsPresentationFrame previous, LyricsPresentationFrame next) =>
            previous.IsVisible != next.IsVisible ||
            previous.TranslationMode != next.TranslationMode ||
            (previous.WordScanProgress is null) != (next.WordScanProgress is null) ||
            !string.Equals(previous.Current, next.Current, StringComparison.Ordinal) ||
            !string.Equals(previous.Next, next.Next, StringComparison.Ordinal) ||
            !string.Equals(previous.CurrentTranslation, next.CurrentTranslation, StringComparison.Ordinal) ||
            !string.Equals(previous.NextTranslation, next.NextTranslation, StringComparison.Ordinal);

        internal void RefreshWebLyricsTimer()
        {
            // 注意用快照的播放状态而不是 _lyricsFrame.IsPlaying：隐藏帧是同一个 Hidden 单例，它的 IsPlaying 恒为 false，
            // 拿它判断会让"等待第一句"期间的定时器永远起不来。
            // Note it reads the snapshot's playback state instead of _lyricsFrame.IsPlaying: a hidden frame is the same
            // Hidden singleton whose IsPlaying is always false, so judging by it would keep the timer from ever starting
            // while waiting for the first line.
            var shouldRun = !_webLyricsGraphicsDisabled && LyricsPresentationRefreshPolicy.ShouldRunTimer(
                _owner.IsLoaded,
                _owner.IsAdvancePruned,
                _lyricsFrame.IsVisible,
                _owner._snapshot.IsPlaying,
                _owner.PresentationSettings.LyricsEnabled,
                _owner._snapshot.IsConnected,
                _owner._snapshot.Lyrics?.Document.Lines?.Count ?? 0);
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

        internal void ApplyWebLyricsStyle() => ApplyWebLyricsStyle(TimeSpan.Zero);

        private void ApplyWebLyricsStyle(TimeSpan colorDuration, bool finishColorTransition = false)
        {
            if (_lyricsWebRenderer is null)
                return;

            var appearance = _owner.PresentationSettings.Appearance.Normalize();
            var alignment = _owner.PresentationSettings.LyricsTextAlignment switch
            {
                LyricsTextAlignment.Left => "Left",
                LyricsTextAlignment.Right => "Right",
                _ => "Center"
            };
            var primary = ToCssColor(_lyricsForeground, 1);
            var secondaryOpacity = SystemParameters.HighContrast ? 1 : 0.68;
            var unsungOpacity = SystemParameters.HighContrast
                ? 1
                : LyricsUnsungOpacity.Normalize(_owner.PresentationSettings.LyricsUnsungOpacityPercent) / 100d;
            var textShadow = _lyricsNeedsContrastShadow
                ? _lyricsUsesLightText
                    ? "1px 1px 1px rgba(0, 0, 0, 0.85)"
                    : "1px 1px 1px rgba(255, 255, 255, 0.85)"
                : "none";

            _lyricsWebRenderer.ApplyStyle(new LyricsWebStyle(
                appearance.ResolveFontFamilySource(SystemFonts.MessageFontFamily.Source),
                appearance.ResolveLatinFontFamily(SystemFonts.MessageFontFamily.Source),
                appearance.ResolveCjkFontFamily(SystemFonts.MessageFontFamily.Source),
                _owner.EffectiveLyricFontSize,
                appearance.FontWeight,
                LyricsCharacterSpacing.Normalize(_owner.PresentationSettings.LyricsCharacterSpacingPercent),
                LyricsLineGap.Normalize(_owner.PresentationSettings.LyricsLineGapPercent),
                alignment,
                primary,
                ToCssColor(_lyricsForeground, secondaryOpacity),
                ToCssColor(_lyricsForeground, secondaryOpacity),
                ToCssColor(_lyricsForeground, unsungOpacity),
                primary,
                textShadow,
                _owner._isVertical,
                _owner.VerticalSecondaryFontSize,
                _owner.PresentationSettings.TaskbarExperience.MediaTextAlignment.ToString(),
                !_owner.CurrentMotion.UseContinuousMotion,
                _owner.UsesAdaptiveTaskbarText && _owner._textBudget.HideSecondary), colorDuration, finishColorTransition);
        }

        internal static string ToCssColor(Brush brush, double opacity)
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
        internal const double WebLyricsHorizontalInsetDip = 16;

        internal double MeasureWebLyricWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var sourceSize = Math.Max(1, _owner.SongArtist.FontSize);
            var targetSize = _owner.EffectiveLyricFontSize;
            var width = TaskBarMediaControl.MeasureTextWidthExact(text, _owner.SongArtist) * targetSize / sourceSize + WebLyricsHorizontalInsetDip;

            // Web 歌词用 CSS letter-spacing 拉开字距：每个字符（含空格与末尾字符）都会多出
            // "字号 × 百分比" 的推进量。自动尺寸必须补上同样的宽度，否则拉大字距后整行会超出
            // 申请到的文字区，右侧被省略号截断——即使任务栏还有空闲空间。
            // The web lyrics widen with CSS letter-spacing: every character (spaces and the final one included) advances by
            // "font size × percent". The auto-size has to add the same amount, otherwise a wider spacing overflows the text
            // area that was requested without it and the right side is clipped with an ellipsis even though the taskbar still
            // has free room. Measured against the real page: width = base + characters × percent × font size.
            var percent = LyricsCharacterSpacing.Normalize(_owner.PresentationSettings.LyricsCharacterSpacingPercent);
            if (percent > 0)
            {
                width += text.Length * targetSize * percent / 100.0;
            }

            return width;
        }

        internal void SetWebLyricsAppearance(Brush foreground, bool needsContrastShadow, bool usesLightText, TimeSpan colorDuration = default)
        {
            _lyricsForeground = foreground;
            _lyricsNeedsContrastShadow = needsContrastShadow;
            _lyricsUsesLightText = usesLightText;
            ApplyWebLyricsStyle(colorDuration);
        }

        internal void FinishColorTransition() => ApplyWebLyricsStyle(TimeSpan.Zero, finishColorTransition: true);

        internal void StopWebLyrics()
        {
            _lyricsWebTimer.Stop();
            _lyricsReleaseTimer.Stop();
            _lyricsRetryTimer.Stop();
            ReleaseWebLyricsRenderer();
            _lyricsCreationFailures = 0;
        }

        internal void ReleaseWebLyricsRenderer()
        {
            _lyricsGraphicsRecoveryQueue?.Cancel();
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
            try { _owner.LyricsWebViewHost.Child = null; }
            finally
            {
                try { renderer?.Dispose(); }
                finally
                {
                    _owner.SongLyricsPanel.Opacity = 0;
                    _owner.SongLyricsPanel.IsHitTestVisible = false;
                    _owner.SongMetadataPanel.Visibility = Visibility.Visible;
                }
            }
        }

        /// <summary>全局异常处理识别出 WebView2 图形层故障后的回调：在新一帧里执行重建，避免在异常展开的调用栈里改动可视树。
        /// Called after the global exception handler identifies a WebView2 graphics fault: the rebuild runs on a fresh
        /// dispatcher frame so the visual tree is never mutated inside the unwinding exception stack.</summary>
        internal bool OnWebLyricsGraphicsRecoveryRequested(WebLyricsRecoveryAction action)
        {
            if (!_lyricsHostLoaded || _owner.Dispatcher.HasShutdownStarted || _owner.Dispatcher.HasShutdownFinished ||
                (_lyricsWebRenderer is null && action != WebLyricsRecoveryAction.DisableForSession)) return false;
            var renderer = _lyricsWebRenderer;
            var generation = _lyricsRendererGeneration;
            return _lyricsGraphicsRecoveryQueue?.TryQueue(action,
                () => _lyricsHostLoaded && generation == _lyricsRendererGeneration && ReferenceEquals(renderer, _lyricsWebRenderer),
                pending =>
            {
                if (pending == WebLyricsRecoveryAction.DisableForSession || WebLyricsGraphicsRecovery.Session.IsDisabled)
                    DisableWebLyricsGraphics();
                else if (!_webLyricsGraphicsDisabled)
                    RecoverWebLyricsGraphics();
            }) == true;
        }

        internal void DisableWebLyricsGraphics()
        {
            _graphicsRecoveryPending = false;
            if (!_webLyricsGraphicsDisabled)
                AppLogService.Current?.Info("Lyrics", $"本宿主已停用 Web 歌词，退回元数据: generation={_lyricsRendererGeneration}");
            _webLyricsGraphicsDisabled = true;
            StopWebLyrics();
            _owner.SongLyricsPanel.Visibility = Visibility.Collapsed;
            UpdateWebLyricsPresentation(allowTransition: false);
            _owner.RaiseDesiredSizeChanged(isForcedRefresh: true);
            WebLyricsGraphicsRecovery.Session.ReportDegraded();
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
        internal void RecoverWebLyricsGraphics()
        {
            if (!WebView2GraphicsFaultPolicy.CanRecover(_lyricsGraphicsRecoveries))
            {
                // A transparent WebView still participates in WPF layout and may throw the same
                // graphics fault again. Detach it before disposing the renderer.
                DisableWebLyricsGraphics();
                AppLogService.Current?.Warn(
                    "Lyrics",
                    $"WebView2 图形层故障已达重建上限（{WebView2GraphicsFaultPolicy.MaximumRecoveries} 次），本次会话停用 Web 歌词并退回元数据");
                return;
            }

            _lyricsGraphicsRecoveries++;
            _graphicsRecoveryPending = true;
            AppLogService.Current?.Warn(
                "Lyrics",
                $"WebView2 图形层故障，正在重建歌词视图（第 {_lyricsGraphicsRecoveries}/{WebView2GraphicsFaultPolicy.MaximumRecoveries} 次）");

            ReleaseWebLyricsRenderer();
            _lyricsCreationFailures = 0;
            UpdateWebLyricsPresentation(allowTransition: false);
        }


    }
}
