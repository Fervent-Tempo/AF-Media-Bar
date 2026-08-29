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


        public MainWindow(MainWindowViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            SystemThemeWatcher.Watch(this);

            InitializeComponent();

            // slow auto-update for display changes
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _timer.Tick += (s, e) => UpdatePosition();
            _timer.Start();

            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

            // subscribe to SMTC media session events and start monitoring
            mediaManager.OnAnyMediaPropertyChanged += MediaManager_OnAnyMediaPropertyChanged;
            mediaManager.OnAnyPlaybackStateChanged += CurrentSession_OnPlaybackStateChanged;
            mediaManager.OnAnySessionOpened += MediaManager_OnAnySessionOpened;
            mediaManager.OnAnySessionClosed += MediaManager_OnAnySessionClosed;
            mediaManager.Start();

            // evaluate the initial state once the window is loaded
            Loaded += MainWindow_Loaded;
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

        public MediaSession? GetActiveMediaSession()
        {
            var validSessions = mediaManager.CurrentMediaSessions.Values.Where(IsSessionAllowed).ToList();

            if (validSessions.Count == 0) return null;

            var focused = mediaManager.GetFocusedSession();
            if (focused != null && validSessions.Any(s => s.Id == focused.Id))
                return focused;

            return validSessions.FirstOrDefault();
        }

        public void UpdateUi(string title, string artist, BitmapImage? icon,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus)
        {
            if (!_timer.IsEnabled)
                _timer.Start();

            // Delegate UI update to the internal update logic
            MediaControl.UpdateSongInfo(title, artist, icon, playbackStatus);

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

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateTaskbar();
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

        private void MediaManager_OnAnySessionOpened(MediaSession mediaSession)
        {
            Dispatcher.BeginInvoke(() => UpdateTaskbar());
        }

        private void MediaManager_OnAnySessionClosed(MediaSession mediaSession)
        {
            Dispatcher.BeginInvoke(() => UpdateTaskbar());
        }
    }
}