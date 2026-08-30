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
    /// TaskBarMediaControl.xaml 的交互逻辑
    /// </summary>
    public partial class TaskBarMediaControl : UserControl
    {
        public TaskBarMediaControl()
        {
            InitializeComponent();
        }

        private string _actualTitle = string.Empty;
        private string _actualArtist = string.Empty;

        private bool _isPaused;
        private bool _isVertical;
        private bool _isSmallTaskbar;

        // 歌词显示状态：解析后的行缓存 + 当前行下标，避免每个快照重复解析。
        private string? _lyricsLrc;
        private IReadOnlyList<LrcLine> _lyricsLines = [];
        private int _lastLyricIndex = -2;

        public void SetVerticalMode(bool isVertical)
        {
            _isVertical = isVertical;
            SongInfoStackPanel.Visibility = isVertical ? Visibility.Collapsed : Visibility.Visible;
            SongArtistContainer.Visibility = !_isSmallTaskbar && !isVertical && !string.IsNullOrEmpty(_actualArtist)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public void SetSmallTaskbarMode(bool isSmallTaskbar)
        {
            _isSmallTaskbar = isSmallTaskbar;
            SongArtistContainer.Visibility = !isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(_actualArtist)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }


        public void ApplyWindowsTheme()
        {
            WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
            bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;

            var foreground = new SolidColorBrush(isDark
                ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0xE4, 0x1C, 0x1C, 0x1C));

            SongTitle.Foreground = foreground;
            SongArtist.Foreground = foreground;
        }


        public void UpdateSongInfo(MediaSnapshot snapshot)
        {
            if (!snapshot.IsConnected)
            {
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

                if (_actualTitle != newTitle || _actualArtist != newArtist)
                {
                    AnimateEntrance();


                    _actualTitle = newTitle;
                    _actualArtist = newArtist;

                    SongTitle.Text = _actualTitle;
                    SongArtist.Text = _actualArtist;
                }

                // 歌词可用时标题位置显示当前歌词行（随快照位置推进）。
                UpdateLyricLine(snapshot);

                // Update tooltip with song info
                SongInfoStackPanel.ToolTip = string.Empty;
                SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(snapshot.Title) ? snapshot.Title : string.Empty;
                SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(snapshot.Artist) ? "\n\n" + snapshot.Artist : string.Empty;


                // change color of icon
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

        private void AnimateEntrance()
        {
            try
            {
                const int msDuration = 300;

                // opacity and left to right animation for SongInfoStackPanel
                DoubleAnimation opacityAnimation = new()
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(msDuration),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                DoubleAnimation translateAnimation = new()
                {
                    From = -10,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(msDuration),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

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

        // hover effects with animations, hard-coded colors because the resource brushes are not accessible here
        private void Grid_MouseEnter(object sender, MouseEventArgs e)
        {
            if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

            SolidColorBrush targetBackgroundBrush;
            bool isDark = ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark;

            if (isDark)
            {
                // dark mode
                targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 };
                TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 0.25 };
            }
            else
            {
                // light mode
                targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) { Opacity = 0.6 };
                TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 1 };
            }

            // Animate background
            var backgroundAnimation = new ColorAnimation
            {
                To = targetBackgroundBrush.Color,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var backgroundOpacityAnimation = new DoubleAnimation
            {
                To = targetBackgroundBrush.Opacity,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            // rare case where background is not a SolidColorBrush after SetupWindow
            if (MainBorder.Background is not SolidColorBrush)
            {
                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
            }

            MainBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
            MainBorder.Background.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);
        }

        private void Grid_MouseLeave(object sender, MouseEventArgs e)
        {
            if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

            // Animate back to transparent
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