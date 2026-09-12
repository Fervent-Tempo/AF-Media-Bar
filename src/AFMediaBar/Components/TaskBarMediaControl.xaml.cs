using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AFMediaBar.Components
{
    /// <summary>播放器表面滚轮及鼠标按键状态。 / Wheel delta and mouse-button state on a player surface.</summary>
    public sealed class PlayerSurfaceWheelEventArgs(
        int delta,
        bool isLeftButtonDown,
        bool isRightButtonDown) : EventArgs
    {
        public int Delta { get; } = delta;
        public bool IsLeftButtonDown { get; } = isLeftButtonDown;
        public bool IsRightButtonDown { get; } = isRightButtonDown;
    }

    /// <summary>
    /// 任务栏媒体控制组件：显示当前播放媒体的信息和封面，响应用户交互。
    /// Taskbar media control component: displays currently playing media info and artwork, responds to user interactions.
    ///
    /// 职责 Responsibilities:
    /// 1. 接收 MediaSnapshot 并更新 UI（标题、艺术家、封面、歌词）
    ///    Receive MediaSnapshot and update UI (title, artist, artwork, lyrics)
    /// 2. 根据任务栏方向（横向/竖向）和大小调整布局
    ///    Adjust layout based on taskbar orientation (horizontal/vertical) and size
    /// 3. 显示当前歌词行（歌词可用时替换标题）
    ///    Display current lyric line (replaces title when lyrics are available)
    /// 4. 处理悬停效果和动画
    ///    Handle hover effects and animations
    ///
    /// ⚠️ 架构约束 Architecture Constraints:
    /// - 此组件只负责 UI 呈现，不包含业务逻辑
    ///   This component is responsible for UI presentation only, no business logic
    /// - 用户操作通过请求事件交给宿主窗口，再由 MainWindowViewModel 执行
    ///   User actions are raised as request events and executed by the host through MainWindowViewModel
    /// - 不直接调用服务，所有数据通过 UpdateSongInfo 方法传入
    ///   Does not call services directly; all data is passed via UpdateSongInfo method
    /// </summary>
    public partial class TaskBarMediaControl : UserControl
    {
        // === 布局渲染引擎 Layout Render Engine ===
        private LayoutRenderEngine? _layoutEngine;
        private WindowMode _currentMode = WindowMode.Taskbar;  // 当前窗口模式 Current window mode

        /// <summary>
        /// 调用 TaskBarMediaControl，提供 API。
        /// Provides the public TaskBarMediaControl entry point required by this component.
        /// </summary>
        public TaskBarMediaControl()
        {
            InitializeComponent();

            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _progressTimer.Tick += (_, _) => UpdateTaskbarProgress();
            _hoverOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _hoverOpenTimer.Tick += (_, _) =>
            {
                _hoverOpenTimer.Stop();
                ShowTaskbarHoverLayer();
            };
            _hoverCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _hoverCloseTimer.Tick += (_, _) =>
            {
                _hoverCloseTimer.Stop();
                HideTaskbarHoverLayer();
            };
            Loaded += (_, _) => _progressTimer.Start();
            Unloaded += (_, _) =>
            {
                _progressTimer.Stop();
                _hoverOpenTimer.Stop();
                _hoverCloseTimer.Stop();
            };

            // 计时器必须先于布局初始化；布局会立即应用已持久化的悬停层开关。
            // Timers must exist before layout initialization, which immediately applies persisted hover settings.
            InitializeLayoutEngine();
        }

        // === 内部状态缓存 Internal State Cache ===
        private string _actualTitle = string.Empty;   // 实际标题（不含歌词）Actual title (without lyrics)
        private string _actualArtist = string.Empty;  // 实际艺术家 Actual artist

        private bool _isPaused;        // 是否暂停 Whether paused
        private bool _isConnected;
        private bool _isVertical;      // 任务栏是否竖向 Whether taskbar is vertical
        private bool _isSmallTaskbar;  // 是否小任务栏 Whether taskbar is small
        private bool _canPlayPause;
        private bool _canSkipPrevious;
        private bool _canSkipNext;
        private string _activeLyric = string.Empty;
        private string _nextLyric = string.Empty;
        private string _translatedLyric = string.Empty;
        private string _secondaryLyric = string.Empty;
        private string _lastSizeFingerprint = string.Empty;
        private readonly DispatcherTimer _progressTimer;
        private readonly DispatcherTimer _hoverOpenTimer;
        private readonly DispatcherTimer _hoverCloseTimer;
        private MediaSnapshot _snapshot = MediaSnapshot.Disconnected;
        private bool _isTaskbarHoverVisible;
        private bool _isSeeking;
        private DateTime _suppressArtworkClickUntilUtc;
        private const double TaskbarSpectrumWidth = 38;
        private const double TaskbarTrailingMargin = 4;
        private const double TaskbarCoveredBlurRadius = 4;
        private const double TaskbarCoveredOpacity = 0.32;

        public event EventHandler? TogglePlayPauseRequested;
        public event EventHandler? SkipPreviousRequested;
        public event EventHandler? SkipNextRequested;
        public event EventHandler? ActivateSourceRequested;
        public event EventHandler? OpenFullPanelRequested;
        public event EventHandler? AudioControlRequested;
        public event EventHandler? OutputDeviceCycleRequested;
        public event Action<double>? SeekRequested;
        public event EventHandler<PlayerSurfaceWheelEventArgs>? WheelRequested;
        public event EventHandler<MediaBarSizeRequestEventArgs>? DesiredSizeChanged;

        // === 歌词显示状态 Lyrics Display State ===
        // 解析后的行缓存 + 当前行下标，避免每个快照重复解析。
        // Parsed line cache + current line index to avoid re-parsing on every snapshot.
        private readonly LyricLinePresenter _lyricPresenter = new();

        /// <summary>
        /// 初始化布局渲染引擎：将 MainBorder 和 BackgroundImage 传入引擎以便动态调整布局。
        /// Initialize layout render engine: pass MainBorder and BackgroundImage to engine for dynamic layout adjustment.
        /// </summary>
        private void InitializeLayoutEngine()
        {
            _layoutEngine = new LayoutRenderEngine(
                mainBorder: MainBorder,
                contentCanvas: MainCanvas,
                backgroundImage: BackgroundImage,
                artworkBorder: SongImageBorder,
                songInfoPanel: SongInfoStackPanel,
                artworkPlaceholder: SongImagePlaceholder,
                songTitle: SongTitle,
                songArtist: SongArtist,
                songTitleContainer: SongTitleContainer,
                songArtistContainer: SongArtistContainer,
                songLyrics: SongLyrics,
                songLyricsContainer: SongLyricsContainer
            );

            // 应用默认布局（任务栏横向）
            // Apply default layout (taskbar horizontal)
            ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);
        }

        /// <summary>
        /// 应用布局：根据窗口模式和方向选择并应用对应的布局配置。
        /// Apply layout: select and apply corresponding layout config based on window mode and orientation.
        /// </summary>
        /// <param name="mode">窗口模式（任务栏/灵动岛）/ Window mode (taskbar/dynamic island)</param>
        /// <param name="orientation">布局方向（横向/竖向）/ Layout orientation (horizontal/vertical)</param>
        public void ApplyLayout(WindowMode mode, LayoutOrientation orientation)
        {
            ApplyLayout(
                mode,
                orientation,
                SettingsManager.Current.LayoutLengthScalePercent,
                SettingsManager.Current.LayoutThicknessScalePercent);
        }

        /// <summary>
        /// 使用宿主解析后的缩放百分比应用布局。
        /// Applies layout with scale percentages resolved by the host.
        /// </summary>
        /// <param name="mode">窗口模式 / Window mode</param>
        /// <param name="orientation">布局方向 / Layout orientation</param>
        /// <param name="lengthScalePercent">主轴长度缩放百分比 / Primary-axis length scale percentage</param>
        /// <param name="thicknessScalePercent">横轴厚度缩放百分比 / Cross-axis thickness scale percentage</param>
        public void ApplyLayout(
            WindowMode mode,
            LayoutOrientation orientation,
            double lengthScalePercent,
            double thicknessScalePercent)
        {
            _currentMode = mode;

            // 从预设中获取布局
            // Get layout from presets
            var layout = LayoutPresets.GetLayout(mode, orientation);

            // 应用布局
            // Apply layout
            _layoutEngine?.ApplyLayout(
                layout,
                lengthScalePercent / 100.0,
                thicknessScalePercent / 100.0);

            // 更新内部状态标志以保持兼容
            // Update internal state flags to maintain compatibility
            _isVertical = orientation == LayoutOrientation.Vertical;
            ApplyTaskbarExperienceSettings();
            ApplyTaskbarSectionGeometry(MainBorder.Width);
            RaiseDesiredSizeChanged();
        }

        /// <summary>
        /// 获取当前应用的布局配置。
        /// Get currently applied layout configuration.
        /// </summary>
        public LayoutSchema? CurrentLayout => _layoutEngine?.CurrentLayout;

        /// <summary>应用自动计算的主轴长度。/ Applies an auto-calculated primary-axis length.</summary>
        public void ApplyPrimaryLength(double primaryLength)
        {
            _layoutEngine?.ApplyPrimaryLength(primaryLength);
            ApplyTaskbarSectionGeometry(primaryLength);
        }

        /// <summary>在宿主更新方向或缩放状态后重新发布尺寸请求。/ Re-raises the size request after the host updates orientation or scale state.</summary>
        public void RefreshDesiredSize() => RaiseDesiredSizeChanged();

        /// <summary>
        /// 应用横向任务栏的层级和交互设置，不改变原有封面、文字或布局引擎。
        /// Applies horizontal-taskbar layer and interaction settings without replacing the original artwork, text, or layout engine.
        /// </summary>
        public void ApplyTaskbarExperienceSettings()
        {
            var isHorizontalTaskbar = _currentMode == WindowMode.Taskbar && !_isVertical;
            var experience = SettingsManager.Current.TaskbarExperience.Normalize();
            var interaction = SettingsManager.Current.Interaction.Normalize();
            var metrics = TaskbarDensityMetrics.From(experience.Density);
            var transportVisible = interaction.Mode != MediaInteractionMode.Gestures;
            var progressVisible = _snapshot.Duration > 0;

            TaskbarSpectrumHoverSurface.Visibility = isHorizontalTaskbar ? Visibility.Visible : Visibility.Collapsed;
            TaskbarRestProgress.Visibility = isHorizontalTaskbar && progressVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            TaskbarTransportButtons.Visibility = transportVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            TaskbarHoverProgress.Visibility = progressVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            TaskbarHoverProgress.IsEnabled = _snapshot.CanSeek;
            TaskbarFullPanelHandle.Visibility = experience.FullLayerEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
            var directFullPanelHandleVisible = isHorizontalTaskbar &&
                                               !experience.HoverLayerEnabled &&
                                               experience.FullLayerEnabled;
            TaskbarDirectFullPanelHandle.Visibility = directFullPanelHandleVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            TaskbarDirectFullPanelHandle.IsHitTestVisible = directFullPanelHandleVisible;
            if (!directFullPanelHandleVisible)
                AnimateDirectFullPanelHandle(false, immediate: true);
            else
                AnimateDirectFullPanelHandle(
                    SongInfoStackPanel.IsMouseOver || TaskbarDirectFullPanelHandle.IsMouseOver,
                    immediate: true);

            foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(TaskbarHoverActions))
            {
                if (ReferenceEquals(button, TaskbarFullPanelHandle))
                    continue;
                button.Width = metrics.ButtonSize;
                button.Height = metrics.ButtonSize;
            }
            TaskbarHoverProgress.Width = metrics.ProgressWidth;
            TaskbarHoverLayer.Height = metrics.HoverLayerHeight;
            ApplyTaskbarSectionGeometry(MainBorder.Width);

            var lyricsAlignment = SettingsManager.Current.LyricsTextAlignment switch
            {
                LyricsTextAlignment.Left => TextAlignment.Left,
                LyricsTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Center
            };
            SongMetadataPanel.Orientation = Orientation.Vertical;
            SongArtistContainer.Margin = new Thickness(0, -1.5, 0, 0);
            var metadataAlignment = experience.ContentLayout == TaskbarContentLayout.CenteredStack
                ? TextAlignment.Center
                : TextAlignment.Left;
            SongTitle.TextAlignment = metadataAlignment;
            SongArtist.TextAlignment = metadataAlignment;
            SongLyrics.TextAlignment = lyricsAlignment;
            SongLyricsSecondary.TextAlignment = lyricsAlignment;
            if (isHorizontalTaskbar && SongMetadataPanel.Visibility == Visibility.Visible)
            {
                if (experience.ContentLayout == TaskbarContentLayout.CompactInline &&
                    !string.IsNullOrEmpty(_actualArtist))
                {
                    SongTitle.Text = string.IsNullOrEmpty(_actualTitle)
                        ? _actualArtist
                        : $"{_actualTitle} · {_actualArtist}";
                    SongArtistContainer.Visibility = Visibility.Collapsed;
                }
                else
                {
                    SongTitle.Text = _actualTitle;
                    SongArtistContainer.Visibility = !_isSmallTaskbar && !string.IsNullOrEmpty(_actualArtist)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
            else if (!isHorizontalTaskbar)
            {
                SongTitle.Text = _actualTitle;
            }

            if (!isHorizontalTaskbar || !experience.HoverLayerEnabled)
                HideTaskbarHoverLayer(immediate: true);
            if (!isHorizontalTaskbar)
            {
                AnimateComponentHover(SongImageHoverOverlay, false);
                AnimateComponentHover(SongInfoHoverOverlay, false);
                AnimateComponentHover(TaskbarSpectrumHoverSurface, false);
            }

            RaiseDesiredSizeChanged();
        }

        /// <summary>
        /// 将统一密度间隔应用到封面、文字和频谱，并让悬停控件只占据文字区域。
        /// Applies one density-controlled gap to artwork, text, and spectrum while constraining hover controls to the text region.
        /// </summary>
        private void ApplyTaskbarSectionGeometry(double primaryLength)
        {
            if (_currentMode != WindowMode.Taskbar || _isVertical || !double.IsFinite(primaryLength))
                return;

            var metrics = TaskbarDensityMetrics.From(SettingsManager.Current.TaskbarExperience.Density);
            var artworkRight = GetTaskbarArtworkRight();
            var textLeft = artworkRight + metrics.SectionGap;
            var reservedRight = metrics.SectionGap + TaskbarSpectrumWidth + TaskbarTrailingMargin;
            var textWidth = Math.Max(0, primaryLength - textLeft - reservedRight);
            var textTop = Canvas.GetTop(SongInfoStackPanel);
            if (!double.IsFinite(textTop))
                textTop = 0;

            Canvas.SetLeft(SongInfoStackPanel, textLeft);
            SongInfoStackPanel.Width = textWidth;
            SongInfoSurface.Width = textWidth;
            SongTitleContainer.Width = textWidth;
            SongArtistContainer.Width = textWidth;
            SongLyricsContainer.Width = textWidth;
            SongLyricsSecondaryContainer.Width = textWidth;
            SongTitle.Width = textWidth;
            SongArtist.Width = textWidth;
            SongLyrics.Width = textWidth;
            SongLyricsSecondary.Width = textWidth;

            Canvas.SetLeft(SongInfoHoverOverlay, textLeft);
            Canvas.SetTop(SongInfoHoverOverlay, textTop);
            SongInfoHoverOverlay.Width = textWidth;
            SongInfoHoverOverlay.Height = SongInfoStackPanel.Height;

            TaskbarRestProgress.Margin = new Thickness(textLeft, 0, reservedRight, 1);
            TaskbarSpectrumHoverSurface.Width = TaskbarSpectrumWidth;
            TaskbarSpectrumHoverSurface.Margin = new Thickness(0, 0, TaskbarTrailingMargin, 0);

            HoverRevealHost.Margin = new Thickness(textLeft, 1, 0, 1);
            HoverRevealHost.Height = Math.Max(0, MainBorder.Height - 2);
            TaskbarHoverLayer.Width = textWidth;
            TaskbarDirectFullPanelHandle.Width = textWidth;
            TaskbarDirectFullPanelHandle.Margin = new Thickness(textLeft, 1, 0, 0);
            if (HoverRevealHost.Visibility == Visibility.Visible)
            {
                HoverRevealHost.BeginAnimation(FrameworkElement.WidthProperty, null);
                HoverRevealHost.Width = textWidth;
            }
        }

        private double GetTaskbarArtworkRight()
        {
            var artworkLeft = Canvas.GetLeft(SongImageBorder);
            if (!double.IsFinite(artworkLeft))
                artworkLeft = 0;
            return artworkLeft + Math.Max(0, SongImageBorder.Width);
        }

        /// <summary>把九段频谱值应用到任务栏静置层。 / Applies nine spectrum-band values to the taskbar rest layer.</summary>
        public void ApplySpectrum(ReadOnlySpan<float> bands)
        {
            for (var index = 0; index < TaskbarSpectrum.Children.Count; index++)
            {
                if (TaskbarSpectrum.Children[index] is Border bar)
                    bar.Height = 3 + Math.Clamp(index < bands.Length ? bands[index] : 0, 0, 1) * 18;
            }
        }

        /// <summary>当前是否有已连接且正在播放的媒体。/ Indicates whether connected media is currently playing.</summary>
        public bool IsPlaying => _isConnected && !_isPaused;

        /// <summary>
        /// 设置竖向模式：任务栏在屏幕左侧或右侧时调整布局。
        /// Set vertical mode: adjust layout when taskbar is on screen left or right edge.
        /// </summary>
        public void SetVerticalMode(bool isVertical)
        {
            // 使用新的布局系统
            // Use new layout system
            var orientation = isVertical ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
            ApplyLayout(_currentMode, orientation);

            // 兼容性：更新可见性（布局系统已处理尺寸）
            // Compatibility: update visibility (layout system handles sizing)
            SongInfoStackPanel.Visibility = isVertical ? Visibility.Collapsed : Visibility.Visible;
            SongArtistContainer.Visibility = !_isSmallTaskbar && !isVertical && !string.IsNullOrEmpty(_actualArtist)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// 设置小任务栏模式：任务栏高度较小时隐藏艺术家信息。
        /// Set small taskbar mode: hide artist info when taskbar height is small.
        /// </summary>
        public void SetSmallTaskbarMode(bool isSmallTaskbar)
        {
            _isSmallTaskbar = isSmallTaskbar;
            SongArtistContainer.Visibility = !isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(_actualArtist)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// 应用播放器文字、可读性背景和灵动岛背景设置。
        /// Applies player text, readability background, and dynamic-island background settings.
        /// </summary>
        public void ApplyAppearanceSettings()
        {
            var appearance = SettingsManager.Current.Appearance.Normalize();
            var appTheme = ApplicationThemeManager.GetAppTheme();
            var isDark = appTheme == ApplicationTheme.Dark;
            if (appTheme == ApplicationTheme.Unknown)
            {
                WindowsThemeDetector.GetWindowsTheme(out var windowsAppTheme, out _);
                isDark = windowsAppTheme == WindowsThemeDetector.ThemeMode.Dark;
            }
            var usesLightText = appearance.PlayerForegroundMode switch
            {
                PlayerForegroundMode.LightText => true,
                PlayerForegroundMode.DarkText => false,
                _ => isDark
            };

            Brush foreground;
            Brush readabilityBackground;
            if (SystemParameters.HighContrast)
            {
                foreground = SystemColors.WindowTextBrush;
                readabilityBackground = appearance.EnhancedReadability && _isConnected
                    ? SystemColors.WindowBrush
                    : Brushes.Transparent;
            }
            else
            {
                foreground = new SolidColorBrush(usesLightText
                    ? Colors.White
                    : Color.FromArgb(0xE4, 0x1C, 0x1C, 0x1C));
                readabilityBackground = appearance.EnhancedReadability && _isConnected
                    ? new SolidColorBrush(usesLightText
                        ? Color.FromArgb(0x78, 0x00, 0x00, 0x00)
                        : Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF))
                    : Brushes.Transparent;
            }

            SongTitle.Foreground = foreground;
            SongLyrics.Foreground = foreground;
            SongLyricsSecondary.Foreground = foreground;
            SongLyricsSecondary.Opacity = SystemParameters.HighContrast ? 1 : 0.68;
            SongArtist.Foreground = foreground;
            SongInfoStackPanel.Background = readabilityBackground;

            if (_currentMode == WindowMode.Taskbar)
            {
                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                TopBorder.BorderBrush = Brushes.Transparent;
                BackgroundImage.Visibility = Visibility.Collapsed;
                return;
            }

            if (_currentMode != WindowMode.DynamicIsland)
                return;

            MainBorder.Background = SettingsManager.Current.DynamicIslandBackgroundMode == DynamicIslandBackgroundMode.Transparent
                ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
                : SystemParameters.HighContrast
                    ? SystemColors.WindowBrush
                    : new SolidColorBrush(isDark
                        ? Color.FromArgb(0xFF, 0x20, 0x20, 0x20)
                        : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3));
        }


        /// <summary>
        /// 更新歌曲信息：根据快照更新 UI 的所有元素（标题、艺术家、封面、歌词、播放状态）。
        /// Update song info: updates all UI elements based on snapshot (title, artist, artwork, lyrics, playback state).
        ///
        /// 算法 Algorithm:
        /// 1. 断开状态：显示占位符图标，清空所有信息
        ///    Disconnected: show placeholder icon, clear all info
        /// 2. 连接状态：更新标题、艺术家、封面、歌词
        ///    Connected: update title, artist, artwork, lyrics
        /// 3. 封面存在时根据播放/暂停状态显示不同图标
        ///    Show different icon based on play/pause state when artwork exists
        /// 4. 歌词可用时用当前行替换标题显示
        ///    Replace title with current lyric line when lyrics are available
        /// </summary>
        public void UpdateSongInfo(MediaSnapshot snapshot)
        {
            _snapshot = snapshot;
            if (!snapshot.IsConnected)
            {
                // 无媒体播放 - 显示占位符保持媒体栏可见
                // No media playing - show the placeholder text so the media bar stays visible
                Dispatcher.Invoke(() =>
                {
                    _actualTitle = string.Empty;
                    _actualArtist = string.Empty;
                    _isConnected = false;
                    _canPlayPause = false;
                    _canSkipPrevious = false;
                    _canSkipNext = false;
                    _lyricPresenter.Update(null, 0);
                    _activeLyric = string.Empty;
                    _nextLyric = string.Empty;
                    _translatedLyric = string.Empty;
                    _secondaryLyric = string.Empty;

                    SongTitle.Text = _actualTitle;
                    SongLyrics.Text = string.Empty;
                    SongLyricsSecondary.Text = string.Empty;
                    SongMetadataPanel.Visibility = Visibility.Visible;
                    SongLyricsPanel.Visibility = Visibility.Collapsed;
                    SongArtist.Text = _actualArtist;
                    SongInfoStackPanel.Visibility = Visibility.Visible;
                    SongInfoStackPanel.ToolTip = string.Empty;
                    SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.ImageSource = null;
                    BackgroundImage.Source = null;
                    BackgroundImage.Visibility = Visibility.Collapsed;
                    SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover
                    TaskbarPreviousButton.IsEnabled = false;
                    TaskbarPlayPauseButton.IsEnabled = false;
                    TaskbarNextButton.IsEnabled = false;
                    TaskbarHoverProgress.IsEnabled = false;
                    HideTaskbarHoverLayer(immediate: true);
                    UpdateTaskbarProgress();
                    ApplyTaskbarExperienceSettings();

                    // 任务栏无媒体时保持完全透明；灵动岛保留布局定义的稳定背景。
                    // Keep the disconnected taskbar transparent; preserve the dynamic-island layout background.
                    if (_currentMode == WindowMode.Taskbar)
                    {
                        MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                        MainBorder.Background.Opacity = 0;
                        TopBorder.BorderBrush = Brushes.Transparent;
                    }

                    Visibility = Visibility.Visible;
                    RaiseDesiredSizeChanged(isResetToPreset: true);
                });
                return;
            }

            _isPaused = !snapshot.IsPlaying;
            _isConnected = true;
            _canPlayPause = snapshot.CanPlayPause;
            _canSkipPrevious = snapshot.CanSkipPrevious;
            _canSkipNext = snapshot.CanSkipNext;

            Dispatcher.Invoke(() =>
            {
                string newTitle = !string.IsNullOrEmpty(snapshot.Title) ? snapshot.Title : "-";
                string newArtist = !string.IsNullOrWhiteSpace(snapshot.Artist)
                    ? snapshot.Artist
                    : !string.IsNullOrWhiteSpace(snapshot.SourceName) ? snapshot.SourceName : "-";

                // 标题或艺术家变化时触发入场动画
                // Trigger entrance animation when title or artist changes
                if (_actualTitle != newTitle || _actualArtist != newArtist)
                {
                    AnimateEntrance();

                    _actualTitle = newTitle;
                    _actualArtist = newArtist;

                    SongTitle.Text = _actualTitle;
                    SongArtist.Text = _actualArtist;
                }

                // 歌词可用时标题位置显示当前歌词行（随快照位置推进）
                // Show current lyric line in title slot when lyrics are available (advances with snapshot position)
                UpdateLyricLine(snapshot);

                // 更新工具提示显示完整歌曲信息
                // Update tooltip with full song info
                SongInfoStackPanel.ToolTip = string.Empty;
                SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(snapshot.Title) ? snapshot.Title : string.Empty;
                SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(snapshot.Artist) ? "\n\n" + snapshot.Artist : string.Empty;

                // 根据主色调改变图标颜色（从封面提取）
                // Change icon color based on dominant color (extracted from artwork)
                SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0
                    ? BitmapHelper.SavedDominantColors.Last()
                    : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
                SongImagePlaceholder.Foreground = brush;

                if (snapshot.Artwork is not null)
                {
                    if (_isPaused)
                    {
                        // show pause icon overlay
                        SongImagePlaceholder.Symbol = SymbolRegular.Pause24;
                        SongImagePlaceholder.Visibility = Visibility.Visible;
                        SongImage.Opacity = 0.4;
                    }
                    else
                    {
                        SongImagePlaceholder.Visibility = Visibility.Collapsed;
                        SongImage.Opacity = 1;
                    }

                    SongImage.ImageSource = snapshot.Artwork;
                    BackgroundImage.Source = snapshot.Artwork;
                    SongImageBorder.Margin = new Thickness(0, 0, 0, -2); // align image better when cover is present
                }
                else
                {
                    SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.ImageSource = null;
                    BackgroundImage.Source = null;
                }

                ApplyLyricPresentation();
                SongArtistContainer.Visibility = !_isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(_actualArtist)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                SongInfoStackPanel.Visibility = _isVertical ? Visibility.Collapsed : Visibility.Visible;
                // 任务栏主体保持透明；灵动岛继续沿用布局引擎已有背景行为。
                // Keep the taskbar body transparent; the island retains its existing layout-engine background behavior.
                BackgroundImage.Visibility = Visibility.Collapsed;

                TaskbarPreviousButton.IsEnabled = _canSkipPrevious;
                TaskbarPlayPauseButton.IsEnabled = _canPlayPause;
                TaskbarNextButton.IsEnabled = _canSkipNext;
                TaskbarPlayPauseIcon.Symbol = _isPaused ? SymbolRegular.Play24 : SymbolRegular.Pause24;
                UpdateTaskbarProgress();
                ApplyTaskbarExperienceSettings();

                Visibility = Visibility.Visible;
                RaiseDesiredSizeChanged();
            });
        }

        /// <summary>
        /// 用快照中的歌词和位置更新标题区域的当前行；无歌词时恢复标题。
        /// Shows the active lyric line in the title slot from the snapshot; restores the title when lyrics are absent.
        /// </summary>
        private void UpdateLyricLine(MediaSnapshot snapshot)
        {
            var update = _lyricPresenter.Update(snapshot.Lyrics, snapshot.Position);
            _activeLyric = update.Text;
            _nextLyric = update.NextText;
            _translatedLyric = update.TranslationText;
            SongTitle.Text = _actualTitle;
        }

        private void ApplyLyricPresentation()
        {
            var settings = SettingsManager.Current;
            var showLyrics = settings.LyricsEnabled && !string.IsNullOrEmpty(_activeLyric);
            _secondaryLyric = settings.LyricsSecondaryLineMode == LyricsSecondaryLineMode.Translation
                ? _translatedLyric
                : _nextLyric;
            var showSecondary = showLyrics &&
                                settings.TwoLineLyricsEnabled &&
                                !string.IsNullOrEmpty(_secondaryLyric);

            SongMetadataPanel.Visibility = showLyrics ? Visibility.Collapsed : Visibility.Visible;
            SongLyricsPanel.Visibility = showLyrics ? Visibility.Visible : Visibility.Collapsed;
            SongLyrics.Text = showLyrics ? _activeLyric : string.Empty;
            SongLyricsSecondary.Text = showSecondary ? _secondaryLyric : string.Empty;
            SongLyricsSecondaryContainer.Visibility = showSecondary ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetRowSpan(SongLyricsContainer, showSecondary ? 1 : 2);
            SongLyricsContainer.VerticalAlignment = showSecondary
                ? VerticalAlignment.Stretch
                : VerticalAlignment.Center;
        }

        /// <summary>根据当前可见文本发布自动尺寸请求。/ Raises an auto-size request for the visible text.</summary>
        private void RaiseDesiredSizeChanged(bool isResetToPreset = false)
        {
            if (_layoutEngine?.CurrentOrientation is not { } orientation)
                return;

            var lyricsVisible = SongLyricsPanel.Visibility == Visibility.Visible;
            var visibleText = lyricsVisible ? _activeLyric : SongTitle.Text;
            var secondaryText = lyricsVisible && SongLyricsSecondaryContainer.Visibility == Visibility.Visible
                ? _secondaryLyric
                : string.Empty;
            var artist = !lyricsVisible && SongArtistContainer.Visibility == Visibility.Visible ? _actualArtist : string.Empty;
            var fingerprint = $"{orientation}|{visibleText}|{secondaryText}|{artist}|{SongTitle.FontSize:0.##}|{SongArtist.FontSize:0.##}|{SettingsManager.Current.LayoutLengthScalePercent:0.##}|{SettingsManager.Current.LayoutThicknessScalePercent:0.##}|{SettingsManager.Current.LyricsEnabled}|{SettingsManager.Current.TwoLineLyricsEnabled}|{SettingsManager.Current.LyricsSecondaryLineMode}|{SettingsManager.Current.TaskbarExperience}|{SettingsManager.Current.Interaction.Mode}|{_snapshot.Duration > 0}";
            if (!isResetToPreset && fingerprint == _lastSizeFingerprint)
                return;

            _lastSizeFingerprint = fingerprint;
            var textWidth = Math.Max(
                Math.Max(MeasureTextWidth(visibleText, lyricsVisible ? SongLyrics : SongTitle),
                    MeasureTextWidth(secondaryText, SongLyricsSecondary)),
                MeasureTextWidth(artist, SongArtist));
            var preset = LayoutPresets.GetLayout(_currentMode, orientation);
            var request = LayoutSizeCalculator.Calculate(
                preset,
                SettingsManager.Current.LayoutLengthScalePercent / 100.0,
                SettingsManager.Current.LayoutThicknessScalePercent / 100.0,
                textWidth,
                double.PositiveInfinity,
                fingerprint,
                isResetToPreset);
            if (_currentMode == WindowMode.Taskbar && orientation == LayoutOrientation.Horizontal)
            {
                request = request with
                {
                    Width = TaskbarExperiencePolicy.CalculateWidth(
                        textWidth,
                        GetTaskbarArtworkRight(),
                        TaskbarSpectrumWidth,
                        TaskbarTrailingMargin,
                        SettingsManager.Current.Interaction.Mode != MediaInteractionMode.Gestures,
                        SettingsManager.Current.TaskbarExperience.HoverLayerEnabled,
                        _snapshot.Duration > 0,
                        SettingsManager.Current.TaskbarExperience.Density,
                        double.PositiveInfinity)
                };
            }
            DesiredSizeChanged?.Invoke(this, new MediaBarSizeRequestEventArgs(request));
        }

        private static double MeasureTextWidth(string text, System.Windows.Controls.TextBlock source)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var pixelsPerDip = VisualTreeHelper.GetDpi(source).PixelsPerDip;
            var formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(source.FontFamily, source.FontStyle, source.FontWeight, source.FontStretch),
                source.FontSize,
                Brushes.Transparent,
                pixelsPerDip)
            {
                Trimming = TextTrimming.None
            };
            return formatted.WidthIncludingTrailingWhitespace + 4;
        }

        /// <summary>
        /// 入场动画：标题/艺术家变化时触发淡入和左滑效果。
        /// Entrance animation: fade-in and left-slide effect when title/artist changes.
        /// </summary>
        private void AnimateEntrance()
        {
            try
            {
                const int msDuration = 300;

                // 不透明度动画：从 0 到 1
                // Opacity animation: from 0 to 1
                DoubleAnimation opacityAnimation = new()
                {
                    From = 0.0,
                    To = _isTaskbarHoverVisible ? TaskbarCoveredOpacity : 1.0,
                    Duration = TimeSpan.FromMilliseconds(msDuration),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                // 平移动画：从左侧 -10px 滑入
                // Translation animation: slide in from -10px left
                DoubleAnimation translateAnimation = new()
                {
                    From = -10,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(msDuration),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                // 应用动画
                // Apply animations
                SongInfoStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
                TranslateTransform translateTransform = new();
                SongInfoStackPanel.RenderTransform = translateTransform;
                translateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        // === 悬停层 Hover layer ===

        private bool CanUseTaskbarComponentHover() =>
            _isConnected && _currentMode == WindowMode.Taskbar && !_isVertical;

        private bool CanShowDirectFullPanelHandle()
        {
            var experience = SettingsManager.Current.TaskbarExperience;
            return CanUseTaskbarComponentHover() &&
                   !experience.HoverLayerEnabled &&
                   experience.FullLayerEnabled;
        }

        /// <summary>直接模糊并淡化原文字区域，悬停按钮作为独立兄弟元素保持清晰。</summary>
        private void AnimateSongInfoCovered(bool isCovered, bool immediate = false)
        {
            if (isCovered && !CanUseTaskbarComponentHover())
                return;

            if (SongInfoStackPanel.Effect is not BlurEffect blur)
            {
                blur = new BlurEffect
                {
                    Radius = 0,
                    KernelType = KernelType.Gaussian,
                    RenderingBias = RenderingBias.Performance
                };
                SongInfoStackPanel.Effect = blur;
            }

            var targetRadius = isCovered ? TaskbarCoveredBlurRadius : 0;
            var targetOpacity = isCovered ? TaskbarCoveredOpacity : 1;
            if (immediate)
            {
                blur.BeginAnimation(BlurEffect.RadiusProperty, null);
                SongInfoStackPanel.BeginAnimation(OpacityProperty, null);
                blur.Radius = targetRadius;
                SongInfoStackPanel.Opacity = targetOpacity;
                return;
            }

            var duration = TimeSpan.FromMilliseconds(isCovered ? 180 : 150);
            var easingMode = isCovered ? EasingMode.EaseOut : EasingMode.EaseInOut;
            blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation
            {
                To = targetRadius,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = easingMode }
            }, HandoffBehavior.SnapshotAndReplace);
            SongInfoStackPanel.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                To = targetOpacity,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = easingMode }
            }, HandoffBehavior.SnapshotAndReplace);
        }

        private void AnimateDirectFullPanelHandle(bool isVisible, bool immediate = false)
        {
            var targetOpacity = isVisible && CanShowDirectFullPanelHandle() ? 1 : 0;
            if (immediate)
            {
                TaskbarDirectFullPanelHandle.BeginAnimation(OpacityProperty, null);
                TaskbarDirectFullPanelHandle.Opacity = targetOpacity;
                return;
            }

            TaskbarDirectFullPanelHandle.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(140),
                EasingFunction = new CubicEase
                {
                    EasingMode = targetOpacity > 0 ? EasingMode.EaseOut : EasingMode.EaseInOut
                }
            }, HandoffBehavior.SnapshotAndReplace);
        }

        /// <summary>复用原 TaskBarMediaControl 的 hover 色彩和节奏，但将效果限制在单个组件。</summary>
        private void AnimateComponentHover(Border surface, bool isHovered)
        {
            if (isHovered && !CanUseTaskbarComponentHover())
                return;

            var isDark = ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark;
            var backgroundColor = isHovered
                ? isDark ? Color.FromArgb(197, 255, 255, 255) : Colors.White
                : Colors.Transparent;
            var backgroundOpacity = isHovered ? isDark ? 0.075 : 0.6 : 0;
            var borderColor = isHovered ? Color.FromArgb(93, 255, 255, 255) : Colors.Transparent;
            var borderOpacity = isHovered ? isDark ? 0.25 : 1 : 0;
            var easingMode = isHovered ? EasingMode.EaseOut : EasingMode.EaseInOut;

            if (surface.Background is not SolidColorBrush background || background.IsFrozen)
            {
                background = new SolidColorBrush(Colors.Transparent);
                surface.Background = background;
            }
            if (surface.BorderBrush is not SolidColorBrush border || border.IsFrozen)
            {
                border = new SolidColorBrush(Colors.Transparent);
                surface.BorderBrush = border;
            }

            var duration = TimeSpan.FromMilliseconds(200);
            background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                To = backgroundColor,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = easingMode }
            });
            background.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation
            {
                To = backgroundOpacity,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = easingMode }
            });
            border.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                To = borderColor,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = easingMode }
            });
            border.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation
            {
                To = borderOpacity,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = easingMode }
            });
        }

        private void SongImageBorder_MouseEnter(object sender, MouseEventArgs e) =>
            AnimateComponentHover(SongImageHoverOverlay, true);

        private void SongImageBorder_MouseLeave(object sender, MouseEventArgs e) =>
            AnimateComponentHover(SongImageHoverOverlay, false);

        private void TaskbarSpectrumHoverSurface_MouseEnter(object sender, MouseEventArgs e) =>
            AnimateComponentHover(TaskbarSpectrumHoverSurface, true);

        private void TaskbarSpectrumHoverSurface_MouseLeave(object sender, MouseEventArgs e) =>
            AnimateComponentHover(TaskbarSpectrumHoverSurface, false);

        private void SongInfoStackPanel_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!CanUseTaskbarComponentHover())
                return;

            AnimateComponentHover(SongInfoHoverOverlay, true);
            AnimateDirectFullPanelHandle(true);
            _hoverCloseTimer.Stop();
            _hoverOpenTimer.Stop();
            if (SettingsManager.Current.TaskbarExperience.HoverLayerEnabled)
                _hoverOpenTimer.Start();
        }

        private void SongInfoStackPanel_MouseLeave(object sender, MouseEventArgs e)
        {
            _hoverOpenTimer.Stop();
            if (!_isTaskbarHoverVisible)
            {
                if (!TaskbarDirectFullPanelHandle.IsMouseOver)
                {
                    AnimateComponentHover(SongInfoHoverOverlay, false);
                    AnimateDirectFullPanelHandle(false);
                }
                return;
            }
            _hoverCloseTimer.Stop();
            _hoverCloseTimer.Start();
        }

        private void TaskbarHoverLayer_MouseEnter(object sender, MouseEventArgs e)
        {
            _hoverCloseTimer.Stop();
            AnimateComponentHover(SongInfoHoverOverlay, true);
        }

        private void TaskbarHoverLayer_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isSeeking)
                return;
            _hoverCloseTimer.Stop();
            _hoverCloseTimer.Start();
        }

        private void TaskbarDirectFullPanelHandle_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!CanShowDirectFullPanelHandle())
                return;
            AnimateComponentHover(SongInfoHoverOverlay, true);
            AnimateDirectFullPanelHandle(true);
        }

        private void TaskbarDirectFullPanelHandle_MouseLeave(object sender, MouseEventArgs e)
        {
            if (SongInfoStackPanel.IsMouseOver)
                return;
            AnimateComponentHover(SongInfoHoverOverlay, false);
            AnimateDirectFullPanelHandle(false);
        }

        private void ShowTaskbarHoverLayer()
        {
            if (!_isConnected || _currentMode != WindowMode.Taskbar || _isVertical ||
                !SettingsManager.Current.TaskbarExperience.HoverLayerEnabled ||
                (!SongInfoStackPanel.IsMouseOver && !HoverRevealHost.IsMouseOver))
                return;

            ApplyTaskbarExperienceSettings();
            _isTaskbarHoverVisible = true;
            AnimateComponentHover(SongInfoHoverOverlay, true);
            AnimateSongInfoCovered(true);
            HoverRevealHost.Visibility = Visibility.Visible;
            HoverRevealHost.IsHitTestVisible = true;
            HoverRevealHost.BeginAnimation(FrameworkElement.WidthProperty, null);
            var targetWidth = Math.Max(0, SongInfoStackPanel.Width);
            HoverRevealHost.Width = Math.Min(Math.Max(1, HoverRevealHost.ActualWidth), targetWidth);
            var reveal = new DoubleAnimation
            {
                From = HoverRevealHost.Width,
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            HoverRevealHost.BeginAnimation(FrameworkElement.WidthProperty, reveal, HandoffBehavior.SnapshotAndReplace);
        }

        private void HideTaskbarHoverLayer(bool immediate = false)
        {
            _hoverOpenTimer.Stop();
            _hoverCloseTimer.Stop();
            if (!immediate && _isSeeking)
                return;
            if (immediate || HoverRevealHost.Visibility != Visibility.Visible)
            {
                HoverRevealHost.BeginAnimation(FrameworkElement.WidthProperty, null);
                HoverRevealHost.Width = 0;
                HoverRevealHost.Visibility = Visibility.Collapsed;
                HoverRevealHost.IsHitTestVisible = false;
                _isTaskbarHoverVisible = false;
                AnimateSongInfoCovered(false, immediate: true);
                var keepRegularHover = CanUseTaskbarComponentHover() &&
                                       (SongInfoStackPanel.IsMouseOver || TaskbarDirectFullPanelHandle.IsMouseOver);
                AnimateComponentHover(SongInfoHoverOverlay, keepRegularHover);
                AnimateDirectFullPanelHandle(keepRegularHover, immediate: true);
                return;
            }

            AnimateSongInfoCovered(false);

            var hide = new DoubleAnimation
            {
                From = HoverRevealHost.ActualWidth,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            hide.Completed += (_, _) =>
            {
                HoverRevealHost.BeginAnimation(FrameworkElement.WidthProperty, null);
                HoverRevealHost.Width = 0;
                HoverRevealHost.Visibility = Visibility.Collapsed;
                HoverRevealHost.IsHitTestVisible = false;
                _isTaskbarHoverVisible = false;
                if (!SongInfoStackPanel.IsMouseOver && !TaskbarDirectFullPanelHandle.IsMouseOver)
                    AnimateComponentHover(SongInfoHoverOverlay, false);
            };
            HoverRevealHost.BeginAnimation(FrameworkElement.WidthProperty, hide, HandoffBehavior.SnapshotAndReplace);
        }

        private void SongImageBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isConnected || !_canPlayPause || e.ChangedButton != MouseButton.Left ||
                DateTime.UtcNow < _suppressArtworkClickUntilUtc)
                return;

            if (_currentMode == WindowMode.Taskbar &&
                SettingsManager.Current.Interaction.Mode == MediaInteractionMode.Buttons)
                return;

            TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void InteractionSurface_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_isConnected)
                return;

            if (_currentMode == WindowMode.Taskbar)
            {
                var leftDown = Mouse.LeftButton == MouseButtonState.Pressed;
                var rightDown = Mouse.RightButton == MouseButtonState.Pressed;
                if (leftDown)
                    _suppressArtworkClickUntilUtc = DateTime.UtcNow.AddMilliseconds(350);
                WheelRequested?.Invoke(this, new PlayerSurfaceWheelEventArgs(e.Delta, leftDown, rightDown));
                e.Handled = SettingsManager.Current.Interaction.Mode != MediaInteractionMode.Buttons;
            }
            else if (e.Delta > 0 && _canSkipPrevious)
            {
                SkipPreviousRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.Delta < 0 && _canSkipNext)
            {
                SkipNextRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void TaskbarPreviousButton_Click(object sender, RoutedEventArgs e) =>
            SkipPreviousRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarPlayPauseButton_Click(object sender, RoutedEventArgs e) =>
            TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarNextButton_Click(object sender, RoutedEventArgs e) =>
            SkipNextRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarDeviceButton_Click(object sender, RoutedEventArgs e) =>
            OutputDeviceCycleRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarVolumeButton_Click(object sender, RoutedEventArgs e) =>
            AudioControlRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarFullPanelHandle_Click(object sender, RoutedEventArgs e) =>
            OpenFullPanelRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarHoverProgress_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_snapshot.CanSeek || _snapshot.Duration <= 0)
                return;
            _isSeeking = true;
            TaskbarHoverProgress.CaptureMouse();
            UpdateSeekValue(e.GetPosition(TaskbarHoverProgress).X);
            e.Handled = true;
        }

        private void TaskbarHoverProgress_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSeeking || e.LeftButton != MouseButtonState.Pressed)
                return;
            UpdateSeekValue(e.GetPosition(TaskbarHoverProgress).X);
            e.Handled = true;
        }

        private void TaskbarHoverProgress_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSeeking)
                return;
            UpdateSeekValue(e.GetPosition(TaskbarHoverProgress).X);
            _isSeeking = false;
            TaskbarHoverProgress.ReleaseMouseCapture();
            SeekRequested?.Invoke(TaskbarHoverProgress.Value);
            if (!HoverRevealHost.IsMouseOver && !SongInfoStackPanel.IsMouseOver)
                HideTaskbarHoverLayer();
            e.Handled = true;
        }

        private void UpdateSeekValue(double pointerX)
        {
            if (TaskbarHoverProgress.ActualWidth <= 0 || _snapshot.Duration <= 0)
                return;
            var ratio = Math.Clamp(pointerX / TaskbarHoverProgress.ActualWidth, 0, 1);
            TaskbarHoverProgress.Value = ratio * _snapshot.Duration;
        }

        private void UpdateTaskbarProgress()
        {
            var position = TaskbarExperiencePolicy.GetPosition(_snapshot, DateTimeOffset.UtcNow);
            var hasDuration = _snapshot.Duration > 0;
            TaskbarRestProgress.Maximum = Math.Max(1, _snapshot.Duration);
            TaskbarRestProgress.Value = position;
            TaskbarHoverProgress.Maximum = Math.Max(1, _snapshot.Duration);
            if (!_isSeeking)
                TaskbarHoverProgress.Value = position;
            if (_currentMode == WindowMode.Taskbar && !_isVertical)
            {
                TaskbarRestProgress.Visibility = hasDuration ? Visibility.Visible : Visibility.Collapsed;
                TaskbarHoverProgress.Visibility = hasDuration ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T typed)
                    yield return typed;
                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private void SongTitleContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isConnected || e.ChangedButton != MouseButton.Left)
                return;

            ActivateSourceRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
