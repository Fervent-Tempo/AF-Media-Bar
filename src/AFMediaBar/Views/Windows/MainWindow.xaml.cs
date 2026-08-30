using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        public MainWindowViewModel ViewModel { get; }

        private readonly ITaskbarDockService _taskBarService;
        private readonly MediaSessionService _mediaSessionService;
        private TaskbarWindow? _taskbarWindow;
        private int _taskbarCreatedMessage;

        // Set while Explorer restarts so windows touching the taskbar pause their work
        internal static volatile bool ExplorerRestarting = false;

        public MainWindow(
            MainWindowViewModel viewModel,
            ITaskbarDockService taskBarService,
            MediaSessionService mediaSessionService)
        {
            ViewModel = viewModel;
            DataContext = this;

            _taskBarService = taskBarService;
            _mediaSessionService = mediaSessionService;

            SystemThemeWatcher.Watch(this);

            InitializeComponent();

            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

            // 快照事件已在服务内调度到 UI 线程，这里只负责转发给任务栏窗口。
            _mediaSessionService.SnapshotChanged += MediaSessionService_OnSnapshotChanged;
            _mediaSessionService.SessionsChanged += MediaSessionService_OnSessionsChanged;

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

            _mediaSessionService.SnapshotChanged -= MediaSessionService_OnSnapshotChanged;
            _mediaSessionService.SessionsChanged -= MediaSessionService_OnSessionsChanged;

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

            // Replay the latest snapshot; if none exists yet, force a synchronous refresh.
            if (_mediaSessionService.CurrentSnapshot is { } snapshot)
            {
                _taskbarWindow.ApplySnapshot(snapshot);
            }
            else
            {
                _mediaSessionService.RefreshNow();
            }
        }

        private void MediaSessionService_OnSnapshotChanged(object? sender, MediaSnapshot snapshot)
        {
            _taskbarWindow?.ApplySnapshot(snapshot);
        }

        private void MediaSessionService_OnSessionsChanged(IReadOnlyList<MediaSessionOption> options)
        {
            _taskbarWindow?.ApplySessions(options);
        }

        // 任务栏窗口右键菜单的命令入口。 / Command entry points for the taskbar window context menu.
        internal void SelectMediaSession(string key) => _mediaSessionService.SelectSession(key);

        internal void ReconnectMediaSession() => _ = _mediaSessionService.ReconnectAsync();

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // The media bar lives in the docked TaskbarWindow; keep this window as an invisible host.
            Visibility = Visibility.Collapsed;

            RecreateTaskbarWindow();
        }
    }
}
