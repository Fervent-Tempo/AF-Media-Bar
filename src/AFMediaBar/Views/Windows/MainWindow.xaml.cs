using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Windows;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Microsoft.Extensions.DependencyInjection;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows
{
    /// <summary>
    /// 隐藏的应用宿主窗口，协调媒体栏模式、托盘入口和任务栏恢复。
    /// Invisible application host coordinating media-bar modes, tray entry points, and taskbar recovery.
    /// </summary>
    public partial class MainWindow : INavigationWindow, ITaskbarWindowHostActions
    {
        public MainWindowViewModel ViewModel { get; }

        private readonly ITaskbarDockService _taskBarService;
        private readonly MediaSessionService _mediaSessionService;
        private readonly AudioControlViewModel _audioControlViewModel;
        private readonly AudioControlFlyoutWindow _audioControlFlyout;
        private readonly NativeMouseInputMonitor _mouseInputMonitor;
        private readonly WindowAppearanceService _appearanceService;
        private readonly TaskbarOccupiedAreaService _occupiedAreaService;
        private readonly Func<SettingsWindow> _settingsWindowFactory;
        private TaskbarWindow? _taskbarWindow;
        private DynamicIslandWindow? _dynamicIslandWindow;
        private SettingsWindow? _settingsWindow;
        private int _taskbarCreatedMessage;
        private bool _isSystemThemeWatcherActive;
        private bool _isClosing;
        private ApplicationBackdropMode? _watchedBackdropMode;
        private CancellationTokenSource? _taskbarRecoveryCancellation;

        // Explorer 重启并重建任务栏子窗口时暂停旧宿主。
        // Pause the old host while an Explorer restart rebuilds the taskbar child window.
        internal static volatile bool TaskbarEnvironmentRecovering;

        bool ITaskbarWindowHostActions.IsEnvironmentRecovering => TaskbarEnvironmentRecovering;
        void ITaskbarWindowHostActions.RequestTaskbarHostReload() => RequestTaskbarHostReload();

        /// <summary>
        /// 创建并初始化应用宿主窗口及其基础设施依赖。
        /// Creates the application host window and initializes its infrastructure dependencies.
        /// </summary>
        public MainWindow(
            MainWindowViewModel viewModel,
            ITaskbarDockService taskBarService,
            MediaSessionService mediaSessionService,
            AudioControlViewModel audioControlViewModel,
            AudioControlFlyoutWindow audioControlFlyout,
            NativeMouseInputMonitor mouseInputMonitor,
            WindowAppearanceService appearanceService,
            TaskbarOccupiedAreaService occupiedAreaService,
            Func<SettingsWindow> settingsWindowFactory)
        {
            ViewModel = viewModel;
            DataContext = this;

            _taskBarService = taskBarService;
            _mediaSessionService = mediaSessionService;
            _audioControlViewModel = audioControlViewModel;
            _audioControlFlyout = audioControlFlyout;
            _mouseInputMonitor = mouseInputMonitor;
            _appearanceService = appearanceService;
            _occupiedAreaService = occupiedAreaService;
            _settingsWindowFactory = settingsWindowFactory;

            InitializeComponent();
            UpdateSystemThemeWatcher(SettingsManager.Current.Appearance);

            // 托盘菜单由 Shell 消息手动打开，没有 PlacementTarget；显式绑定才能让命令保持有效。
            // The tray menu is opened from a Shell message without a PlacementTarget; bind explicitly so commands remain active.
            TrayMenu.DataContext = this;
            ContextMenuHelper.AttachOutsideClickDismissal(TrayMenu);
            _appearanceService.Attach(TrayMenu, this);

            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

            // 快照事件已在服务内调度到 UI 线程，这里只负责转发给任务栏窗口。
            _mediaSessionService.SnapshotChanged += MediaSessionService_OnSnapshotChanged;
            _mediaSessionService.SessionsChanged += MediaSessionService_OnSessionsChanged;

            // 订阅布局设置变更事件
            // Subscribe to layout settings changed event
            SettingsManager.LayoutSettingsChanged += SettingsManager_OnLayoutSettingsChanged;
            SettingsManager.AppearanceSettingsChanged += SettingsManager_OnAppearanceSettingsChanged;
            _audioControlViewModel.FlyoutToggleRequested += AudioControl_OnFlyoutToggleRequested;
            _audioControlViewModel.TrayContextMenuRequested += AudioControl_OnTrayContextMenuRequested;
            _mouseInputMonitor.LeftButtonPressed += MouseInputMonitor_OnLeftButtonPressed;
            ViewModel.OpenSettingsRequested += ViewModel_OpenSettingsRequested;

            // evaluate the initial state once the window is loaded
            Loaded += MainWindow_Loaded;
        }

        #region INavigationWindow methods

        /// <summary>返回导航控件；隐藏宿主不提供页面导航。/ Returns the navigation control; the hidden host does not expose page navigation.</summary>
        public INavigationView GetNavigation() => throw new NotImplementedException();

        /// <summary>尝试导航到页面类型。/ Attempts to navigate to a page type.</summary>
        public bool Navigate(Type pageType) => false;

        /// <summary>设置页面提供器；隐藏宿主不承载页面。/ Sets the page provider; the hidden host does not host pages.</summary>
        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) =>
            throw new NotImplementedException();

        /// <summary>设置导航服务提供器；隐藏宿主不使用该入口。/ Sets the navigation service provider; unused by the hidden host.</summary>
        public void SetServiceProvider(IServiceProvider serviceProvider) =>
            throw new NotImplementedException();

        /// <summary>显示宿主窗口。/ Shows the host window.</summary>
        public void ShowWindow() => Show();

        /// <summary>关闭宿主窗口。/ Closes the host window.</summary>
        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        #region Window Controller & others

        /// <summary>协调主窗口关闭和任务栏宿主解挂。/ Coordinates main-window shutdown and taskbar-host detachment.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel)
                return;

            _isClosing = true;
            _taskbarRecoveryCancellation?.Cancel();
            _taskbarRecoveryCancellation?.Dispose();
            _taskbarRecoveryCancellation = null;
            TaskbarEnvironmentRecovering = false;

            if (_isSystemThemeWatcherActive)
            {
                try
                {
                    SystemThemeWatcher.UnWatch(this);
                }
                catch (InvalidOperationException)
                {
                    // 窗口句柄可能已被异常的显示环境恢复销毁；关闭路径必须继续完成。
                    // An abnormal display recovery may already have destroyed the HWND; shutdown must continue.
                }
                _isSystemThemeWatcherActive = false;
            }

            CloseTaskbarWindow();
            var dynamicIslandWindow = _dynamicIslandWindow;
            _dynamicIslandWindow = null;
            dynamicIslandWindow?.Close();
            _audioControlFlyout.Close();
        }

        /// <summary>
        /// Raises the closed event.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            _mediaSessionService.SnapshotChanged -= MediaSessionService_OnSnapshotChanged;
            _mediaSessionService.SessionsChanged -= MediaSessionService_OnSessionsChanged;
            SettingsManager.LayoutSettingsChanged -= SettingsManager_OnLayoutSettingsChanged;
            SettingsManager.AppearanceSettingsChanged -= SettingsManager_OnAppearanceSettingsChanged;
            _audioControlViewModel.FlyoutToggleRequested -= AudioControl_OnFlyoutToggleRequested;
            _audioControlViewModel.TrayContextMenuRequested -= AudioControl_OnTrayContextMenuRequested;
            _mouseInputMonitor.LeftButtonPressed -= MouseInputMonitor_OnLeftButtonPressed;
            ViewModel.OpenSettingsRequested -= ViewModel_OpenSettingsRequested;
            // Make sure that closing this window will begin the process of closing the application.
            Application.Current.Shutdown();
        }

        /// <summary>初始化主窗口消息钩子和 TaskbarCreated 注册。/ Initializes the main-window hook and TaskbarCreated registration.</summary>
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
                RequestTaskbarEnvironmentRecovery();
                handled = true;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 等待任务栏环境稳定后重建任务栏子窗口，用于 Explorer 恢复和手动重载。
        /// Recreates the taskbar child window after the Shell stabilizes, for Explorer recovery and manual reloads.
        /// </summary>
        internal void RequestTaskbarEnvironmentRecovery()
        {
            if (_isClosing || SettingsManager.Current.WindowMode != WindowMode.Taskbar)
                return;

            if (TaskbarEnvironmentRecovering)
                return;

            TaskbarEnvironmentRecovering = true;
            _taskbarWindow?.SuspendForEnvironmentRecovery();

            var previous = _taskbarRecoveryCancellation;
            _taskbarRecoveryCancellation = new CancellationTokenSource();
            previous?.Cancel();
            previous?.Dispose();

            _ = RecoverTaskbarEnvironmentAsync(_taskbarRecoveryCancellation);
        }

        /// <summary>
        /// 从托盘或媒体栏菜单请求安全重建任务栏宿主。
        /// Requests a safe taskbar-host rebuild from the tray or media-bar menu.
        /// </summary>
        internal void RequestTaskbarHostReload()
        {
            if (_isClosing || SettingsManager.Current.WindowMode != WindowMode.Taskbar)
                return;

            TrayMenu.IsOpen = false;
            RequestTaskbarEnvironmentRecovery();
        }

        private async Task RecoverTaskbarEnvironmentAsync(CancellationTokenSource recovery)
        {
            var stableSamples = 0;
            IntPtr previousHandle = IntPtr.Zero;
            RECT previousRect = default;
            uint previousDpi = 0;
            var recreated = false;
            try
            {
                for (var attempt = 0; attempt < TaskbarRecoveryPolicy.MaximumAttempts; attempt++)
                {
                    await Task.Delay(TaskbarRecoveryPolicy.GetDelay(attempt), recovery.Token);
                    if (_isClosing || SettingsManager.Current.WindowMode != WindowMode.Taskbar)
                        return;

                    var taskbarHandle = _taskBarService.GetSelectedTaskbarHandle(
                        SettingsManager.Current.TaskbarBarSelectedMonitor, out _);
                    if (taskbarHandle == IntPtr.Zero ||
                        !_taskBarService.TryGetTaskbarRect(taskbarHandle, out var rect) ||
                        rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                    {
                        stableSamples = 0;
                        continue;
                    }

                    var dpi = GetDpiForWindow(taskbarHandle);
                    if (dpi == 0)
                    {
                        stableSamples = 0;
                        continue;
                    }

                    stableSamples = taskbarHandle == previousHandle &&
                                    rect.Equals(previousRect) &&
                                    dpi == previousDpi
                        ? stableSamples + 1
                        : 1;
                    previousHandle = taskbarHandle;
                    previousRect = rect;
                    previousDpi = dpi;
                    if (stableSamples < TaskbarRecoveryPolicy.RequiredStableSamples)
                        continue;

                    RecreateTaskbarWindow();
                    recreated = true;
                    return;
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(_taskbarRecoveryCancellation, recovery))
                {
                    _taskbarRecoveryCancellation = null;
                    TaskbarEnvironmentRecovering = false;
                    if (!recreated)
                        _taskbarWindow?.ResumeAfterEnvironmentRecovery();
                    recovery.Dispose();
                }
            }
        }

        #endregion

        /// <summary>
        /// Closes and re-creates the docked taskbar window (e.g. after Explorer restarted
        /// and destroyed the old taskbar together with our child window).
        /// </summary>
        public void RecreateTaskbarWindow()
        {
            if (SettingsManager.Current.WindowMode != WindowMode.Taskbar)
            {
                ActivateWindowMode(SettingsManager.Current.WindowMode);
                return;
            }

            CloseTaskbarWindow();

            _taskbarWindow = new TaskbarWindow(_taskBarService, ViewModel, this, _appearanceService, _occupiedAreaService);
            _taskbarWindow.ApplyAppearanceSettings();

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

        private void CloseTaskbarWindow()
        {
            // 先清除共享引用，避免重入的媒体回调访问已被 Explorer 或 Close() 销毁 HWND 的窗口。
            // Clear the published reference first so reentrant media callbacks cannot target
            // a Window whose HWND has already been destroyed by Explorer or Close().
            var taskbarWindow = _taskbarWindow;
            _taskbarWindow = null;
            if (taskbarWindow is null)
                return;

            taskbarWindow.SuspendForEnvironmentRecovery();
            taskbarWindow.DetachFromTaskbar();
            try
            {
                taskbarWindow.Close();
            }
            catch (InvalidOperationException)
            {
                // Explorer may already have destroyed the cross-process child HWND.
            }
        }

        private void MediaSessionService_OnSnapshotChanged(object? sender, MediaSnapshot snapshot)
        {
            if (_isClosing)
                return;

            _taskbarWindow?.ApplySnapshot(snapshot);
            _dynamicIslandWindow?.ApplySnapshot(snapshot);
        }

        private void MediaSessionService_OnSessionsChanged(IReadOnlyList<MediaSessionOption> options)
        {
            if (_isClosing)
                return;

            _taskbarWindow?.ApplySessions(options);
            _dynamicIslandWindow?.ApplySessions(options);
            ApplyTraySessions(options);
        }

        private void ApplyTraySessions(IReadOnlyList<MediaSessionOption> options)
        {
            TraySessionsMenuItem.Items.Clear();
            foreach (var option in options)
            {
                TraySessionsMenuItem.Items.Add(new MenuItem
                {
                    Header = option.DisplayName,
                    IsCheckable = true,
                    IsChecked = option.IsSelected,
                    Command = ViewModel.SelectMediaSessionCommand,
                    CommandParameter = option.Key
                });
            }
        }

        /// <summary>
        /// 处理布局设置变更事件：应用新的窗口模式和布局方向。
        /// Handle layout settings changed event: apply new window mode and layout orientation.
        /// </summary>
        private void SettingsManager_OnLayoutSettingsChanged(object? sender, LayoutSettingsChangedEventArgs e)
        {
            // 在 UI 线程上执行布局更新
            // Execute layout update on UI thread
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing)
                    return;

                ActivateWindowMode(e.WindowMode);
                _taskbarWindow?.ApplyLayoutSettings(e.WindowMode, e.OrientationMode);
                _dynamicIslandWindow?.ApplyLayoutSettings(e.OrientationMode);
                _dynamicIslandWindow?.ApplyAppearanceSettings();
            });
        }

        private void SettingsManager_OnAppearanceSettingsChanged(object? sender, AppearanceSettingsChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing)
                    return;

                UpdateSystemThemeWatcher(e.Appearance);
                _taskbarWindow?.ApplyAppearanceSettings();
                _dynamicIslandWindow?.ApplyAppearanceSettings();
            });
        }

        private void UpdateSystemThemeWatcher(AppearanceSettings appearance)
        {
            if (_isClosing)
                return;

            var shouldWatch = appearance.ApplicationThemeMode == ApplicationThemeMode.Automatic;
            if (shouldWatch == _isSystemThemeWatcherActive &&
                (!shouldWatch || _watchedBackdropMode == appearance.BackdropMode))
            {
                return;
            }

            if (_isSystemThemeWatcherActive)
            {
                SystemThemeWatcher.UnWatch(this);
            }

            if (shouldWatch)
            {
                SystemThemeWatcher.Watch(
                    this,
                    WindowBackdropType.None,
                    updateAccents: true);
            }

            _isSystemThemeWatcherActive = shouldWatch;
            _watchedBackdropMode = shouldWatch ? appearance.BackdropMode : null;
        }

        private void ActivateWindowMode(WindowMode mode)
        {
            if (mode == WindowMode.DynamicIsland)
            {
                _taskbarRecoveryCancellation?.Cancel();
                TaskbarEnvironmentRecovering = false;
                CloseTaskbarWindow();
                _dynamicIslandWindow ??= App.Services.GetRequiredService<DynamicIslandWindow>();
                _dynamicIslandWindow.ApplyLayoutSettings(SettingsManager.Current.LayoutOrientationMode);
                _dynamicIslandWindow.ApplyAppearanceSettings();
                _dynamicIslandWindow.ApplySessions(_mediaSessionService.CurrentSessionOptions);
                _dynamicIslandWindow.Show();
                if (_mediaSessionService.CurrentSnapshot is { } islandSnapshot)
                    _dynamicIslandWindow.ApplySnapshot(islandSnapshot);
                return;
            }

            _dynamicIslandWindow?.Close();
            _dynamicIslandWindow = null;
            if (_taskbarWindow is null)
            {
                _taskbarWindow = new TaskbarWindow(_taskBarService, ViewModel, this, _appearanceService, _occupiedAreaService);
                _taskbarWindow.ApplyAppearanceSettings();
                if (_mediaSessionService.CurrentSnapshot is { } snapshot)
                    _taskbarWindow.ApplySnapshot(snapshot);
                else
                    _mediaSessionService.RefreshNow();
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // The media bar lives in the docked TaskbarWindow; keep this window as an invisible host.
            Visibility = Visibility.Collapsed;

            ActivateWindowMode(SettingsManager.Current.WindowMode);
        }

        private async void AudioControl_OnFlyoutToggleRequested(TrayIconBounds? bounds)
        {
            if (_isClosing)
                return;

            await _audioControlFlyout.ToggleAsync(bounds);
        }

        private void ViewModel_OpenSettingsRequested(object? sender, EventArgs e)
        {
            if (_isClosing)
                return;

            _settingsWindow ??= _settingsWindowFactory();
            _settingsWindow.Closed -= SettingsWindow_Closed;
            _settingsWindow.Closed += SettingsWindow_Closed;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        private void SettingsWindow_Closed(object? sender, EventArgs e)
        {
            if (sender is SettingsWindow window)
            {
                window.Closed -= SettingsWindow_Closed;
                if (ReferenceEquals(_settingsWindow, window))
                    _settingsWindow = null;
            }
        }

        private void AudioControl_OnTrayContextMenuRequested(TrayIconBounds? bounds)
        {
            if (_isClosing)
                return;

            _audioControlFlyout.Hide();
            ApplyTraySessions(_mediaSessionService.CurrentSessionOptions);
            ReloadTaskbarHostMenuItem.IsEnabled =
                SettingsManager.Current.WindowMode == WindowMode.Taskbar;
            TrayMenu.DataContext = this;
            TrayMenu.PlacementTarget = this;
            TrayMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            TrayMenu.IsOpen = true;
        }

        private void ReloadTaskbarHostMenuItem_Click(object sender, RoutedEventArgs e) =>
            RequestTaskbarHostReload();

        private void MouseInputMonitor_OnLeftButtonPressed(object? sender, NativeMouseButtonEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing)
                    return;

                ContextMenuHelper.CloseIfOutside(TrayMenu, e.ScreenX, e.ScreenY);
                _taskbarWindow?.CloseContextMenuIfOutside(e.ScreenX, e.ScreenY);
                _dynamicIslandWindow?.CloseContextMenuIfOutside(e.ScreenX, e.ScreenY);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

    }
}
