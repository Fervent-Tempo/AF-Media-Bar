using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Windows.Media.Control;
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

        private GlobalSystemMediaTransportControlsSessionPlaybackStatus? _lastPlaybackStatus;

        private string _actualTitle = string.Empty;
        private string _actualArtist = string.Empty;

        private bool _isPaused;
        private bool _isVertical;
        private bool _isSmallTaskbar;

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


        private static GlobalSystemMediaTransportControlsSessionMediaProperties? TryGetMediaProperties(
            GlobalSystemMediaTransportControlsSession controlSession)
        {
            try
            {
                return controlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
            }
            catch (COMException)
            {
                return null;
            }
        }

        public void UpdateSongInfo(string title, string artist, BitmapImage? icon,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus)
        {
            if (title == "-" && artist == "-")
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
                    SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover

                    MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                    MainBorder.Background.Opacity = 0;
                    TopBorder.BorderBrush = Brushes.Transparent;

                    Visibility = Visibility.Visible;
                });
                return;
            }

            _isPaused = false;
            if (playbackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                _isPaused = true;
            }

            Dispatcher.Invoke(() =>
            {
                string newTitle = !string.IsNullOrEmpty(title) ? title : "-";
                string newArtist = !string.IsNullOrEmpty(artist) ? artist : "-";

                if (_actualTitle != newTitle || _actualArtist != newArtist)
                {
                    AnimateEntrance();


                    _actualTitle = newTitle;
                    _actualArtist = newArtist;

                    SongTitle.Text = _actualTitle;
                    SongArtist.Text = _actualArtist;
                }

                // Update tooltip with song info
                SongInfoStackPanel.ToolTip = string.Empty;
                SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(title) ? title : string.Empty;
                SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(artist) ? "\n\n" + artist : string.Empty;


                // change color of icon
                SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0
                    ? BitmapHelper.SavedDominantColors.Last()
                    : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
                SongImagePlaceholder.Foreground = brush;

                if (icon != null)
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

                    SongImage.ImageSource = icon;
                    BackgroundImage.Source = icon;
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
                SongArtistContainer.Visibility = !_isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(artist)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                SongInfoStackPanel.Visibility = _isVertical ? Visibility.Collapsed : Visibility.Visible;
                // BackgroundImage.Visibility = ViewModel.TaskbarWidgetBackgroundBlur
                //     ? Visibility.Visible
                //     : Visibility.Collapsed;

                Visibility = Visibility.Visible;
            });
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