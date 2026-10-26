using System.Diagnostics;
using AFMediaBar.Classes.Models;
using AFMediaBar.Components;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Services.Notifications;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;
using AFMediaBar.ViewModels.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows
{
    /// <summary>
    /// 隐藏的应用宿主窗口，协调媒体栏模式、托盘入口和任务栏恢复。
    /// Invisible application host coordinating media-bar modes, tray entry points, and taskbar recovery.
    /// </summary>
    public partial class MainWindow : ITaskbarWindowHostActions
    {
        private readonly FullPanelWindowHost _fullPanels;
        public MainWindowViewModel ViewModel { get; }
        private readonly IApplicationExitService _exitService;

        private readonly TaskbarWindowViewModel _taskbarViewModel;
        private readonly TaskbarFallbackNotificationGate _placementNotificationGate = new();
        private readonly ITaskbarDockService _taskBarService;
        private readonly MediaSessionService _mediaSessionService;
        private readonly AudioControlViewModel _audioControlViewModel;
        private readonly Func<AudioControlFlyoutWindow> _audioControlFlyoutFactory;
        private AudioControlFlyoutWindow? _audioControlFlyout;
        private int _audioControlFlyoutRequestVersion;
        private readonly AppLogService _logService;
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
        private readonly IDisplayMonitorService _displayMonitorService;
        private readonly TrackChangeNotificationCoordinator _trackChangeNotificationCoordinator;
        private readonly Func<TrackChangeNotificationWindow> _trackChangeNotificationFactory;
        private readonly ShellTrayIconService _trayIconService;
        private readonly ISystemNotificationService _systemNotifications;
        private readonly LyricsRecoveryNotificationCoordinator _lyricsRecoveryNotifications;
        private readonly UpdateService _updateService;
        private readonly DispatcherTimer _taskbarTopologyTimer;

        /// <summary>后台剪枝协调器：任务栏宿主订阅它的档位变化，因此这里只做转交，不在宿主里查询电源状态。
        /// The background prune coordinator: the taskbar host subscribes to its level changes, so this field only hands it over and the host never
        /// queries the power state itself.</summary>
        private readonly MemoryPruneCoordinator _memoryPruneCoordinator;
        private readonly List<TaskbarWindow> _taskbarWindows = [];
        private SettingsWindow? _settingsWindow;
        private TrackChangeNotificationWindow? _trackChangeNotificationWindow;
        private string? _effectiveTaskbarTargetSignature;
        private int _taskbarCreatedMessage;
        private bool _isClosing;
        private bool _layoutSettingsUpdateScheduled;
        private bool _experienceSettingsUpdateScheduled;
        private LayoutSettingsChangedEventArgs? _latestLayoutSettings;
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
        private readonly AFMediaBar.Components.MemoryContextMenu _memoryMenu;
        private readonly ContextMenu _batteryMenu;

        private readonly QuickLaunchService _quickLaunch;
        public MainWindow(
            AFMediaBar.Classes.Services.Audio.BluetoothBatteryService batterySource,
            AFMediaBar.ViewModels.Pages.MemoryToolsViewModel memoryTools,
            MainWindowViewModel viewModel,
            TaskbarWindowViewModel taskbarViewModel,
            ITaskbarDockService taskBarService,
            AudioControlViewModel audioControlViewModel,
            Func<AudioControlFlyoutWindow> audioControlFlyoutFactory,
            AppLogService logService,
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
            FullPanelWindowHost fullPanels,
            IDisplayMonitorService displayMonitorService,
            TrackChangeNotificationCoordinator trackChangeNotificationCoordinator,
            Func<TrackChangeNotificationWindow> trackChangeNotificationFactory,
            ShellTrayIconService trayIconService,
            ISystemNotificationService systemNotifications,
            LyricsRecoveryNotificationCoordinator lyricsRecoveryNotifications,
            UpdateService updateService,
            MemoryPruneCoordinator memoryPruneCoordinator,
            IApplicationExitService exitService,
            BarRuntimeCoordinator runtime,
            Func<string, ITaskbarWindowHostActions, TaskbarWindow> taskbarFactory,
            Func<string, DisplayMonitorInfo, FloatingBarWindow> floatingFactory,
            MediaSessionService mediaSessionService,
            AFMediaBar.ViewModels.Components.AFContextMenuViewModel menuViewModel,
            QuickLaunchService quickLaunch)
        {
            _quickLaunch = quickLaunch;
            ViewModel = viewModel;
            _memoryMenu = new AFMediaBar.Components.MemoryContextMenu(memoryTools, () =>
            {
                ViewModel_OpenSettingsRequested(this, EventArgs.Empty);
                _settingsWindow?.NavigateToGroup(SettingsPageKey.Application, "Memory.Title");
            });
            _batteryMenu = new ContextMenu();
            _batteryMenu.SetResourceReference(StyleProperty, typeof(ContextMenu));
            var refreshBattery = new Wpf.Ui.Controls.MenuItem();
            refreshBattery.SetResourceReference(HeaderedItemsControl.HeaderProperty, "Loc.Battery.Refresh");
            refreshBattery.Click += (_, _) => batterySource.RequestRefresh();
            var batterySettings = new Wpf.Ui.Controls.MenuItem();
            batterySettings.SetResourceReference(HeaderedItemsControl.HeaderProperty, "Loc.Battery.Open");
            batterySettings.Click += (_, _) =>
            {
                ViewModel_OpenSettingsRequested(this, EventArgs.Empty);
                _settingsWindow?.NavigateToGroup(SettingsPageKey.DeviceManagement, "Battery.Title");
            };
            _batteryMenu.Items.Add(refreshBattery); _batteryMenu.Items.Add(batterySettings);
            Closed += (_, _) => { _memoryMenu.IsOpen = false; _batteryMenu.IsOpen = false; };
            _exitService = exitService;
            _runtime = runtime;
            _taskbarFactory = taskbarFactory;
            _floatingFactory = floatingFactory;
            SettingsManager.SettingsChanged += RuntimeSettingsChanged;
            _taskbarViewModel = taskbarViewModel;
            DataContext = this;
            _mediaSessionService = mediaSessionService;
            _taskBarService = taskBarService;
            _audioControlViewModel = audioControlViewModel;
            _audioControlFlyoutFactory = audioControlFlyoutFactory;
            _logService = logService;
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
            _fullPanels = fullPanels;
            _fullPanels.PanelClosed += FullPanelHostClosed;
            _displayMonitorService = displayMonitorService;
            _trackChangeNotificationCoordinator = trackChangeNotificationCoordinator;
            _trackChangeNotificationFactory = trackChangeNotificationFactory;
            _trayIconService = trayIconService;
            _systemNotifications = systemNotifications;
            _lyricsRecoveryNotifications = lyricsRecoveryNotifications;
            _updateService = updateService;
            _memoryPruneCoordinator = memoryPruneCoordinator;
            _memoryPruneCoordinator.LevelChanged += RuntimePruneChanged;

            // Shell 可能在 TaskbarCreated 之后才稍晚创建副任务栏，而且切换 Windows 的“在所有显示器上显示任务栏”不会改变显示器列表。
            // 这个低成本监视只比较句柄、不观察任务栏矩形，因此能补齐拓扑，又不会让自动隐藏动画触发宿主重建。
            // Shell may create a secondary taskbar slightly after TaskbarCreated, and toggling Windows' "show taskbar on all displays" does not change
            // the monitor list. This inexpensive handle-only watcher repairs that topology without observing taskbar rectangles, so auto-hide motion
            // never triggers a host rebuild.
            _taskbarTopologyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _taskbarTopologyTimer.Tick += (_, _) =>
            {
                if (!_isClosing && !TaskbarEnvironmentRecovering && SettingsManager.Current.WindowMode == WindowMode.Taskbar)
                    ApplyEffectiveTaskbarMonitorChange();
                if (!_isClosing && SettingsManager.Current.WindowMode == WindowMode.FloatingBar) QueueRuntimeRefresh();
                TryNotifySmallTaskbar();
            };

            InitializeComponent();
            TrayMenu.ViewModel = menuViewModel;


            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            ContextMenuHelper.AttachOutsideClickDismissal(TrayMenu);
            _appearanceService.Attach(TrayMenu, this);
            TrayMenu.OpenSettingsRequested += ViewModel_OpenSettingsRequested;
            TrayMenu.ExitRequested += TrayMenu_ExitRequested;
            TrayMenu.OpenUpdateSettingsRequested += ViewModel_OpenUpdateSettingsRequested;
            TrayMenu.ReloadTaskbarHostRequested += TrayMenu_ReloadTaskbarHostRequested;
            TrayMenu.QuickLaunchRequested += TrayMenu_QuickLaunchRequested;

            // 快照事件已在服务内调度到 UI 线程，这里只负责转发给任务栏窗口。
            _mediaSessionService.SnapshotChanged += MediaSessionService_OnSnapshotChanged;
            _mediaSessionService.SessionsChanged += MediaSessionService_OnSessionsChanged;

            // 订阅布局设置变更事件
            // Subscribe to layout settings changed event
            SettingsManager.LayoutSettingsChanged += SettingsManager_OnLayoutSettingsChanged;
            SettingsManager.AppearanceSettingsChanged += SettingsManager_OnAppearanceSettingsChanged;
            SettingsManager.TaskbarAppearanceSettingsChanged += SettingsManager_OnTaskbarAppearanceSettingsChanged;
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
            _audioControlViewModel.InputDeviceMenuRequested += AudioControl_OnInputDeviceMenuRequested;
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
            _systemNotifications.NotificationClicked += TrayIconService_OnNotificationClicked;
            _trayIconService.ShellRestarted += OnNotificationShellRestored;
            WebLyricsGraphicsRecovery.Session.Degraded += OnWebLyricsDegraded;

            // evaluate the initial state once the window is loaded
            Loaded += MainWindow_Loaded;
            InitializeExperimentalHostWatcher();
        }



        #region Window Controller & others

        /// <summary>协调主窗口关闭和任务栏宿主解挂。/ Coordinates main-window shutdown and taskbar-host detachment.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel)
                return;

            _exitService.ArmExitProtection();
            PrepareForExit();
        }

        /// <summary>在 Dispatcher 仍运行时撤下宿主及浮层；最终关窗重复调用不会重做清理。</summary>
        internal void PrepareForExit()
        {
            if (_isClosing) return;

            _isClosing = true;
            _memoryMenu.IsOpen = false; _batteryMenu.IsOpen = false;
            DetachTaskbarHostsForExit();
            CloseDeveloperPreviews();
            _lyricsRecoveryNotifications.CancelPendingRequests();
            _taskbarTopologyTimer.Stop();
            _taskbarRecoveryCancellation?.Cancel();
            _taskbarRecoveryCancellation?.Dispose();
            _taskbarRecoveryCancellation = null;
            TaskbarEnvironmentRecovering = false;

            // ContextMenu Popup HWNDs are not Application.Windows entries. Close every
            // menu explicitly before the hidden host begins shutting down.
            TrayMenu.IsOpen = false;
            foreach (var taskbarWindow in _taskbarWindows)
                taskbarWindow.ClosePlayerMenu();

            CloseTaskbarWindows();
            _audioControlFlyout?.Dispose();
            CloseFullPanelWindows();
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

            _mediaSessionService.SnapshotChanged -= MediaSessionService_OnSnapshotChanged;
            _mediaSessionService.SessionsChanged -= MediaSessionService_OnSessionsChanged;
            SettingsManager.LayoutSettingsChanged -= SettingsManager_OnLayoutSettingsChanged;
            SettingsManager.AppearanceSettingsChanged -= SettingsManager_OnAppearanceSettingsChanged;
            SettingsManager.TaskbarAppearanceSettingsChanged -= SettingsManager_OnTaskbarAppearanceSettingsChanged;
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
            _audioControlViewModel.InputDeviceMenuRequested -= AudioControl_OnInputDeviceMenuRequested;
            _audioControlViewModel.CurrentAppVolumeMenuRequested -= AudioControl_OnCurrentAppVolumeMenuRequested;
            _audioControlViewModel.SettingsOpenRequested -= ViewModel_OpenSettingsRequested;
            _mouseInputMonitor.LeftButtonPressed -= MouseInputMonitor_OnLeftButtonPressed;
            TrayMenu.OpenSettingsRequested -= ViewModel_OpenSettingsRequested;
            TrayMenu.ExitRequested -= TrayMenu_ExitRequested;
            TrayMenu.OpenUpdateSettingsRequested -= ViewModel_OpenUpdateSettingsRequested;
            TrayMenu.ReloadTaskbarHostRequested -= TrayMenu_ReloadTaskbarHostRequested;
            TrayMenu.QuickLaunchRequested -= TrayMenu_QuickLaunchRequested;
            _taskbarViewModel.OpenSettingsRequested -= ViewModel_OpenSettingsRequested;
            _taskbarViewModel.OpenUpdateSettingsRequested -= ViewModel_OpenUpdateSettingsRequested;
            _updateService.UpdateStateChanged -= UpdateService_OnStateChanged;
            _systemNotifications.NotificationClicked -= TrayIconService_OnNotificationClicked;
            _trayIconService.ShellRestarted -= OnNotificationShellRestored;
            WebLyricsGraphicsRecovery.Session.Degraded -= OnWebLyricsDegraded;
            // Make sure that closing this window will begin the process of closing the application.
            _fullPanels.PanelClosed -= FullPanelHostClosed;
            _fullPanels.Dispose();
            _exitService.RequestExit();
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
            if (SettingsManager.Current.WindowMode == WindowMode.FloatingBar) { QueueRuntimeRefresh(force: true); return; }
            if (_isClosing || SettingsManager.Current.WindowMode != WindowMode.Taskbar)
                return;

            if (TaskbarEnvironmentRecovering)
                return;

            TaskbarEnvironmentRecovering = true;
            foreach (var taskbarWindow in _taskbarWindows)
                taskbarWindow.SuspendForEnvironmentRecovery();

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
            if (SettingsManager.Current.WindowMode == WindowMode.FloatingBar) { QueueRuntimeRefresh(force: true); return; }
            if (_isClosing || SettingsManager.Current.WindowMode != WindowMode.Taskbar)
                return;

            RequestTaskbarEnvironmentRecovery();
        }

        private async Task RecoverTaskbarEnvironmentAsync(CancellationTokenSource recovery)
        {
            var stableSamples = 0;
            string? previousSignature = null;
            var recreated = false;
            try
            {
                for (var attempt = 0; attempt < TaskbarRecoveryPolicy.MaximumAttempts; attempt++)
                {
                    await Task.Delay(TaskbarRecoveryPolicy.GetDelay(attempt), recovery.Token);
                    if (_isClosing || SettingsManager.Current.WindowMode != WindowMode.Taskbar)
                        return;

                    if (!TryGetStableTaskbarEnvironmentSignature(out var signature))
                    {
                        stableSamples = 0;
                        continue;
                    }

                    stableSamples = string.Equals(signature, previousSignature, StringComparison.Ordinal)
                        ? stableSamples + 1
                        : 1;
                    previousSignature = signature;
                    if (stableSamples < TaskbarRecoveryPolicy.RequiredStableSamples)
                        continue;

                    RecreateTaskbarWindows();
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
                    {
                        foreach (var taskbarWindow in _taskbarWindows)
                            taskbarWindow.ResumeAfterEnvironmentRecovery();
                    }
                    recovery.Dispose();
                }
            }
        }

        private bool TryGetStableTaskbarEnvironmentSignature(out string signature)
        {
            var entries = new List<string>();
            foreach (var target in ResolveTaskbarTargetDeviceIds())
            {
                var handle = _taskBarService.GetSelectedTaskbarHandle(target, out _);
                if (handle == IntPtr.Zero ||
                    !_taskBarService.TryGetTaskbarRect(handle, out var rect) ||
                    rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                {
                    continue;
                }

                var dpi = GetDpiForWindow(handle);
                if (dpi == 0)
                    continue;

                entries.Add($"{handle.ToInt64():X}:{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}:{dpi}");
            }

            signature = string.Join("|", entries.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));
            return entries.Count > 0;
        }

        #endregion

        /// <summary>
        /// Closes and re-creates the docked taskbar window (e.g. after Explorer restarted
        /// and destroyed the old taskbar together with our child window).
        /// </summary>
        public void RecreateTaskbarWindow() => RecreateTaskbarWindows();

        private void RecreateTaskbarWindows()
        { QueueRuntimeRefresh(force: true); }

        private void CloseTaskbarWindows()
        {
            SettingsManager.SettingsChanged -= RuntimeSettingsChanged;
            _memoryPruneCoordinator.LevelChanged -= RuntimePruneChanged;
            _runtimeRequest++;
            CloseFullPanelWindows();
            _taskbarWindows.Clear();
            _floatingWindows.Clear();
            _runtime.Dispose();
        }

        /// <summary>
        /// 在其他退出清理前挂起并解挂全部任务栏宿主；随后由 CloseTaskbarWindows 完成关窗释放。
        /// </summary>
        internal void DetachTaskbarHostsForExit()
        {
            foreach (var taskbarWindow in _taskbarWindows.ToArray())
            {
                try
                {
                    taskbarWindow.SuspendForEnvironmentRecovery();
                    taskbarWindow.DetachFromTaskbar();
                }
                catch (Exception exception)
                {
                    // 单个宿主解挂失败也必须继续：退出流程不能被一个异常打断。
                    // One host failing to detach must not stop the exit: a single exception cannot break the sequence.
                    _logService.Error("Exit", "任务栏宿主解挂失败", exception);
                }
            }
        }

        private void MediaSessionService_OnSnapshotChanged(object? sender, MediaSnapshot snapshot)
        {
            if (_isClosing)
                return;

            foreach (var taskbarWindow in _taskbarWindows)
                taskbarWindow.ApplySnapshot(snapshot);
            foreach (var floating in _floatingWindows) floating.ApplySnapshot(snapshot);
        }

        private void MediaSessionService_OnSessionsChanged(IReadOnlyList<MediaSessionOption> options)
        {
            if (_isClosing)
                return;

            foreach (var taskbarWindow in _taskbarWindows)
                taskbarWindow.ApplySessions(options);
            TrayMenu.ApplySessions(options);
        }

        /// <summary>
        /// 处理布局设置变更事件：应用新的窗口模式和布局方向。
        /// Handle layout settings changed event: apply new window mode and layout orientation.
        /// </summary>
        private void SettingsManager_OnLayoutSettingsChanged(object? sender, LayoutSettingsChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => SettingsManager_OnLayoutSettingsChanged(sender, e));
                return;
            }

            _latestLayoutSettings = e;
            if (_layoutSettingsUpdateScheduled)
                return;
            _layoutSettingsUpdateScheduled = true;
            Dispatcher.BeginInvoke(() =>
            {
                _layoutSettingsUpdateScheduled = false;
                if (_isClosing)
                    return;

                var latest = _latestLayoutSettings;
                _latestLayoutSettings = null;
                if (latest is null)
                    return;
                ActivateTaskbarMode();
                foreach (var taskbarWindow in _taskbarWindows)
                    taskbarWindow.ApplyLayoutSettings(WindowMode.Taskbar, latest.OrientationMode);
            }, DispatcherPriority.Render);
        }

        private void SettingsManager_OnAppearanceSettingsChanged(object? sender, AppearanceSettingsChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing)
                    return;

                foreach (var taskbarWindow in _taskbarWindows)
                    taskbarWindow.ApplyAppearanceSettings();
            });
        }

        private void SettingsManager_OnTaskbarAppearanceSettingsChanged(object? sender, AppearanceSettingsChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing)
                    return;

                foreach (var taskbarWindow in _taskbarWindows)
                    taskbarWindow.ApplyAppearanceSettings();
            });
        }

        private void SettingsManager_OnLyricsSettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing)
                    return;
                var snapshot = _mediaSessionService.CurrentSnapshot ?? MediaSnapshot.Disconnected;
                foreach (var taskbarWindow in _taskbarWindows)
                {
                    // 歌词呈现设置变更：帧遵循"可见内容一字未变就不重发"，与演示器守卫是同一条规则。
                    // Lyrics-presentation settings change: the frame follows "no visible change, no re-send",
                    // the same rule the presenter's own guard applies.
                    taskbarWindow.ApplySnapshot(snapshot, lyricsSettingsRefresh: true);
                }
            });
        }

        private void ActivateTaskbarMode()
        { QueueRuntimeRefresh(); }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // The media bar lives in the docked TaskbarWindow; keep this window as an invisible host.
            Visibility = Visibility.Collapsed;

            _displayMonitorService.Refresh();
            _effectiveTaskbarTargetSignature = ResolveEffectiveTaskbarTargetSignature();
            ActivateTaskbarMode();
            _taskbarTopologyTimer.Start();
            if (WebLyricsGraphicsRecovery.Session.HasDegraded) OnWebLyricsDegraded();
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
                foreach (var taskbarWindow in _taskbarWindows)
                    taskbarWindow.ApplyAppearanceSettings();
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
            if (SettingsManager.Current.WindowMode == WindowMode.FloatingBar) { QueueRuntimeRefresh(); return; }
            if (_isClosing || TaskbarEnvironmentRecovering)
                return;

            var nextSignature = ResolveEffectiveTaskbarTargetSignature();
            if (string.Equals(_effectiveTaskbarTargetSignature, nextSignature, StringComparison.OrdinalIgnoreCase))
                return;

            var hadPreviousTarget = !string.IsNullOrWhiteSpace(_effectiveTaskbarTargetSignature);
            _effectiveTaskbarTargetSignature = nextSignature;
            if (!hadPreviousTarget || SettingsManager.Current.WindowMode != WindowMode.Taskbar)
                return;

            CloseFullPanelWindows();
            RequestTaskbarEnvironmentRecovery();
        }

        private string ResolveEffectiveTaskbarTargetSignature()
        {
            var targets = ResolveTaskbarTargetDeviceIds();
            return string.Join("|", targets.Select(target =>
            {
                var handle = _taskBarService.GetSelectedTaskbarHandle(target, out _);
                return $"{target}:{handle.ToInt64():X}";
            }));
        }

        private IReadOnlyList<string> ResolveTaskbarTargetDeviceIds() =>
            TaskbarTargetPolicy.ResolveDeviceIds(
                _displayMonitorService.GetMonitors(),
                SettingsManager.Current.TaskbarTargetMonitorDeviceIds,
                SettingsManager.Current.TaskbarTargetMonitorDeviceId);

        private async void AudioControl_OnFlyoutToggleRequested(TrayIconBounds? bounds)
        {
            if (_isClosing)
                return;

            try
            {
                await ToggleAudioControlFlyoutAsync(bounds);
            }
            catch (Exception exception)
            {
                _logService.Error("Audio", "切换托盘音频面板失败", exception);
            }
        }

        /// <summary>首次打开才加载音频浮窗界面；离开原生回调后再创建，隐藏或退出使待打开请求失效。</summary>
        internal async Task ToggleAudioControlFlyoutAsync(TrayIconBounds? bounds)
        {
            if (_isClosing)
                return;

            if (_audioControlFlyout is null)
            {
                var version = _audioControlFlyoutRequestVersion;
                await Dispatcher.Yield(DispatcherPriority.Background);
                if (_isClosing || version != _audioControlFlyoutRequestVersion)
                    return;
                _audioControlFlyout ??= _audioControlFlyoutFactory();
                if (_isClosing)
                {
                    _audioControlFlyout.Dispose();
                    return;
                }
                if (version != _audioControlFlyoutRequestVersion)
                    return;
            }

            await _audioControlFlyout.ToggleAsync(bounds);
        }

        /// <summary>隐藏已有浮窗并取消尚未开始的首次创建，不为关闭操作加载界面。</summary>
        internal void HideAudioControlFlyout()
        {
            _audioControlFlyoutRequestVersion++;
            _audioControlFlyout?.Hide();
        }

        /// <summary>
        /// 托盘点击要求打开输出设备菜单：交给任务栏宿主的紧凑菜单实现，锚点用托盘图标的位置，
        /// 因此菜单出现在用户刚刚点击的地方。
        /// The tray click asks for the output-device menu: the taskbar host owns that compact menu, and the anchor is the tray
        /// icon's position, so the menu appears where the user just clicked.
        /// </summary>
        private async void AudioControl_OnInputDeviceMenuRequested(TrayIconBounds? bounds) =>
            await ShowTrayCompactMenuAsync(TaskbarCompactFlyoutMode.InputDevice, bounds);

        private async void AudioControl_OnOutputDeviceMenuRequested(TrayIconBounds? bounds) =>
            await ShowTrayCompactMenuAsync(TaskbarCompactFlyoutMode.OutputDevice, bounds);

        /// <inheritdoc cref="AudioControl_OnOutputDeviceMenuRequested" />
        private async void AudioControl_OnCurrentAppVolumeMenuRequested(TrayIconBounds? bounds) =>
            await ShowTrayCompactMenuAsync(TaskbarCompactFlyoutMode.Volume, bounds);

        private async Task ShowTrayCompactMenuAsync(TaskbarCompactFlyoutMode mode, TrayIconBounds? bounds)
        {
            if (_isClosing) return;
            if (_floatingWindows.FirstOrDefault() is { } floating)
            {
                await floating.ShowCompactMenuAsync(mode, bounds);
                return;
            }
            var primaryTaskbarWindow = _taskbarWindows.FirstOrDefault();
            if (primaryTaskbarWindow is null)
                return;

            await primaryTaskbarWindow.ShowCompactMenuAsync(mode, bounds);
        }

        private void SettingsManager_OnTaskbarExperienceSettingsChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => SettingsManager_OnTaskbarExperienceSettingsChanged(sender, e));
                return;
            }

            if (_experienceSettingsUpdateScheduled)
                return;
            _experienceSettingsUpdateScheduled = true;
            Dispatcher.BeginInvoke(() =>
            {
                _experienceSettingsUpdateScheduled = false;
                if (_isClosing)
                    return;
                foreach (var taskbarWindow in _taskbarWindows)
                    taskbarWindow.ApplyExperienceSettings();
            }, DispatcherPriority.Render);
        }

        private void CreateTaskbarWindows()
        { QueueRuntimeRefresh(); }

        private TaskbarWindow CreateTaskbarWindow(string targetDeviceId)
        {
            var window = _taskbarFactory(targetDeviceId, this);
            window.OpenFullPanelRequested += TaskbarWindow_OpenFullPanelRequested;
            window.InformationAutomaticallyHidden += TaskbarWindow_InformationAutomaticallyHidden;
            window.PlayerMenuOpenRequested += TaskbarWindow_PlayerMenuOpenRequested;
            window.PlacementTransitionStarted += TaskbarWindow_PlacementTransitionStarted;
            window.PlacementFallbackRequested += TaskbarWindow_PlacementFallbackRequested;
            return window;
        }

        private void TaskbarWindow_PlacementTransitionStarted(object? sender, EventArgs e)
        {
            if (sender is TaskbarWindow window && _taskbarWindows.Contains(window) && _fullPanels.TryGet(window.TargetMonitorDeviceId, out var panel))
                panel.RequestClose();
        }

        private void TaskbarWindow_PlayerMenuOpenRequested(TaskbarWindow host, Point? screenAnchor)
        {
            if (_isClosing || TaskbarEnvironmentRecovering || !_taskbarWindows.Contains(host)) return;
            ContextMenuHelper.OpenAtScreenAnchor(host.IsPerformanceMenuRequested ? _memoryMenu : host.IsBatteryMenuRequested ? _batteryMenu : host.PlayerContextMenu, this, screenAnchor);
        }

        private void TaskbarWindow_OpenFullPanelRequested(object? sender, FullPanelOpenRequestEventArgs e)
        {
            if (_isClosing || sender is not TaskbarWindow host || !_taskbarWindows.Contains(host)) return;
            var panel = _fullPanels.Open(host.TargetMonitorDeviceId, e.Anchor, e.VerticalAnchor, e.EnsureOpen, null, e.PlacementSide);
            if (panel is not null) host.SetFullPanelOpen(panel.IsVisible);
        }

        private void FullPanelHostClosed(string target)
        {
            foreach (var host in _taskbarWindows.Where(host => host.TargetMonitorDeviceId == target))
                host.SetFullPanelOpen(false);
            foreach (var host in _floatingWindows.Where(host => host.TargetMonitorDeviceId == target))
                host.SetFullPanelOpen(false);
        }

        private void CloseFullPanelWindows() => _fullPanels.CloseAll();

        private void ViewModel_OpenSettingsRequested(object? sender, EventArgs e)
        {
            if (_isClosing)
                return;

            _settingsWindow ??= _settingsWindowFactory();
            _settingsWindow.Closed -= SettingsWindow_Closed;
            _settingsWindow.Closed += SettingsWindow_Closed;
            _settingsWindow.Show();
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
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
            _systemNotifications.TryShowNotification(
                Translations.Get("Update.Notification.Title"),
                Translations.Format("Update.Notification.Body", version, state.CurrentVersion),
                ShellNotificationTarget.Application, "Update.Available");
        }

        private void OnNotificationShellRestored(object? sender, EventArgs e)
        {
            if (!_isClosing) _lyricsRecoveryNotifications.OnShellRestored();
        }

        private void OnWebLyricsDegraded()
        {
            if (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (!_isClosing && !Dispatcher.HasShutdownStarted) _lyricsRecoveryNotifications.ReportDegraded();
            });
        }

        private void TrayIconService_OnNotificationClicked(ShellNotificationTarget target)
        {
            if (_isClosing) return;
            if (target == ShellNotificationTarget.Application)
            {
                ViewModel_OpenUpdateSettingsRequested(this, EventArgs.Empty);
                return;
            }
            if (target == ShellNotificationTarget.FloatingBarExperiment)
            {
                ViewModel_OpenSettingsRequested(this, EventArgs.Empty);
                _settingsWindow?.NavigateToGroup(SettingsPageKey.ScreenAndPlacement, "Common.Group.ChooseDisplayMode");
                return;
            }
            if (target == ShellNotificationTarget.VerticalTaskbarExperiment)
            {
                ViewModel_OpenSettingsRequested(this, EventArgs.Empty);
                _settingsWindow?.NavigateToGroup(SettingsPageKey.ScreenAndPlacement, "Common.Group.ScreenAndPlacement");
                return;
            }
            if (target != ShellNotificationTarget.TaskbarBackground) return;

            ViewModel_OpenSettingsRequested(this, EventArgs.Empty);
            if (_settingsWindow is not { } settingsWindow) return;
            settingsWindow.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() =>
                {
                    if (_isClosing || !ReferenceEquals(_settingsWindow, settingsWindow)) return;
                    settingsWindow.NavigateToGroup(SettingsPageKey.Appearance, "Appearance.Group.TaskbarBackground");
                }));
        }

        private void TaskbarWindow_PlacementFallbackRequested(object? sender, TaskbarPlacementFallbackEventArgs e)
        {
            if (_isClosing || sender is not TaskbarWindow window || !_taskbarWindows.Contains(window))
                return;
            if (!e.HasContent)
            {
                _placementNotificationGate.ForgetHost(window.TargetMonitorDeviceId);
                return;
            }
            var pressure = _placementNotificationGate.Observe(window.TargetMonitorDeviceId, e.Pressure,
                e.HasAmpleSpace, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, e.Generation, e.PublicationId, e.LastFailurePublicationId);
            if (pressure is null)
                return;
            _systemNotifications.TryShowNotification(
                Translations.Get("Taskbar.Placement.Notification.Title"),
                Translations.Get(pressure == TaskbarSpacePressure.Hidden ? "Taskbar.Placement.Notification.Hidden" : "Taskbar.Placement.Notification.Constrained"),
                ShellNotificationTarget.None, pressure == TaskbarSpacePressure.Hidden ? "Taskbar.Hidden" : "Taskbar.Constrained");
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

            HideAudioControlFlyout();
            TrayMenu.ApplySessions(_mediaSessionService.CurrentSessionOptions);
            TrayMenu.ApplyQuickLaunchEntries(SettingsManager.Current.QuickLaunch.Entries ?? []);
            TrayMenu.IsReloadTaskbarHostEnabled =
                SettingsManager.Current.WindowMode == WindowMode.Taskbar;
            TrayMenu.PlacementTarget = this;
            TrayMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            TrayMenu.IsOpen = true;
        }

        private void TrayMenu_ExitRequested(object? sender, EventArgs e) => _exitService.RequestExit();

        private void TrayMenu_ReloadTaskbarHostRequested(object? sender, EventArgs e) =>
            RequestTaskbarHostReload();

        private async void TrayMenu_QuickLaunchRequested(QuickLaunchEntry entry)
        {
            if (_isClosing) return;
            if (!(SettingsManager.Current.QuickLaunch.Entries ?? []).Any(current => current.Id == entry.Id &&
                current.Kind == entry.Kind && current.Target == entry.Target)) return;
            _quickLaunch.Select(entry.Id);
            QuickLaunchResult result;
            try { result = await _sourceActivationService.LaunchAsync(entry); }
            catch (Exception exception)
            {
                _logService.Error("QuickLaunch", "启动应用失败", exception);
                result = QuickLaunchResult.Failed;
            }
            if (_isClosing || _quickLaunch.Selected?.Id != entry.Id || result == QuickLaunchResult.Success)
                return;

            _systemNotifications.TryShowNotification(
                Translations.Get("Panel.QuickLaunch.Title"),
                Translations.Get(result == QuickLaunchResult.InvalidTarget
                    ? "Shell.QuickLaunch.Status.InvalidTarget"
                    : "Shell.QuickLaunch.Status.Failed"), ShellNotificationTarget.None, "QuickLaunch.Failed");
        }

        private void MouseInputMonitor_OnLeftButtonPressed(object? sender, NativeMouseButtonEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing)
                    return;

                ContextMenuHelper.CloseIfOutside(TrayMenu, e.ScreenX, e.ScreenY);
                foreach (var taskbarWindow in _taskbarWindows)
                    taskbarWindow.CloseContextMenuIfOutside(e.ScreenX, e.ScreenY);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

    }
}
