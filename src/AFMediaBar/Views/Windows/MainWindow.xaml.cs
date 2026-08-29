using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        public MainWindowViewModel ViewModel { get; }

        private readonly DispatcherTimer _timer;

        public readonly WindowsMediaController.MediaManager mediaManager = new();
        private GlobalSystemMediaTransportControlsSessionPlaybackStatus? _lastPlaybackStatus;

        private string _actualTitle = string.Empty;
        private string _actualArtist = string.Empty;

        private bool _isPaused;
        private bool _isVertical;
        private bool _isSmallTaskbar;

        public MainWindow(MainWindowViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            SystemThemeWatcher.Watch(this);

            InitializeComponent();

            // Apply Windows theme colors (independent of the app theme setting)
            ApplyWindowsTheme();

            // slow auto-update for display changes
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _timer.Tick += (s, e) => UpdatePosition();
            _timer.Start();

            MainBorder.SizeChanged += (s, e) =>
            {
                var rect = new RectangleGeometry(new Rect(0, 0, MainBorder.ActualWidth, MainBorder.ActualHeight), 6, 6);
                MainBorder.Clip = rect;
            };

            // for hover animation
            if (MainBorder.Background is not SolidColorBrush)
            {
                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
            }

            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

            // subscribe to SMTC media session events and start monitoring
            mediaManager.OnAnyMediaPropertyChanged += MediaManager_OnAnyMediaPropertyChanged;
            mediaManager.OnAnyPlaybackStateChanged += CurrentSession_OnPlaybackStateChanged;
            mediaManager.OnAnySessionClosed += MediaManager_OnAnySessionClosed;
            mediaManager.Start();
        }

        #region INavigationWindow methods

        public INavigationView GetNavigation() => throw new NotImplementedException();

        public bool Navigate(Type pageType) => false;

        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) =>
            throw new NotImplementedException();

        public void SetServiceProvider(IServiceProvider serviceProvider) =>
            throw new NotImplementedException();

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        #region Window Controller & others

        /// <summary>
        /// Raises the closed event.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Make sure that closing this window will begin the process of closing the application.
            Application.Current.Shutdown();
        }

        #endregion

        /// <summary>
        /// Applies Windows theme colors to the widget (independent of the app theme setting).
        /// </summary>
        public void ApplyWindowsTheme()
        {
            bool isDark = ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark;

            var foreground = new SolidColorBrush(isDark
                ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0xE4, 0x1C, 0x1C, 0x1C));

            SongTitle.Foreground = foreground;
            SongArtist.Foreground = foreground;
        }

        public void UpdateTaskbar()
        {
            var activeSession = GetActiveMediaSession();
            if (!mediaManager.IsStarted || activeSession == null)
            {
                UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
                return;
            }

            var songInfo = TryGetMediaProperties(activeSession.ControlSession);
            if (songInfo == null)
                return;
            var playbackInfo = activeSession.ControlSession.GetPlaybackInfo();
            var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
            BitmapHelper.GetDominantColors(1);
            UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo.PlaybackStatus);
        }

        public void UpdateUi(string title, string artist, BitmapImage? icon,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus)
        {
            // Autohide - Widget hides when playback is paused
            _lastPlaybackStatus = playbackStatus;

            if (!_timer.IsEnabled)
                _timer.Start();

            // Delegate UI update to the internal update logic
            UpdateSongInfo(title, artist, icon, playbackStatus);

            // Update position after UI change
            Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);

            Dispatcher.Invoke(() => { Visibility = Visibility.Visible; });
        }

        /// <summary>
        /// Keeps the media bar centered on the taskbar.
        /// </summary>
        public void UpdatePosition()
        {
            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Bottom - Height;
        }

        public bool IsSessionAllowed(MediaSession? session)
        {
            if (session == null) return false;
            return true;
        }

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

        public MediaSession? GetActiveMediaSession()
        {
            var validSessions = mediaManager.CurrentMediaSessions.Values.Where(IsSessionAllowed).ToList();

            if (validSessions.Count == 0) return null;

            var focused = mediaManager.GetFocusedSession();
            if (focused != null && validSessions.Any(s => s.Id == focused.Id))
                return focused;

            return validSessions.FirstOrDefault();
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
                // No media playing, hide UI
                Dispatcher.Invoke(() =>
                {
                    _actualTitle = string.Empty;
                    _actualArtist = string.Empty;

                    if (ViewModel.TaskbarWidgetHideCompletely)
                    {
                        Visibility = Visibility.Collapsed;
                        return;
                    }

                    SongTitle.Text = string.Empty;
                    SongArtist.Text = string.Empty;
                    SongInfoStackPanel.Visibility = Visibility.Collapsed;
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
                    // changed info
                    if (ViewModel.TaskbarWidgetAnimated)
                    {
                        AnimateEntrance();
                    }

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
                    if (_isPaused && ViewModel.TaskbarWidgetShowPauseOverlay)
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
                BackgroundImage.Visibility = ViewModel.TaskbarWidgetBackgroundBlur
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                Visibility = Visibility.Visible;
            });
        }

        private void AnimateEntrance()
        {
            return;
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
            return;
            if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

            SolidColorBrush targetBackgroundBrush;
            bool isDark = ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark;

            if (isDark)
            { // dark mode
                targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 };
                TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 0.25 };
            }
            else
            { // light mode
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

        // SMTC media session event handlers
        private void MediaManager_OnAnyMediaPropertyChanged(MediaSession mediaSession,
            GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties)
        {
            Dispatcher.BeginInvoke(() => UpdateTaskbar());
        }

        private void CurrentSession_OnPlaybackStateChanged(MediaSession mediaSession,
            GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo)
        {
            Dispatcher.BeginInvoke(() => UpdateTaskbar());
        }

        private void MediaManager_OnAnySessionClosed(MediaSession mediaSession)
        {
            Dispatcher.BeginInvoke(() => UpdateTaskbar());
        }
    }
}