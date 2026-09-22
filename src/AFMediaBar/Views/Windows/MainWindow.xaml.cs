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

        private readonly ITaskbarDockService _taskBarService;
        private readonly MediaSessionService _mediaSessionService;
        private readonly AudioControlViewModel _audioControlViewModel;
        private readonly AudioControlFlyoutWindow _audioControlFlyout;
        private readonly NativeMouseInputMonitor _mouseInputMonitor;
        private readonly WindowAppearanceService _appearanceService;
        private readonly Func<TaskbarCompactFlyoutWindow> _compactFlyoutFactory;
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
        private readonly DispatcherTimer _taskbarTopologyTimer;

        /// <summary>后台剪枝协调器：任务栏宿主订阅它的档位变化，因此这里只做转交，不在宿主里查询电源状态。
        /// The background prune coordinator: the taskbar host subscribes to its level changes, so this field only hands it over and the host never
        /// queries the power state itself.</summary>
        private readonly MemoryPruneCoordinator _memoryPruneCoordinator;
        private readonly List<TaskbarWindow> _taskbarWindows = [];
        private CapsuleIslandWindow? _capsuleIslandWindow;
        private SettingsWindow? _settingsWindow;
        private readonly Dictionary<string, TaskbarFullPanelWindow> _fullPanelWindows = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _fullPanelClosedAtUtc = new(StringComparer.OrdinalIgnoreCase);
        private TrackChangeNotificationWindow? _trackChangeNotificationWindow;
        private string? _effectiveTaskbarTargetSignature;
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
            ITaskbarDockService taskBarService,
            MediaSessionService mediaSessionService,
            AudioControlViewModel audioControlViewModel,
            AudioControlFlyoutWindow audioControlFlyout,
            NativeMouseInputMonitor mouseInputMonitor,
            WindowAppearanceService appearanceService,
            Func<TaskbarCompactFlyoutWindow> compactFlyoutFactory,
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
            DataContext = this;

            _taskBarService = taskBarService;
            _mediaSessionService = mediaSessionService;
            _audioControlViewModel = audioControlViewModel;
            _audioControlFlyout = audioControlFlyout;
            _mouseInputMonitor = mouseInputMonitor;
            _appearanceService = appearanceService;
            _compactFlyoutFactory = compactFlyoutFactory;
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
            };

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
            ApplicationThemeManager.Changed += ApplicationThemeManager_OnChanged;
            SettingsManager.LyricsSettingsChanged += SettingsManager_OnLyricsSettingsChanged;
            SettingsManager.TaskbarExperienceSettingsChanged += SettingsManager_OnTaskbarExperienceSettingsChanged;
            SettingsManager.InteractionSettingsChanged += SettingsManager_OnTaskbarExperienceSettingsChanged;
            // 频谱组件（柱数/内容区高度/灵敏度/刷新率）也属于"扩展功能"设置：任务栏侧一直订阅着它（TaskbarWindow 的
            // ApplyExtraFeaturesSettings），灵动岛侧过去没有订阅——岛上有了波形之后就成了一条真实的缺口：改了设置岛上的波形
            // 纹丝不动。这里按任务栏同一模式补上订阅。
            // The spectrum component (band count, content height, sensitivity, refresh rate) is part of the "extra features" settings, which the
            // taskbar has always subscribed to (TaskbarWindow's ApplyExtraFeaturesSettings) while the island never did — a real gap once the island
            // has a waveform: editing the settings left the island's waveform unchanged. The subscription is added here, following the very pattern
            // the taskbar uses.
            SettingsManager.ExtraFeaturesSettingsChanged += SettingsManager_OnExtraFeaturesSettingsChanged;
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
            ViewModel.OpenSettingsRequested += ViewModel_OpenSettingsRequested;
            ViewModel.OpenUpdateSettingsRequested += ViewModel_OpenUpdateSettingsRequested;

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
            _capsuleIslandWindow?.CloseIslandMenu();

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

            CloseTaskbarWindows();
            var capsuleIslandWindow = _capsuleIslandWindow;
            _capsuleIslandWindow = null;
            capsuleIslandWindow?.Close();
            _audioControlFlyout.Close();
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
            ApplicationThemeManager.Changed -= ApplicationThemeManager_OnChanged;
            SettingsManager.LyricsSettingsChanged -= SettingsManager_OnLyricsSettingsChanged;
            SettingsManager.TaskbarExperienceSettingsChanged -= SettingsManager_OnTaskbarExperienceSettingsChanged;
            SettingsManager.InteractionSettingsChanged -= SettingsManager_OnTaskbarExperienceSettingsChanged;
            SettingsManager.ExtraFeaturesSettingsChanged -= SettingsManager_OnExtraFeaturesSettingsChanged;
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
            ViewModel.OpenSettingsRequested -= ViewModel_OpenSettingsRequested;
            ViewModel.OpenUpdateSettingsRequested -= ViewModel_OpenUpdateSettingsRequested;
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
            if (_isClosing || SettingsManager.Current.WindowMode != WindowMode.Taskbar)
                return;

            TrayMenu.IsOpen = false;
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
        {
            if (SettingsManager.Current.WindowMode != WindowMode.Taskbar)
            {
                ActivateWindowMode(SettingsManager.Current.WindowMode);
                return;
            }

            CloseTaskbarWindows();
            CreateTaskbarWindows();
            _effectiveTaskbarTargetSignature = ResolveEffectiveTaskbarTargetSignature();

            // Replay the latest snapshot; if none exists yet, force a synchronous refresh.
            if (_mediaSessionService.CurrentSnapshot is { } snapshot)
            {
                foreach (var taskbarWindow in _taskbarWindows)
                    taskbarWindow.ApplySnapshot(snapshot);
            }
            else
            {
                _mediaSessionService.RefreshNow();
            }
        }

        private void CloseTaskbarWindows()
        {
            // 完整层的坐标与任务栏宿主一一对应；重建宿主时先收起旧面板，避免它继续挂在已失效的屏幕/DPI 上。
            // Full-panel coordinates belong to their taskbar hosts; dismiss old panels before rebuilding hosts so none remains on stale monitor/DPI state.
            CloseFullPanelWindows();
            // 先清除共享引用，避免重入的媒体回调访问已被 Explorer 或 Close() 销毁 HWND 的窗口。
            // Clear the published reference first so reentrant media callbacks cannot target
            // a Window whose HWND has already been destroyed by Explorer or Close().
            var taskbarWindows = _taskbarWindows.ToArray();
            _taskbarWindows.Clear();
            if (taskbarWindows.Length == 0)
                return;

            foreach (var taskbarWindow in taskbarWindows)
            {
                taskbarWindow.OpenFullPanelRequested -= TaskbarWindow_OpenFullPanelRequested;
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
        }

        private void MediaSessionService_OnSnapshotChanged(object? sender, MediaSnapshot snapshot)
        {
            if (_isClosing)
                return;

            foreach (var taskbarWindow in _taskbarWindows)
                taskbarWindow.ApplySnapshot(snapshot);
            _capsuleIslandWindow?.ApplySnapshot(snapshot);
        }

        private void MediaSessionService_OnSessionsChanged(IReadOnlyList<MediaSessionOption> options)
        {
            if (_isClosing)
                return;

            foreach (var taskbarWindow in _taskbarWindows)
                taskbarWindow.ApplySessions(options);
            // 岛的右键菜单有自己的"切换媒体源"子菜单，因此快照与会话列表必须同样分发给它。
            // The island's context menu owns its own "switch media source" submenu, so the session list is handed to it too.
            _capsuleIslandWindow?.ApplySessions(options);
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
                foreach (var taskbarWindow in _taskbarWindows)
                    taskbarWindow.ApplyLayoutSettings(e.WindowMode, e.OrientationMode);
                _capsuleIslandWindow?.ApplyAppearanceSettings();
            });
        }

        private void SettingsManager_OnAppearanceSettingsChanged(object? sender, AppearanceSettingsChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing)
                    return;

                UpdateSystemThemeWatcher(e.Appearance);
                foreach (var taskbarWindow in _taskbarWindows)
                    taskbarWindow.ApplyAppearanceSettings();
                _capsuleIslandWindow?.ApplyAppearanceSettings();
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
                    taskbarWindow.ApplySnapshot(snapshot);
                _capsuleIslandWindow?.ApplySnapshot(snapshot);
            });
        }

        /// <summary>
        /// 扩展功能设置（频谱组件、性能组件、来源过滤、快捷启动）变更：频谱组件一变就要求灵动岛按新设置重新落一次波形几何并重画。
        /// 任务栏侧走它自己的 <c>ApplyExtraFeaturesSettings</c>，这里只补灵动岛这一半（见订阅处的注释）。
        /// An extra-features settings change (spectrum component, performance component, source filter, quick launch): a spectrum change asks the
        /// island to land its waveform geometry again from the new settings and repaint. The taskbar takes its own
        /// <c>ApplyExtraFeaturesSettings</c> path; this only fills in the island's half (see the note at the subscription).
        /// </summary>
        private void SettingsManager_OnExtraFeaturesSettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing)
                    return;

                _capsuleIslandWindow?.ApplySpectrumSettings();
            });
        }

        private void UpdateSystemThemeWatcher(AppearanceSettings appearance)
        {            if (_isClosing)
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
                CloseTaskbarWindows();
                if (_capsuleIslandWindow is null)
                {
                    var island = App.Services.GetRequiredService<CapsuleIslandWindow>();
                    island.OpenFullPanelRequested += CapsuleIslandWindow_OpenFullPanelRequested;
                    _capsuleIslandWindow = island;
                }

                _capsuleIslandWindow.ApplyAppearanceSettings();
                _capsuleIslandWindow.Show();
                _capsuleIslandWindow.ApplySessions(_mediaSessionService.CurrentSessionOptions);
                if (_mediaSessionService.CurrentSnapshot is { } islandSnapshot)
                    _capsuleIslandWindow.ApplySnapshot(islandSnapshot);
                return;
            }

            if (_capsuleIslandWindow is { } islandWindow)
            {
                islandWindow.OpenFullPanelRequested -= CapsuleIslandWindow_OpenFullPanelRequested;
                _capsuleIslandWindow = null;
                islandWindow.Close();
            }

            // 完整层可能正挂在岛的锚点上：岛消失后它必须跟着消失，否则会停在一块已经不存在的位置上。
            // The full panel may be anchored to the island: once the island goes away it has to go too, or it would sit on
            // top of an anchor that no longer exists.
            CloseFullPanelWindows();
            if (_taskbarWindows.Count == 0)
            {
                CreateTaskbarWindows();
                if (_mediaSessionService.CurrentSnapshot is { } snapshot)
                {
                    foreach (var taskbarWindow in _taskbarWindows)
                        taskbarWindow.ApplySnapshot(snapshot);
                }
                else
                    _mediaSessionService.RefreshNow();
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // The media bar lives in the docked TaskbarWindow; keep this window as an invisible host.
            Visibility = Visibility.Collapsed;

            _displayMonitorService.Refresh();
            _effectiveTaskbarTargetSignature = ResolveEffectiveTaskbarTargetSignature();
            ActivateWindowMode(SettingsManager.Current.WindowMode);
            _taskbarTopologyTimer.Start();
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
                _capsuleIslandWindow?.ApplyAppearanceSettings();
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

        /// <summary>
        /// 托盘点击要求打开输出设备菜单 / 当前应用音量菜单：按当前存在的媒体栏宿主路由，
        /// 锚点用托盘图标的位置，因此菜单出现在用户刚刚点击的地方。
        /// The tray click asks for the output-device / current-application-volume menu: the request is routed to whichever
        /// media-bar host currently exists, and the anchor is the tray icon's position, so the menu appears where the user
        /// just clicked.
        ///
        /// 这里曾经在任务栏媒体栏列表为空时直接返回，于是灵动岛模式下托盘左键的这两项完全没有反应（静默失效）。
        /// 现在宿主选择交给纯策略：任务栏优先、其次是岛、都没有时退回托盘音频浮窗——每一次点击都有真实落点。
        /// This used to return as soon as the taskbar media-bar list was empty, which made both tray left-click actions do
        /// nothing at all in dynamic-island mode (a silent failure). The host is now chosen by a pure policy: taskbar
        /// first, then the island, and the tray audio flyout when neither exists — every click has a real destination.
        /// </summary>
        private async Task ShowTrayCompactMenuAsync(TaskbarCompactFlyoutMode mode, TrayIconBounds? bounds)
        {
            if (_isClosing)
                return;

            switch (TrayAudioMenuRoutePolicy.Resolve(_taskbarWindows.Count > 0, _capsuleIslandWindow is not null))
            {
                case TrayAudioMenuRoute.TaskbarHost:
                    if (_taskbarWindows.FirstOrDefault() is { } taskbarWindow)
                        await taskbarWindow.ShowCompactMenuAsync(mode, bounds);
                    return;

                case TrayAudioMenuRoute.IslandHost:
                    if (_capsuleIslandWindow is { } island)
                        await island.ShowCompactMenuAsync(mode, bounds);
                    return;

                default:
                    await _audioControlFlyout.ToggleAsync(bounds);
                    return;
            }
        }

        private void SettingsManager_OnTaskbarExperienceSettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing) return;
                foreach (var taskbarWindow in _taskbarWindows)
                    taskbarWindow.ApplyExperienceSettings();
            });
        }

        private void CreateTaskbarWindows()
        {
            var seenTaskbars = new HashSet<IntPtr>();
            foreach (var targetDeviceId in ResolveTaskbarTargetDeviceIds())
            {
                var handle = _taskBarService.GetSelectedTaskbarHandle(targetDeviceId, out _);
                if (handle == IntPtr.Zero || !seenTaskbars.Add(handle))
                    continue;

                var window = CreateTaskbarWindow(targetDeviceId);
                _taskbarWindows.Add(window);
                window.ApplyAppearanceSettings();
            }
        }

        private TaskbarWindow CreateTaskbarWindow(string targetDeviceId)
        {
            var window = new TaskbarWindow(
                _taskBarService,
                targetDeviceId,
                ViewModel,
                this,
                _appearanceService,
                _compactFlyoutFactory,
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

            ToggleFullPanelNear(taskbarWindow.GetMediaBarScreenBounds(), taskbarWindow.TargetMonitorDeviceId);
        }

        /// <summary>
        /// 灵动岛的右键菜单请求打开完整层：与任务栏走同一条路径，只有锚点与目标显示器来自岛，
        /// 因此完整层不需要为岛再写一份（定位、失焦关闭、分区分组、音频与性能区都自动具备）。
        /// The island's context menu asks for the full panel: it takes the same path as the taskbar and only the anchor and
        /// target display come from the island, so no second full panel is needed (placement, focus-loss dismissal,
        /// sections, audio and performance blocks are all inherited).
        /// </summary>
        private void CapsuleIslandWindow_OpenFullPanelRequested(object? sender, EventArgs e)
        {
            if (_isClosing || _capsuleIslandWindow is not { } island)
                return;

            ToggleFullPanelNear(island.GetMediaBarScreenBounds(), island.TargetMonitorDeviceId);
        }

        /// <summary>
        /// 在给定锚点处打开（或收起）完整层，并按目标显示器缓存实例。
        /// Opens — or dismisses — the full panel at the given anchor, caching the instance per target display.
        ///
        /// 350ms 的冷却窗口防止"失焦关闭后同一次点击又把它打开"：失焦关闭与再次点击几乎是同一瞬间发生的。
        /// The 350 ms cooldown keeps a focus-loss dismissal from being reopened by the very same click, which happens
        /// within a moment of each other.
        /// </summary>
        private void ToggleFullPanelNear(Rect anchor, string targetDeviceId)
        {
            if (!_fullPanelWindows.TryGetValue(targetDeviceId, out var panel) &&
                _fullPanelClosedAtUtc.TryGetValue(targetDeviceId, out var closedAtUtc) &&
                DateTime.UtcNow - closedAtUtc < TimeSpan.FromMilliseconds(350))
            {
                return;
            }

            if (panel is null)
            {
                panel = _fullPanelFactory();
                _fullPanelWindows[targetDeviceId] = panel;
            }

            panel.Closed -= FullPanelWindow_Closed;
            panel.Closed += FullPanelWindow_Closed;
            panel.ToggleNear(anchor, targetDeviceId);
        }

        private void FullPanelWindow_Closed(object? sender, EventArgs e)
        {
            if (sender is TaskbarFullPanelWindow window)
            {
                window.Closed -= FullPanelWindow_Closed;
                var target = _fullPanelWindows.FirstOrDefault(entry => ReferenceEquals(entry.Value, window)).Key;
                if (!string.IsNullOrWhiteSpace(target))
                {
                    _fullPanelWindows.Remove(target);
                    _fullPanelClosedAtUtc[target] = DateTime.UtcNow;
                }

                // 完整层是歌词与封面的另一个消费者，关闭后做一遍温和回收。
                // The full panel is another consumer of lyrics and artwork, so a gentle reclaim follows its close.
                _memoryPruneCoordinator.RequestTrim(MemoryTrimTrigger.PanelClosed);
            }
        }

        private void CloseFullPanelWindows()
        {
            foreach (var window in _fullPanelWindows.Values.Distinct().ToArray())
                window.RequestClose();
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
                foreach (var taskbarWindow in _taskbarWindows)
                    taskbarWindow.CloseContextMenuIfOutside(e.ScreenX, e.ScreenY);
                // 岛的右键菜单同样是 Popup 窗口，不在 Application.Windows 里，其"点菜单外关闭"必须由这里驱动。
                // The island's context menu is a Popup window too and is not an Application.Windows entry, so its
                // outside-click dismissal is driven from here as well.
                _capsuleIslandWindow?.CloseIslandMenuIfOutside(e.ScreenX, e.ScreenY);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

    }
}
