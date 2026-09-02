using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Shapes;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AFMediaBar.Components
{
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
    /// - 媒体控制命令由 TaskbarWindow 的 ContextMenu 绑定到 MainWindowViewModel
    ///   Media control commands are bound from TaskbarWindow's ContextMenu to MainWindowViewModel
    /// - 不直接调用服务，所有数据通过 UpdateSongInfo 方法传入
    ///   Does not call services directly; all data is passed via UpdateSongInfo method
    /// </summary>
    public partial class TaskBarMediaControl : UserControl
    {
        public TaskBarMediaControl()
        {
            InitializeComponent();
        }

        // === 内部状态缓存 Internal State Cache ===
        private string _actualTitle = string.Empty;   // 实际标题（不含歌词）Actual title (without lyrics)
        private string _actualArtist = string.Empty;  // 实际艺术家 Actual artist

        private bool _isPaused;        // 是否暂停 Whether paused
        private bool _isVertical;      // 任务栏是否竖向 Whether taskbar is vertical
        private bool _isSmallTaskbar;  // 是否小任务栏 Whether taskbar is small

        // === 歌词显示状态 Lyrics Display State ===
        // 解析后的行缓存 + 当前行下标，避免每个快照重复解析。
        // Parsed line cache + current line index to avoid re-parsing on every snapshot.
        private string? _lyricsLrc;
        private IReadOnlyList<LrcLine> _lyricsLines = [];
        private int _lastLyricIndex = -2;

        /// <summary>
        /// 设置竖向模式：任务栏在屏幕左侧或右侧时调整布局。
        /// Set vertical mode: adjust layout when taskbar is on screen left or right edge.
        /// </summary>
        public void SetVerticalMode(bool isVertical)
        {
            _isVertical = isVertical;
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
        /// 应用 Windows 主题：根据系统深浅色模式调整文字颜色。
        /// Apply Windows theme: adjust text color based on system light/dark mode.
        /// </summary>
        public void ApplyWindowsTheme()
        {
            WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
            bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;

            var foreground = new SolidColorBrush(isDark
                ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)  // 深色模式：白色文字 Dark mode: white text
                : Color.FromArgb(0xE4, 0x1C, 0x1C, 0x1C)); // 浅色模式：深色文字 Light mode: dark text

            SongTitle.Foreground = foreground;
            SongArtist.Foreground = foreground;
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
            if (!snapshot.IsConnected)
            {
                // 无媒体播放 - 显示占位符保持媒体栏可见
                // No media playing - show the placeholder text so the media bar stays visible
                Dispatcher.Invoke(() =>
                {
                    _actualTitle = string.Empty;
                    _actualArtist = string.Empty;

                    SongTitle.Text = _actualTitle;
                    SongArtist.Text = _actualArtist;
                    SongInfoStackPanel.Visibility = Visibility.Visible;
                    SongInfoStackPanel.ToolTip = string.Empty;
                    SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.ImageSource = null;
                    BackgroundImage.Source = null;
                    BackgroundImage.Visibility = Visibility.Collapsed;
                    SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover

                    MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                    MainBorder.Background.Opacity = 0;
                    TopBorder.BorderBrush = Brushes.Transparent;

                    Visibility = Visibility.Visible;
                });
                return;
            }

            _isPaused = !snapshot.IsPlaying;

            Dispatcher.Invoke(() =>
            {
                string newTitle = !string.IsNullOrEmpty(snapshot.Title) ? snapshot.Title : "-";
                string newArtist = !string.IsNullOrEmpty(snapshot.Artist) ? snapshot.Artist : "-";

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

                SongTitle.Visibility = Visibility.Visible;
                SongArtistContainer.Visibility = !_isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(snapshot.Artist)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                SongInfoStackPanel.Visibility = _isVertical ? Visibility.Collapsed : Visibility.Visible;
                // blurred cover background, off by default like FluentFlyout's TaskbarWidgetBackgroundBlur
                BackgroundImage.Visibility = SettingsManager.Current.TaskbarBarBackgroundBlur
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// 用快照中的歌词和位置更新标题区域的当前行；无歌词时恢复标题。
        /// Shows the active lyric line in the title slot from the snapshot; restores the title when lyrics are absent.
        /// </summary>
        private void UpdateLyricLine(MediaSnapshot snapshot)
        {
            var lrc = snapshot.Lyrics?.Lrc;
            if (string.IsNullOrWhiteSpace(lrc))
            {
                if (_lyricsLrc is not null)
                {
                    _lyricsLrc = null;
                    _lyricsLines = [];
                    _lastLyricIndex = -2;
                    SongTitle.Text = _actualTitle;
                }

                return;
            }

            if (!string.Equals(_lyricsLrc, lrc, StringComparison.Ordinal))
            {
                _lyricsLrc = lrc;
                _lyricsLines = LrcParser.Parse(lrc);
                _lastLyricIndex = -2;
            }

            var index = LrcParser.FindIndex(_lyricsLines, TimeSpan.FromSeconds(snapshot.Position));
            if (index != _lastLyricIndex)
            {
                _lastLyricIndex = index;
                SongTitle.Text = index >= 0 ? _lyricsLines[index].Text : _actualTitle;
            }
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
                    To = 1.0,
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

        // === 悬停效果 Hover Effects ===
        // 硬编码颜色，因为资源画刷在此处不可访问
        // Hard-coded colors because the resource brushes are not accessible here

        /// <summary>
        /// 鼠标进入事件：显示悬停背景和边框效果。
        /// Mouse enter event: show hover background and border effects.
        /// </summary>
        private void Grid_MouseEnter(object sender, MouseEventArgs e)
        {
            if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

            SolidColorBrush targetBackgroundBrush;
            bool isDark = ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark;

            if (isDark)
            {
                // 深色模式 Dark mode
                targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 };
                TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 0.25 };
            }
            else
            {
                // 浅色模式 Light mode
                targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) { Opacity = 0.6 };
                TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 1 };
            }

            // 背景颜色动画 Animate background color
            var backgroundAnimation = new ColorAnimation
            {
                To = targetBackgroundBrush.Color,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            // 背景不透明度动画 Animate background opacity
            var backgroundOpacityAnimation = new DoubleAnimation
            {
                To = targetBackgroundBrush.Opacity,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            // 罕见情况：SetupWindow 后背景不是 SolidColorBrush
            // Rare case where background is not a SolidColorBrush after SetupWindow
            if (MainBorder.Background is not SolidColorBrush)
            {
                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
            }

            MainBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
            MainBorder.Background.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);
        }

        /// <summary>
        /// 鼠标离开事件：动画恢复到透明背景。
        /// Mouse leave event: animate back to transparent background.
        /// </summary>
        private void Grid_MouseLeave(object sender, MouseEventArgs e)
        {
            if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

            // 动画恢复到透明 Animate back to transparent
            var backgroundAnimation = new ColorAnimation
            {
                To = Colors.Transparent,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            var backgroundOpacityAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            MainBorder.Background?.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
            MainBorder.Background?.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);

            TopBorder.BorderBrush = Brushes.Transparent;
        }
    }
}