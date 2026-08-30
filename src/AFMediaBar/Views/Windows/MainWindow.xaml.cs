using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using static AFMediaBar.Classes.Interop.NativeMethods;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        public MainWindowViewModel ViewModel { get; }

        public readonly WindowsMediaController.MediaManager mediaManager = new();

        private readonly ITaskbarDockService _taskBarService;
        private TaskbarWindow? _taskbarWindow;
        private int _taskbarCreatedMessage;

        // Set while Explorer restarts so windows touching the taskbar pause their work
        internal static volatile bool ExplorerRestarting = false;

        public MainWindow(MainWindowViewModel viewModel, ITaskbarDockService taskBarService)
        {
            ViewModel = viewModel;
            DataContext = this;

            _taskBarService = taskBarService;

            SystemThemeWatcher.Watch(this);

            InitializeComponent();

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

            _taskbarWindow?.Close();
            _taskbarWindow = null;

            // Make sure that closing this window will begin the process of closing the application.
            Application.Current.Shutdown();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = (HwndSource)PresentationSource.FromDependencyObject(this);
            source.AddHook(WndProc);
            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _taskbarCreatedMessage)
            {
                // Explorer restarted; defer recovery until the taskbar is back and stable.
                ExplorerRestarting = true;
                _ = WaitForExplorerAndRecoverAsync();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private async Task WaitForExplorerAndRecoverAsync()
        {
            try
            {
                // Poll for the taskbar window for up to ~10 seconds
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(500);
                    if (FindWindow("Shell_TrayWnd", null) != IntPtr.Zero)
                        break;
                }
            }
            catch { }

            ExplorerRestarting = false;
            RecreateTaskbarWindow();
        }

        #endregion

        /// <summary>
        /// Closes and re-creates the docked taskbar window (e.g. after Explorer restarted
        /// and destroyed the old taskbar together with our child window).
        /// </summary>
        public void RecreateTaskbarWindow()
        {
            if (_taskbarWindow != null)
            {
                try
                {
                    _taskbarWindow.Close();
                }
                catch { }

                _taskbarWindow = null;
            }

            _taskbarWindow = new TaskbarWindow(_taskBarService, this);
            UpdateTaskbar();
        }

        public void UpdateTaskbar()
        {
            var activeSession = GetActiveMediaSession();
            if (!mediaManager.IsStarted || activeSession == null)
            {
                _taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
                return;
            }

            var songInfo = TryGetMediaProperties(activeSession.ControlSession);
            if (songInfo == null)
                return;
            var playbackInfo = activeSession.ControlSession.GetPlaybackInfo();
            var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
            BitmapHelper.GetDominantColors(1);
            _taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo.PlaybackStatus);
            _taskbarWindow?.MediaControl.ApplyWindowsTheme();
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

        public bool IsSessionAllowed(MediaSession? session)
        {
            if (session == null) return false;
            return true;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // The media bar lives in the docked TaskbarWindow; keep this window as an invisible host.
            Visibility = Visibility.Collapsed;

            RecreateTaskbarWindow();
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
