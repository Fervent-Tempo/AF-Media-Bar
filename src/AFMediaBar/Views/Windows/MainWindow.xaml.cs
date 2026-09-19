using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;
using AFMediaBar.ViewModels.Windows;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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

        private readonly TaskbarWindowViewModel _taskbarViewModel;
        private readonly ITaskbarDockService _taskBarService;
        private readonly AudioControlViewModel _audioControlViewModel;
        private readonly AudioControlFlyoutWindow _audioControlFlyout;
        private readonly NativeMouseInputMonitor _mouseInputMonitor;
        private readonly WindowAppearanceService _appearanceService;
        private readonly ScreenBackgroundSampler _screenBackgroundSampler;
        private readonly TaskbarOccupiedAreaService _occupiedAreaService;
        private readonly TaskbarLengthConstraintsService _taskbarLengthConstraints;
        private readonly Func<SettingsWindow> _settingsWindowFactory;
        private readonly GlobalInteractionRouter _interactionRouter;
        private readonly AudioInteractionService _audioInteractionService;
        private readonly AudioMonitorService _audioMonitorService;
        private readonly MediaSourceActivationService _sourceActivationService;
        private readonly SystemMetricsMonitorService _systemMetricsMonitor;
        private readonly Func<TaskbarFullPanelWindow> _fullPanelFactory;
        private readonly IDisplayMonitorService _displayMonitorService;
        private readonly TrackChangeNotificationCoordinator _trackChangeNotificationCoordinator;
        private readonly Func<TrackChangeNotificationWindow> _trackChangeNotificationFactory;
        private readonly ShellTrayIconService _trayIconService;
        private readonly UpdateService _updateService;

        /// <summary>后台剪枝协调器：任务栏宿主订阅它的档位变化，因此这里只做转交，不在宿主里查询电源状态。
        /// The background prune coordinator: the taskbar host subscribes to its level changes, so this field only hands it over and the host never
        /// queries the power state itself.</summary>
        private readonly MemoryPruneCoordinator _memoryPruneCoordinator;
        private TaskbarWindow? _taskbarWindow;
        private DynamicIslandWindow? _dynamicIslandWindow;
        private SettingsWindow? _settingsWindow;
        private TaskbarFullPanelWindow? _fullPanelWindow;
        private TrackChangeNotificationWindow? _trackChangeNotificationWindow;
        private string? _effectiveTaskbarMonitorDeviceId;
        private DateTime _fullPanelClosedAtUtc;
        private int _taskbarCreatedMessage;
        private bool _isSystemThemeWatcherActive;
        private bool _isClosing;
        private ApplicationBackdropMode? _watchedBackdropMode;
        private CancellationTokenSource? _taskbarRecoveryCancellation;

        /// <summary>
        /// 已经用系统通知提醒过的版本。每个版本只提醒一次：否则每天一次的自动检查都会再弹一遍。
        /// Version already announced through a system notification. Each version is announced once; otherwise every
        /// daily automatic check would pop up again.
        /// </summary>
        private string? _notifiedUpdateVersion;

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
            TaskbarWindowViewModel taskbarViewModel,
            ITaskbarDockService taskBarService,
            AudioControlViewModel audioControlViewModel,
            AudioControlFlyoutWindow audioControlFlyout,
            NativeMouseInputMonitor mouseInputMonitor,
            WindowAppearanceService appearanceService,
            ScreenBackgroundSampler screenBackgroundSampler,
            TaskbarOccupiedAreaService occupiedAreaService,
            TaskbarLengthConstraintsService taskbarLengthConstraints,
            Func<SettingsWindow> settingsWindowFactory,
            GlobalInteractionRouter interactionRouter,
            AudioInteractionService audioInteractionService,
            AudioMonitorService audioMonitorService,
            MediaSourceActivationService sourceActivationService,
            SystemMetricsMonitorService systemMetricsMonitor,
            Func<TaskbarFullPanelWindow> fullPanelFactory,
            IDisplayMonitorService displayMonitorService,
            TrackChangeNotificationCoordinator trackChangeNotificationCoordinator,
            Func<TrackChangeNotificationWindow> trackChangeNotificationFactory,
            ShellTrayIconService trayIconService,
            UpdateService updateService,
            MemoryPruneCoordinator memoryPruneCoordinator)
        {
            ViewModel = viewModel;
            _taskbarViewModel = taskbarViewModel;
            DataContext = this;

            _taskBarService = taskBarService;
            _audioControlViewModel = audioControlViewModel;
            _audioControlFlyout = audioControlFlyout;
            _mouseInputMonitor = mouseInputMonitor;
            _appearanceService = appearanceService;
            _screenBackgroundSampler = screenBackgroundSampler;
            _occupiedAreaService = occupiedAreaService;
            _taskbarLengthConstraints = taskbarLengthConstraints;
            _settingsWindowFactory = settingsWindowFactory;
            _interactionRouter = interactionRouter;
            _audioInteractionService = audioInteractionService;
            _audioMonitorService = audioMonitorService;
            _sourceActivationService = sourceActivationService;
            _systemMetricsMonitor = systemMetricsMonitor;
            _fullPanelFactory = fullPanelFactory;
            _displayMonitorService = displayMonitorService;
            _trackChangeNotificationCoordinator = trackChangeNotificationCoordinator;
            _trackChangeNotificationFactory = trackChangeNotificationFactory;
            _trayIconService = trayIconService;
            _updateService = updateService;
            _memoryPruneCoordinator = memoryPruneCoordinator;

            InitializeComponent();
            UpdateSystemThemeWatcher(SettingsManager.Current.Appearance);


            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            ContextMenuHelper.AttachOutsideClickDismissal(TrayMenu);
            _appearanceService.Attach(TrayMenu, this);
            TrayMenu.OpenSettingsRequested += ViewModel_OpenSettingsRequested;
            TrayMenu.OpenUpdateSettingsRequested += ViewModel_OpenUpdateSettingsRequested;
            TrayMenu.ReloadTaskbarHostRequested += TrayMenu_ReloadTaskbarHostRequested;

            // 快照事件已在服务内调度到 UI 线程，这里只负责转发给任务栏窗口。
            App.Services.GetRequiredService<MediaSessionService>().SnapshotChanged += MediaSessionService_OnSnapshotChanged;
            App.Services.GetRequiredService<MediaSessionService>().SessionsChanged += MediaSessionService_OnSessionsChanged;

            // 订阅布局设置变更事件
            // Subscribe to layout settings changed event
            SettingsManager.LayoutSettingsChanged += SettingsManager_OnLayoutSettingsChanged;
            SettingsManager.AppearanceSettingsChanged += SettingsManager_OnAppearanceSettingsChanged;
            ApplicationThemeManager.Changed += ApplicationThemeManager_OnChanged;
            SettingsManager.LyricsSettingsChanged += SettingsManager_OnLyricsSettingsChanged;
            SettingsManager.TaskbarExperienceSettingsChanged += SettingsManager_OnTaskbarExperienceSettingsChanged;
            SettingsManager.InteractionSettingsChanged += SettingsManager_OnTaskbarExperienceSettingsChanged;
            SettingsManager.TaskbarTargetMonitorChanged += SettingsManager_OnTaskbarTargetMonitorChanged;
            _displayMonitorService.MonitorsChanged += DisplayMonitorService_OnMonitorsChanged;
            _trackChangeNotificationCoordinator.NotificationRequested += TrackChangeNotificationCoordinator_OnNotificationRequested;
            _trackChangeNotificationCoordinator.NotificationContentUpdated += TrackChangeNotificationCoordinator_OnNotificationContentUpdated;
            _trackChangeNotificationCoordinator.DismissRequested += TrackChangeNotificationCoordinator_OnDismissRequested;
            _audioControlViewModel.FlyoutToggleRequested += AudioControl_OnFlyoutToggleRequested;
            _audioControlViewModel.TrayContextMenuRequested += AudioControl_OnTrayContextMenuRequested;
            _audioControlViewModel.OutputDeviceMenuRequested += AudioControl_OnOutputDeviceMenuRequested;
            _audioControlViewModel.CurrentAppVolumeMenuRequested += AudioControl_OnCurrentAppVolumeMenuRequested;
            _audioControlViewModel.SettingsOpenRequested += ViewModel_OpenSettingsRequested;
            _mouseInputMonitor.LeftButtonPressed += MouseInputMonitor_OnLeftButtonPressed;
            _taskbarViewModel.OpenSettingsRequested += ViewModel_OpenSettingsRequested;
            _taskbarViewModel.OpenUpdateSettingsRequested += ViewModel_OpenUpdateSettingsRequested;

            // 发现新版本时由宿主弹一次系统通知，点击通知把用户带到"应用与关于"。
            // 通知只表达"有新版本"：下载与安装都由用户在该页显式触发。
            // The host raises one system notification when a newer version is found, and clicking it takes the user
            // to "application and about". The notification only says "a newer version exists": both downloading and
            // installing are started explicitly on that page.
            _updateService.UpdateStateChanged += UpdateService_OnStateChanged;
            _trayIconService.NotificationClicked += TrayIconService_OnNotificationClicked;

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

            // ContextMenu Popup HWNDs are not Application.Windows entries. Close every
            // menu explicitly before the hidden host begins shutting down.
            _taskbarWindow?.ClosePlayerMenu();
            if (_dynamicIslandWindow is not null)
                _dynamicIslandWindow.ClosePlayerMenu();

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
            _fullPanelWindow?.RequestClose();
            _fullPanelWindow = null;
            var notificationWindow = _trackChangeNotificationWindow;
            _trackChangeNotificationWindow = null;
            notificationWindow?.HideImmediately();
            notificationWindow?.Close();
        }

        /// <summary>
        /// Raises the closed event.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            App.Services.GetRequiredService<MediaSessionService>().SnapshotChanged -= MediaSessionService_OnSnapshotChanged;
            App.Services.GetRequiredService<MediaSessionService>().SessionsChanged -= MediaSessionService_OnSessionsChanged;
            SettingsManager.LayoutSettingsChanged -= SettingsManager_OnLayoutSettingsChanged;
            SettingsManager.AppearanceSettingsChanged -= SettingsManager_OnAppearanceSettingsChanged;
            ApplicationThemeManager.Changed -= ApplicationThemeManager_OnChanged;
            SettingsManager.LyricsSettingsChanged -= SettingsManager_OnLyricsSettingsChanged;
            SettingsManager.TaskbarExperienceSettingsChanged -= SettingsManager_OnTaskbarExperienceSettingsChanged;
            SettingsManager.InteractionSettingsChanged -= SettingsManager_OnTaskbarExperienceSettingsChanged;
            SettingsManager.TaskbarTargetMonitorChanged -= SettingsManager_OnTaskbarTargetMonitorChanged;
            _displayMonitorService.MonitorsChanged -= DisplayMonitorService_OnMonitorsChanged;
            _trackChangeNotificationCoordinator.NotificationRequested -= TrackChangeNotificationCoordinator_OnNotificationRequested;
            _trackChangeNotificationCoordinator.NotificationContentUpdated -= TrackChangeNotificationCoordinator_OnNotificationContentUpdated;
            _trackChangeNotificationCoordinator.DismissRequested -= TrackChangeNotificationCoordinator_OnDismissRequested;
            _audioControlViewModel.FlyoutToggleRequested -= AudioControl_OnFlyoutToggleRequested;
            _audioControlViewModel.TrayContextMenuRequested -= AudioControl_OnTrayContextMenuRequested;
            _audioControlViewModel.OutputDeviceMenuRequested -= AudioControl_OnOutputDeviceMenuRequested;
            _audioControlViewModel.CurrentAppVolumeMenuRequested -= AudioControl_OnCurrentAppVolumeMenuRequested;
            _audioControlViewModel.SettingsOpenRequested -= ViewModel_OpenSettingsRequested;
            _mouseInputMonitor.LeftButtonPressed -= MouseInputMonitor_OnLeftButtonPressed;
            TrayMenu.OpenSettingsRequested -= ViewModel_OpenSettingsRequested;
            TrayMenu.OpenUpdateSettingsRequested -= ViewModel_OpenUpdateSettingsRequested;
            TrayMenu.ReloadTaskbarHostRequested -= TrayMenu_ReloadTaskbarHostRequested;
            _taskbarViewModel.OpenSettingsRequested -= ViewModel_OpenSettingsRequested;
            _taskbarViewModel.OpenUpdateSettingsRequested -= ViewModel_OpenUpdateSettingsRequested;
            _updateService.UpdateStateChanged -= UpdateService_OnStateChanged;
            _trayIconService.NotificationClicked -= TrayIconService_OnNotificationClicked;
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
                _displayMonitorService.Refresh();
                RequestTaskbarEnvironmentRecovery();
                handled = true;
            }
            else if (msg == WM_DISPLAYCHANGE)
            {
                _displayMonitorService.Refresh();
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
                        SettingsManager.Current.TaskbarTargetMonitorDeviceId, out _);
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

            _taskbarWindow = CreateTaskbarWindow();
            _taskbarWindow.ApplyAppearanceSettings();

            // Replay the latest snapshot; if none exists yet, force a synchronous refresh.
            if (App.Services.GetRequiredService<MediaSessionService>().CurrentSnapshot is { } snapshot)
            {
                _taskbarWindow.ApplySnapshot(snapshot);
            }
            else
            {
                App.Services.GetRequiredService<MediaSessionService>().RefreshNow();
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
            TrayMenu.ApplySessions(options);
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

        private void SettingsManager_OnLyricsSettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing)
                    return;

                var snapshot = App.Services.GetRequiredService<MediaSessionService>().CurrentSnapshot ?? MediaSnapshot.Disconnected;
                _taskbarWindow?.ApplySnapshot(snapshot);
                _dynamicIslandWindow?.ApplySnapshot(snapshot);
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
                _dynamicIslandWindow.ApplySessions(App.Services.GetRequiredService<MediaSessionService>().CurrentSessionOptions);
                _dynamicIslandWindow.Show();
                if (App.Services.GetRequiredService<MediaSessionService>().CurrentSnapshot is { } islandSnapshot)
                    _dynamicIslandWindow.ApplySnapshot(islandSnapshot);
                return;
            }

            _dynamicIslandWindow?.Close();
            _dynamicIslandWindow = null;
            if (_taskbarWindow is null)
            {
                _taskbarWindow = CreateTaskbarWindow();
                _taskbarWindow.ApplyAppearanceSettings();
                if (App.Services.GetRequiredService<MediaSessionService>().CurrentSnapshot is { } snapshot)
                    _taskbarWindow.ApplySnapshot(snapshot);
                else
                    App.Services.GetRequiredService<MediaSessionService>().RefreshNow();
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // The media bar lives in the docked TaskbarWindow; keep this window as an invisible host.
            Visibility = Visibility.Collapsed;

            _displayMonitorService.Refresh();
            _effectiveTaskbarMonitorDeviceId = ResolveEffectiveTaskbarMonitorDeviceId();
            ActivateWindowMode(SettingsManager.Current.WindowMode);
        }

        private void TrackChangeNotificationCoordinator_OnNotificationRequested(
            object? sender,
            TrackChangeNotificationRequest request)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing)
                    return;

                _trackChangeNotificationWindow ??= _trackChangeNotificationFactory();
                _trackChangeNotificationWindow.Closed -= TrackChangeNotificationWindow_Closed;
                _trackChangeNotificationWindow.Closed += TrackChangeNotificationWindow_Closed;
                _trackChangeNotificationWindow.ShowNotification(request);
            });
        }

        private void ApplicationThemeManager_OnChanged(ApplicationTheme theme, Color accent)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing)
                    return;
                _taskbarWindow?.ApplyAppearanceSettings();
                _dynamicIslandWindow?.ApplyAppearanceSettings();
            }, DispatcherPriority.Background);
        }

        private void TrackChangeNotificationCoordinator_OnDismissRequested(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(() => _trackChangeNotificationWindow?.HideImmediately());

        private void TrackChangeNotificationCoordinator_OnNotificationContentUpdated(
            object? sender,
            MediaSnapshot snapshot) =>
            Dispatcher.BeginInvoke(() => _trackChangeNotificationWindow?.UpdateSnapshot(snapshot));

        private void TrackChangeNotificationWindow_Closed(object? sender, EventArgs e)
        {
            if (sender is not TrackChangeNotificationWindow window)
                return;

            window.Closed -= TrackChangeNotificationWindow_Closed;
            if (ReferenceEquals(window, _trackChangeNotificationWindow))
                _trackChangeNotificationWindow = null;

            // 通知窗口每一首歌都会关闭一次，而它刚显示过一张封面位图：关掉之后做一遍温和回收（后台 GC，不碰工作集），
            // 由剪枝协调器自己的 5 秒节流保证不会变成"每首歌一次 GC 风暴"。
            // The notification window closes once per track and it has just shown a cover bitmap, so a gentle reclaim follows it — a background
            // collection that leaves the working set alone — with the coordinator's own five-second throttle keeping it from becoming a collection per
            // track.
            _memoryPruneCoordinator.RequestTrim(MemoryTrimTrigger.PanelClosed);
        }

        private void SettingsManager_OnTaskbarTargetMonitorChanged(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(() => ApplyEffectiveTaskbarMonitorChange());

        private void DisplayMonitorService_OnMonitorsChanged(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(() => ApplyEffectiveTaskbarMonitorChange());

        private void ApplyEffectiveTaskbarMonitorChange()
        {
            if (_isClosing)
                return;

            var nextDeviceId = ResolveEffectiveTaskbarMonitorDeviceId();
            if (string.Equals(_effectiveTaskbarMonitorDeviceId, nextDeviceId, StringComparison.OrdinalIgnoreCase))
                return;

            var hadPreviousTarget = !string.IsNullOrWhiteSpace(_effectiveTaskbarMonitorDeviceId);
            _effectiveTaskbarMonitorDeviceId = nextDeviceId;
            if (!hadPreviousTarget || SettingsManager.Current.WindowMode != WindowMode.Taskbar)
                return;

            _fullPanelWindow?.RequestClose();
            RequestTaskbarEnvironmentRecovery();
        }

        private string? ResolveEffectiveTaskbarMonitorDeviceId() =>
            _displayMonitorService.ResolveFixedMonitor(SettingsManager.Current.TaskbarTargetMonitorDeviceId)?.DeviceId;

        private async void AudioControl_OnFlyoutToggleRequested(TrayIconBounds? bounds)
        {
            if (_isClosing)
                return;

            await _audioControlFlyout.ToggleAsync(bounds);
        }

        /// <summary>
        /// 托盘点击要求打开输出设备菜单：交给任务栏宿主的紧凑菜单实现，锚点用托盘图标的位置，
        /// 因此菜单出现在用户刚刚点击的地方。
        /// The tray click asks for the output-device menu: the taskbar host owns that compact menu, and the anchor is the tray
        /// icon's position, so the menu appears where the user just clicked.
        /// </summary>
        private async void AudioControl_OnOutputDeviceMenuRequested(TrayIconBounds? bounds) =>
            await ShowTrayCompactMenuAsync(TaskbarCompactFlyoutMode.OutputDevice, bounds);

        /// <inheritdoc cref="AudioControl_OnOutputDeviceMenuRequested" />
        private async void AudioControl_OnCurrentAppVolumeMenuRequested(TrayIconBounds? bounds) =>
            await ShowTrayCompactMenuAsync(TaskbarCompactFlyoutMode.Volume, bounds);

        private async Task ShowTrayCompactMenuAsync(TaskbarCompactFlyoutMode mode, TrayIconBounds? bounds)
        {
            if (_isClosing || _taskbarWindow is null)
                return;

            await _taskbarWindow.ShowCompactMenuAsync(mode, bounds);
        }

        private void SettingsManager_OnTaskbarExperienceSettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing) return;
                _taskbarWindow?.ApplyExperienceSettings();
            });
        }

        private TaskbarWindow CreateTaskbarWindow()
        {
            var window = new TaskbarWindow(
                _taskBarService,
                _taskbarViewModel,
                this,
                _appearanceService,
                _occupiedAreaService,
                _taskbarLengthConstraints,
                _interactionRouter,
                _audioInteractionService,
                _audioMonitorService,
                _sourceActivationService,
                _systemMetricsMonitor,
                _screenBackgroundSampler,
                _mouseInputMonitor,
                _memoryPruneCoordinator);
            window.OpenFullPanelRequested += TaskbarWindow_OpenFullPanelRequested;
            return window;
        }

        private void TaskbarWindow_OpenFullPanelRequested(object? sender, EventArgs e)
        {
            if (_isClosing || sender is not TaskbarWindow taskbarWindow)
                return;

            if (_fullPanelWindow is null && DateTime.UtcNow - _fullPanelClosedAtUtc < TimeSpan.FromMilliseconds(350))
                return;

            _fullPanelWindow ??= _fullPanelFactory();
            _fullPanelWindow.Closed -= FullPanelWindow_Closed;
            _fullPanelWindow.Closed += FullPanelWindow_Closed;
            _fullPanelWindow.ToggleNear(taskbarWindow.GetMediaBarScreenBounds());
        }

        private void FullPanelWindow_Closed(object? sender, EventArgs e)
        {
            if (sender is TaskbarFullPanelWindow window)
            {
                window.Closed -= FullPanelWindow_Closed;
                if (ReferenceEquals(window, _fullPanelWindow))
                    _fullPanelWindow = null;
                _fullPanelClosedAtUtc = DateTime.UtcNow;

                // 完整层是歌词与封面的另一个消费者，关闭后做一遍温和回收。
                // The full panel is another consumer of lyrics and artwork, so a gentle reclaim follows its close.
                _memoryPruneCoordinator.RequestTrim(MemoryTrimTrigger.PanelClosed);
            }
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

        /// <summary>
        /// 打开设置窗口并直接落在「应用与关于」：托盘菜单、媒体栏右键菜单与系统通知都走这条路，
        /// 用户因此不需要自己在六个页面里再找一次。
        /// Opens the settings window straight on "application": the tray menu, the media-bar context menu
        /// and the system notification all take this path, so the user never has to find that page again among six.
        /// </summary>
        private void ViewModel_OpenUpdateSettingsRequested(object? sender, EventArgs e)
        {
            if (_isClosing)
                return;

            ViewModel_OpenSettingsRequested(sender, e);
            if (_settingsWindow is not { } settingsWindow)
                return;

            // 新建的窗口要等布局完成才接受导航，因此把跳转排到 Loaded 之后：立刻调用时导航视图还没有内容宿主，
            // 表现就是"点了没反应"。
            // A freshly created window only accepts navigation once its layout exists, so the jump is queued behind
            // Loaded: calling it immediately finds a navigation view with no content host yet, which looks exactly
            // like a click that does nothing.
            settingsWindow.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => settingsWindow.Navigate(typeof(AFMediaBar.Views.Pages.ApplicationPage))));
        }

        private void UpdateService_OnStateChanged(UpdateState state)
        {
            if (_isClosing)
                return;

            if (state.Phase is not (UpdatePhase.Available or UpdatePhase.ManualOnly))
                return;

            if (state.AvailableVersion is not { } version ||
                string.Equals(_notifiedUpdateVersion, version, StringComparison.OrdinalIgnoreCase))
                return;

            _notifiedUpdateVersion = version;

            // 通知文本在发布的这一刻才取：窗口长期存活，缓存成字段就会在切换语言后继续用旧语言弹通知。
            // The notification text is fetched at the moment it is raised: this window lives as long as the process, so
            // caching it in a field would keep announcing in the old language after a switch.
            _trayIconService.TryShowNotification(
                Translations.Get("Update.Notification.Title"),
                Translations.Format("Update.Notification.Body", version, state.CurrentVersion));
        }

        private void TrayIconService_OnNotificationClicked(object? sender, EventArgs e)
        {
            if (_isClosing)
                return;

            ViewModel_OpenUpdateSettingsRequested(sender, EventArgs.Empty);
        }

        private void SettingsWindow_Closed(object? sender, EventArgs e)
        {
            if (sender is SettingsWindow window)
            {
                window.Closed -= SettingsWindow_Closed;
                if (ReferenceEquals(_settingsWindow, window))
                    _settingsWindow = null;

                // 设置窗口是最大的界面树与缓存持有者（六个页面、说明图与全部设置控件），因此走**深度**回收：
                // 关掉设置页之后用户通常回去做别的事，短时间不会再开，交还工作集换来的页错误不落在交互路径上。
                // The settings window holds the largest visual tree and the most caches — six pages, the diagrams, and every settings control — so it gets
                // the **deep** reclaim: after closing the settings the user usually goes off to do something else and will not reopen it right away, which
                // keeps the page faults caused by returning the working set off the interaction path.
                _memoryPruneCoordinator.RequestTrim(MemoryTrimTrigger.SettingsWindowClosed);
            }
        }

        private void AudioControl_OnTrayContextMenuRequested(TrayIconBounds? bounds)
        {
            if (_isClosing)
                return;

            _audioControlFlyout.Hide();
            TrayMenu.ApplySessions(App.Services.GetRequiredService<MediaSessionService>().CurrentSessionOptions);
            TrayMenu.IsReloadTaskbarHostEnabled =
                SettingsManager.Current.WindowMode == WindowMode.Taskbar;
            TrayMenu.PlacementTarget = this;
            TrayMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            TrayMenu.IsOpen = true;
        }

        private void TrayMenu_ReloadTaskbarHostRequested(object? sender, EventArgs e) =>
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
