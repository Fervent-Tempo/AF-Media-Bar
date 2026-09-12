// The media bar docked into the Explorer taskbar, ported from FluentFlyout's TaskbarWindow
// (https://github.com/ManualDinosaur/FluentFlyout, GPL-3.0-or-later).

using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Windows;
using AFMediaBar.Components;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 透明子窗口覆盖整个任务栏，媒体栏通过 Canvas 放置并用 SetWindowRgn 裁剪。
/// A transparent child window spans the whole taskbar; the media bar is placed on a canvas
/// and clipped with SetWindowRgn so the rest of the taskbar remains visible and click-through.
/// </summary>
public partial class TaskbarWindow : Window
{
    // physical px offsets from the taskbar edges
    private const int EdgePadding = 20;
    private static readonly TimeSpan EnvironmentChangeProbeCooldown = TimeSpan.FromSeconds(2);

    private readonly ITaskbarDockService _taskBarService;
    private readonly TaskbarOccupiedAreaService _occupiedAreaService;
    private readonly MainWindowViewModel _viewModel;
    private readonly ITaskbarWindowHostActions _hostActions;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _sizeAnimationTimer;
    private readonly DispatcherTimer _spectrumTimer;
    private readonly GlobalInteractionRouter _interactionRouter;
    private readonly AudioInteractionService _audioInteractionService;

    private IntPtr _lastTaskbarHandle;
    private IntPtr _windowHandle;
    private bool _positionUpdateInProgress;
    private bool _isClosing;
    private bool _isEnvironmentSuspended;
    private bool _isDetachedFromTaskbar;
    private bool _setupComplete;
    private bool _environmentLayoutQueued;
    private bool _isDragging;
    private bool _isDragPending;
    private DateTime _suppressContextMenuUntilUtc;
    private DateTime _skipOccupiedAreaProbeUntilUtc;
    private WindowMode? _appliedWindowMode;
    private LayoutOrientation? _appliedOrientation;
    private double _appliedLengthScalePercent = double.NaN;
    private double _appliedThicknessScalePercent = double.NaN;
    private int _dragStartCursorPrimary;
    private int _dragStartBarPrimary;
    private int _dragPrimaryLimit;
    private double _sizeAnimationStart;
    private double _sizeAnimationTarget;
    private double _sizeAnimationProgress;
    private MediaBarSizeRequest? _pendingSizeRequest;
    private MediaBarSizeRequest? _lastDesiredSizeRequest;
    private readonly AudioMonitorService _audioMonitorService;
    private readonly float[] _spectrumBands = new float[AudioMonitorService.BandCount];
    private MediaSnapshot _lastSnapshot = MediaSnapshot.Disconnected;

    public event EventHandler? OpenFullPanelRequested;
    public event EventHandler? AudioControlRequested;

    /// <summary>
    /// 创建任务栏媒体宿主并连接其基础设施动作。
    /// Creates the taskbar media host and connects its infrastructure actions.
    /// </summary>
    public TaskbarWindow(
        ITaskbarDockService taskBarService,
        MainWindowViewModel viewModel,
        ITaskbarWindowHostActions hostActions,
        WindowAppearanceService appearanceService,
        TaskbarOccupiedAreaService occupiedAreaService,
        GlobalInteractionRouter interactionRouter,
        AudioInteractionService audioInteractionService,
        AudioMonitorService audioMonitorService)
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();

        DataContext = viewModel;
        ContextMenuHelper.AttachOutsideClickDismissal(PlayerMenu);
        appearanceService.Attach(PlayerMenu, this);
        MediaControl.TogglePlayPauseRequested += MediaControl_TogglePlayPauseRequested;
        MediaControl.SkipPreviousRequested += MediaControl_SkipPreviousRequested;
        MediaControl.SkipNextRequested += MediaControl_SkipNextRequested;
        MediaControl.ActivateSourceRequested += MediaControl_ActivateSourceRequested;
        MediaControl.OpenFullPanelRequested += MediaControl_OpenFullPanelRequested;
        MediaControl.AudioControlRequested += MediaControl_AudioControlRequested;
        MediaControl.OutputDeviceCycleRequested += MediaControl_OutputDeviceCycleRequested;
        MediaControl.SeekRequested += MediaControl_SeekRequested;
        MediaControl.WheelRequested += MediaControl_WheelRequested;
        MediaControl.DesiredSizeChanged += MediaControl_DesiredSizeChanged;

        _taskBarService = taskBarService;
        _occupiedAreaService = occupiedAreaService;
        _viewModel = viewModel;
        _hostActions = hostActions;
        _interactionRouter = interactionRouter;
        _audioInteractionService = audioInteractionService;
        _audioMonitorService = audioMonitorService;

        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1500); // slow auto-update for display changes
        _timer.Tick += PositionTimer_Tick;
        _timer.Start();

        _sizeAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _sizeAnimationTimer.Tick += (_, _) => AdvanceSizeAnimation();
        _spectrumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _spectrumTimer.Tick += (_, _) =>
        {
            if (!_isClosing && _appliedOrientation == LayoutOrientation.Horizontal &&
                _audioMonitorService.GetSpectrum(_spectrumBands))
                MediaControl.ApplySpectrum(_spectrumBands);
        };
        _spectrumTimer.Start();

        Loaded += Window_Loaded;

        Show();
    }

    /// <summary>初始化任务栏宿主窗口句柄和消息钩子。/ Initializes the taskbar-host handle and message hook.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource source = (HwndSource)PresentationSource.FromDependencyObject(this);
        _windowHandle = source.Handle;
        source.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCDESTROY)
        {
            // Explorer 会在 TaskbarCreated 到达 MainWindow 前销毁任务栏子 HWND；立即阻止媒体回调。
            // Explorer destroys taskbar child HWNDs before TaskbarCreated reaches MainWindow;
            // block media callbacks immediately so they cannot show an already closed Window.
            _isClosing = true;
            _windowHandle = IntPtr.Zero;
            _timer.Stop();
            _sizeAnimationTimer.Stop();
            _spectrumTimer.Stop();
            return IntPtr.Zero;
        }

        if (_setupComplete && TaskbarHostMessagePolicy.IsEnvironmentChange(msg))
        {
            // Explorer 重排任务栏期间只使用保守区间，避免同步 UI Automation 探测与 Shell 互相等待。
            // Use the conservative range while Explorer rearranges the taskbar so synchronous
            // UI Automation probing cannot deadlock with the Shell during repeated display changes.
            _skipOccupiedAreaProbeUntilUtc = DateTime.UtcNow + EnvironmentChangeProbeCooldown;
            _occupiedAreaService.InvalidateCache();
            _sizeAnimationTimer.Stop();
            if (_lastDesiredSizeRequest is { } desiredSize)
                _pendingSizeRequest = desiredSize;
            if (!_environmentLayoutQueued)
            {
                _environmentLayoutQueued = true;
                Dispatcher.BeginInvoke(() =>
                {
                    _environmentLayoutQueued = false;
                    if (_isClosing || _isEnvironmentSuspended)
                        return;

                    InvalidateMeasure();
                    InvalidateArrange();
                    InvalidateVisual();
                    UpdateLayout();
                    UpdatePosition();
                }, DispatcherPriority.ContextIdle);
            }
        }

        // Some interface mods (e.g. Nilesoft Shell, Windhawk) collect information from all
        // windows associated with the taskbar, which can freeze the widget and the whole
        // taskbar. Prevent the propagation of these messages, and stop the widget from
        // blocking the taskbar's message processing.
        if (TaskbarHostMessagePolicy.ShouldSuppressPropagation(msg))
        {
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (_isClosing || _isEnvironmentSuspended)
            return;

        if (!_isDragging &&
            DateTime.UtcNow >= _skipOccupiedAreaProbeUntilUtc &&
            _pendingSizeRequest is { } request &&
            _appliedOrientation is { } orientation)
        {
            _pendingSizeRequest = null;
            ApplyDesiredSizeRequest(request, orientation);
        }

        UpdatePosition();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupWindow();
        _setupComplete = true;
    }

    #region TaskBar Layout&Position

    private void SetupWindow()
    {
        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr taskbarWindowHandle = interop.Handle;

            IntPtr taskbarHandle = _taskBarService.GetSelectedTaskbarHandle(
                SettingsManager.Current.TaskbarBarSelectedMonitor, out _);
            _lastTaskbarHandle = taskbarHandle;

            ApplyLayoutSettings(SettingsManager.Current.WindowMode, SettingsManager.Current.LayoutOrientationMode, taskbarHandle);

            // If this window is created faster than the taskbar is loaded, taskbarHandle will be NULL;
            // UpdatePosition will re-attach once the taskbar appears.
            _taskBarService.DockWindow(taskbarWindowHandle, taskbarHandle);

            CalculateAndSetPosition(taskbarHandle, taskbarWindowHandle);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void UpdatePosition()
    {
        if (_isClosing || _isEnvironmentSuspended || _isDragging || _hostActions.IsEnvironmentRecovering)
        {
            // Explorer 正在恢复时不更新旧宿主位置。
            // Do not reposition the old host while Explorer is recovering.
            return;
        }

        if (!SettingsManager.Current.TaskbarBarEnabled)
            return;

        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr taskbarHandle = _taskBarService.GetSelectedTaskbarHandle(
                SettingsManager.Current.TaskbarBarSelectedMonitor, out _);
            _lastTaskbarHandle = taskbarHandle;

            ApplyLayoutSettings(SettingsManager.Current.WindowMode, SettingsManager.Current.LayoutOrientationMode, taskbarHandle);

            if (interop.Handle == IntPtr.Zero)
            {
                // Our HWND was destroyed with the old taskbar; let MainWindow recreate the window.
                if (_hostActions.IsEnvironmentRecovering)
                    return;

                _timer.Stop();

                Dispatcher.BeginInvoke(_hostActions.RecreateTaskbarWindow, DispatcherPriority.Background);

                return;
            }

            // If the taskbar was not found during initialization or another taskbar was
            // selected, re-attach here.
            if (GetParent(interop.Handle) != taskbarHandle)
            {
                _taskBarService.DockWindow(interop.Handle, taskbarHandle);
            }

            if (taskbarHandle != IntPtr.Zero && interop.Handle != IntPtr.Zero)
            {
                Dispatcher.BeginInvoke(() => { CalculateAndSetPosition(taskbarHandle, interop.Handle); },
                    DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void CalculateAndSetPosition(IntPtr taskbarHandle, IntPtr taskbarWindowHandle)
    {
        // Prevent overlapping updates - if a previous update is still running, skip this tick.
        if (_positionUpdateInProgress)
            return;
        _positionUpdateInProgress = true;

        try
        {
            // get DPI scaling
            double dpiScale = _taskBarService.GetTaskbarDpiScale(taskbarHandle);

            // Guard against invalid DPI (e.g. stale handle during explorer restart)
            if (dpiScale <= 0)
                return;

            if (!_taskBarService.TryGetTaskbarRect(taskbarHandle, out RECT taskbarRect))
                return;

            int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
            int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            if (taskbarWidth <= 0 || taskbarHeight <= 0)
                return;

            // Cover the whole taskbar with the child window (taskbar-relative coordinates)
            _taskBarService.SetWindowPosition(taskbarWindowHandle, taskbarHandle, taskbarRect,
                taskbarWidth, taskbarHeight);

            // Place the bar on the canvas and clip the window to it
            RECT barRect = PositionBar(taskbarRect, dpiScale);
            _taskBarService.ApplyInputRegion(taskbarWindowHandle, [barRect]);
        }
        finally
        {
            _positionUpdateInProgress = false;
        }
    }

    private RECT PositionBar(RECT taskbarRect, double dpiScale)
    {
        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;

        var canvas = MediaControl.CurrentLayout?.Canvas;
        double barWidth = canvas?.Width ?? 300;
        double barHeight = canvas?.Height ?? 44;
        var orientation = _appliedOrientation ?? LayoutOrientation.Horizontal;
        var preferredRange = GetPreferredSafeRange(taskbarRect, orientation, dpiScale);
        var maximumPrimary = preferredRange.Length / dpiScale;
        if (DateTime.UtcNow >= _skipOccupiedAreaProbeUntilUtc && maximumPrimary > 0)
        {
            var currentPrimary = orientation == LayoutOrientation.Horizontal ? barWidth : barHeight;
            if (currentPrimary > maximumPrimary)
            {
                ApplyPrimaryLength(maximumPrimary);
                canvas = MediaControl.CurrentLayout?.Canvas;
                barWidth = canvas?.Width ?? barWidth;
                barHeight = canvas?.Height ?? barHeight;
            }
        }
        int physicalWidth = (int)Math.Round(barWidth * dpiScale);
        int physicalHeight = (int)Math.Round(barHeight * dpiScale);

        bool isVertical = _appliedOrientation == LayoutOrientation.Vertical;
        int primaryLength = isVertical ? taskbarHeight : taskbarWidth;
        int primarySize = isVertical ? physicalHeight : physicalWidth;
        int crossLength = isVertical ? taskbarWidth : taskbarHeight;
        int crossSize = isVertical ? physicalWidth : physicalHeight;

        var placement = TaskbarBarPlacementCalculator.Calculate(
            primaryLength,
            primarySize,
            crossLength,
            crossSize,
            preferredRange,
            SettingsManager.Current.Position,
            SettingsManager.Current.TaskbarBarManualPadding,
            SettingsManager.Current.TaskbarBarCrossAxisOffsetDip,
            dpiScale,
            EdgePadding);
        var primaryPos = placement.Primary;
        var crossPos = placement.Cross;

        // Canvas coordinates and control size are DIPs, hence the dpiScale conversion
        Canvas.SetLeft(MediaControl, (isVertical ? crossPos : primaryPos) / dpiScale);
        Canvas.SetTop(MediaControl, (isVertical ? primaryPos : crossPos) / dpiScale);
        MediaControl.Width = physicalWidth / dpiScale;
        MediaControl.Height = physicalHeight / dpiScale;

        return new RECT
        {
            Left = isVertical ? crossPos : primaryPos,
            Top = isVertical ? primaryPos : crossPos,
            Right = (isVertical ? crossPos : primaryPos) + physicalWidth,
            Bottom = (isVertical ? primaryPos : crossPos) + physicalHeight
        };
    }

    #endregion

    #region SMTC

    /// <summary>
    /// 在任务栏宿主上应用媒体快照并安排位置刷新。
    /// Applies a media snapshot to the taskbar host and schedules repositioning.
    /// </summary>
    /// <param name="snapshot">不可变媒体快照 / Immutable media snapshot.</param>
    public void ApplySnapshot(MediaSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ApplySnapshot(snapshot), DispatcherPriority.Background);
            return;
        }

        if (!SettingsManager.Current.TaskbarBarEnabled || _isClosing || _isEnvironmentSuspended)
            return;

        _lastSnapshot = snapshot;

        if (!_timer.IsEnabled)
            _timer.Start();

        // Delegate UI update to the original media control and its taskbar-only overlay.
        MediaControl.UpdateSongInfo(snapshot);
        MediaControl.ApplyAppearanceSettings();

        // Update position after UI change
        Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);

        // 修改 Visibility 前在 UI 线程再次检查；Explorer 可能在媒体回调与显示步骤之间销毁子 HWND。
        // Recheck on the UI thread immediately before touching Window.Visibility. Explorer
        // can destroy the child HWND between a media callback and this presentation step.
        if (!_isClosing && !_isEnvironmentSuspended)
            Visibility = Visibility.Visible;
    }

    #endregion

    /// <summary>
    /// 应用布局设置：根据窗口模式和布局方向更新媒体控件的布局。
    /// Apply layout settings: update media control layout based on window mode and orientation mode.
    /// </summary>
    /// <param name="windowMode">窗口模式 / Window mode</param>
    /// <param name="orientationMode">布局方向模式 / Layout orientation mode</param>
    public void ApplyLayoutSettings(WindowMode windowMode, LayoutOrientationMode orientationMode)
    {
        var taskbarHandle = _lastTaskbarHandle;
        if (taskbarHandle == IntPtr.Zero)
        {
            taskbarHandle = _taskBarService.GetSelectedTaskbarHandle(
                SettingsManager.Current.TaskbarBarSelectedMonitor, out _);
        }

        ApplyLayoutSettings(windowMode, orientationMode, taskbarHandle);
        Dispatcher.BeginInvoke(UpdatePosition, DispatcherPriority.Background);
    }

    private void ApplyLayoutSettings(WindowMode windowMode, LayoutOrientationMode orientationMode, IntPtr taskbarHandle)
    {
        if (_isClosing)
            return;

        // 将 LayoutOrientationMode 转换为 LayoutOrientation
        // Convert LayoutOrientationMode to LayoutOrientation
        LayoutOrientation orientation;

        if (orientationMode == LayoutOrientationMode.Auto)
        {
            // 自动模式直接根据当前任务栏矩形判定，任务栏在左右边缘时为竖向。
            // Auto mode resolves the current taskbar rectangle; left/right docking is vertical.
            orientation = _taskBarService.IsTaskbarVertical(taskbarHandle)
                ? LayoutOrientation.Vertical
                : LayoutOrientation.Horizontal;
        }
        else
        {
            // 手动模式：直接映射
            // Manual mode: direct mapping
            orientation = orientationMode == LayoutOrientationMode.Horizontal
                ? LayoutOrientation.Horizontal
                : LayoutOrientation.Vertical;
        }

        // 应用布局到媒体控件
        // Apply layout to media control
        var orientationChanged = _appliedOrientation != orientation;
        var lengthScalePercent = SettingsManager.Current.LayoutLengthScalePercent;
        var thicknessScalePercent = ResolveTaskbarThicknessScalePercent(
            taskbarHandle,
            orientation,
            SettingsManager.Current.LayoutThicknessScalePercent);
        var layoutChanged = _appliedWindowMode != windowMode ||
                            orientationChanged ||
                            !lengthScalePercent.Equals(_appliedLengthScalePercent) ||
                            !thicknessScalePercent.Equals(_appliedThicknessScalePercent);
        if (layoutChanged)
        {
            MediaControl.ApplyLayout(windowMode, orientation, lengthScalePercent, thicknessScalePercent);
            MediaControl.ApplyAppearanceSettings();
            MediaControl.ApplyTaskbarExperienceSettings();
            _appliedWindowMode = windowMode;
            _appliedOrientation = orientation;
            _appliedLengthScalePercent = lengthScalePercent;
            _appliedThicknessScalePercent = thicknessScalePercent;
            MediaControl.RefreshDesiredSize();
        }

        if (orientationChanged && IsLoaded)
        {
            Dispatcher.BeginInvoke(UpdatePosition, DispatcherPriority.Loaded);
        }

        // 窗口模式切换由 MainWindow 负责重新创建任务栏或灵动岛宿主。
        // MainWindow recreates the taskbar or dynamic-island host when the window mode changes.
    }

    private double ResolveTaskbarThicknessScalePercent(
        IntPtr taskbarHandle,
        LayoutOrientation orientation,
        double requestedPercent)
    {
        requestedPercent = Math.Clamp(requestedPercent, 70, 125);
        var dpiScale = _taskBarService.GetTaskbarDpiScale(taskbarHandle);
        if (dpiScale <= 0 || !_taskBarService.TryGetTaskbarRect(taskbarHandle, out var taskbarRect))
            return requestedPercent;

        var preset = LayoutPresets.GetLayout(WindowMode.Taskbar, orientation);
        var baseCrossSize = orientation == LayoutOrientation.Vertical
            ? preset.Canvas.Width
            : preset.Canvas.Height;
        var physicalCrossSize = orientation == LayoutOrientation.Vertical
            ? taskbarRect.Right - taskbarRect.Left
            : taskbarRect.Bottom - taskbarRect.Top;
        var availableCrossSizeDip = physicalCrossSize / dpiScale;

        // 任务栏宿主会裁剪超出横轴的内容，因此仅限制实际渲染值，保留用户设置值。
        // The taskbar host clips cross-axis overflow, so cap only the rendered value and preserve the user's setting.
        var maximumPercent = Math.Max(70, availableCrossSizeDip / baseCrossSize * 100);
        return Math.Min(requestedPercent, maximumPercent);
    }

    /// <summary>
    /// 用最新会话列表重建右键菜单的"切换媒体源"子菜单；点击通过命令执行。
    /// Rebuilds the "switch media source" submenu from the latest session list; clicks run through commands.
    /// </summary>
    public void ApplySessions(IReadOnlyList<MediaSessionOption> options)
    {
        if (_isClosing || _isEnvironmentSuspended)
            return;

        Dispatcher.Invoke(() =>
        {
            SessionsMenuItem.Items.Clear();
            foreach (var option in options)
            {
                var item = new MenuItem
                {
                    Header = option.DisplayName,
                    IsCheckable = true,
                    IsChecked = option.IsSelected,
                    Command = _viewModel.SelectMediaSessionCommand,
                    CommandParameter = option.Key
                };
                SessionsMenuItem.Items.Add(item);
            }
        });
    }

    /// <summary>立即应用播放器与右键菜单外观。 / Immediately applies player and context-menu appearance.</summary>
    public void ApplyAppearanceSettings()
    {
        MediaControl.ApplyAppearanceSettings();
    }

    /// <summary>在全局左键点击位于菜单外时关闭右键菜单。 / Closes the context menu after a global left click outside it.</summary>
    public void CloseContextMenuIfOutside(int screenX, int screenY) =>
        ContextMenuHelper.CloseIfOutside(PlayerMenu, screenX, screenY);

    /// <summary>关闭任务栏媒体菜单。/ Closes the taskbar media menu.</summary>
    internal void ClosePlayerMenu() => PlayerMenu.IsOpen = false;

    /// <summary>返回当前媒体栏的屏幕 DIP 边界，供完整面板定位。 / Returns the current media-bar screen DIP bounds for full-panel placement.</summary>
    public Rect GetMediaBarScreenBounds()
    {
        var active = GetActiveControl();
        var point = active.PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(active);
        return new Rect(
            point.X / dpi.DpiScaleX,
            point.Y / dpi.DpiScaleY,
            active.ActualWidth,
            active.ActualHeight);
    }

    /// <summary>返回音频弹窗所需的媒体栏物理像素边界。 / Returns media-bar physical-pixel bounds for the audio flyout.</summary>
    public TrayIconBounds GetMediaBarScreenPhysicalBounds()
    {
        var active = GetActiveControl();
        var point = active.PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(active);
        return new TrayIconBounds(
            (int)Math.Round(point.X),
            (int)Math.Round(point.Y),
            (int)Math.Round(point.X + active.ActualWidth * dpi.DpiScaleX),
            (int)Math.Round(point.Y + active.ActualHeight * dpi.DpiScaleY));
    }

    public void ApplyExperienceSettings()
    {
        MediaControl.ApplyTaskbarExperienceSettings();
        MediaControl.UpdateSongInfo(_lastSnapshot);
        Dispatcher.BeginInvoke(UpdatePosition, DispatcherPriority.Background);
    }

    /// <summary>安全停止任务栏宿主并解除 Explorer 停靠。/ Safely stops the taskbar host and detaches it from Explorer.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
            return;

        _isClosing = true;
        SuspendForEnvironmentRecovery();
        DetachFromTaskbar();
    }

    /// <summary>
    /// 停止旧任务栏宿主的布局、动画和菜单更新，等待环境恢复时重建窗口。
    /// Stops layout, animation, and menu updates on an old taskbar host until it is recreated.
    /// </summary>
    internal void SuspendForEnvironmentRecovery()
    {
        if (_isEnvironmentSuspended)
            return;

        _isEnvironmentSuspended = true;
        _timer.Stop();
        _sizeAnimationTimer.Stop();
        _spectrumTimer.Stop();
        _pendingSizeRequest = null;
        PlayerMenu.IsOpen = false;
    }

    internal void ResumeAfterEnvironmentRecovery()
    {
        if (_isClosing || !_isEnvironmentSuspended)
            return;

        _isEnvironmentSuspended = false;
        if (!_timer.IsEnabled)
            _timer.Start();
        if (!_spectrumTimer.IsEnabled)
            _spectrumTimer.Start();
        UpdatePosition();
    }

    internal void DetachFromTaskbar()
    {
        if (_isDetachedFromTaskbar || _windowHandle == IntPtr.Zero)
            return;

        _isDetachedFromTaskbar = true;
        _taskBarService.UndockWindow(_windowHandle);
    }

    private void MediaControl_TogglePlayPauseRequested(object? sender, EventArgs e) =>
        Execute(_viewModel.TogglePlayPauseCommand);

    private void MediaControl_SkipPreviousRequested(object? sender, EventArgs e) =>
        Execute(_viewModel.SkipPreviousCommand);

    private void MediaControl_SkipNextRequested(object? sender, EventArgs e) =>
        Execute(_viewModel.SkipNextCommand);

    private void MediaControl_ActivateSourceRequested(object? sender, EventArgs e) =>
        Execute(_viewModel.ActivateMediaSourceCommand);

    private static void Execute(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
            command.Execute(null);
    }

    private void MediaControl_OpenFullPanelRequested(object? sender, EventArgs e) =>
        OpenFullPanelRequested?.Invoke(this, EventArgs.Empty);

    private void MediaControl_AudioControlRequested(object? sender, EventArgs e) =>
        AudioControlRequested?.Invoke(this, EventArgs.Empty);

    private async void MediaControl_WheelRequested(object? sender, PlayerSurfaceWheelEventArgs e)
    {
        if (e.IsLeftButtonDown)
            EndTaskbarDrag();
        if (e.IsRightButtonDown)
        {
            _suppressContextMenuUntilUtc = DateTime.UtcNow.AddMilliseconds(450);
            PlayerMenu.IsOpen = false;
        }
        await _interactionRouter.ExecuteWheelAsync(e.Delta, e.IsLeftButtonDown, e.IsRightButtonDown);
    }

    private async void MediaControl_OutputDeviceCycleRequested(object? sender, EventArgs e)
    {
        await _audioInteractionService.CycleOutputDeviceAsync(1, deferApply: false);
    }

    private void MediaControl_SeekRequested(double position) =>
        ExecuteWithParameter(_viewModel.SeekCommand, position);

    private static void ExecuteWithParameter(System.Windows.Input.ICommand command, object parameter)
    {
        if (command.CanExecute(parameter))
            command.Execute(parameter);
    }

    private void MediaControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            SettingsManager.Current.TaskbarBarPositionLocked ||
            e.OriginalSource is DependencyObject source && IsMediaAction(source))
        {
            return;
        }

        var taskbarHandle = _lastTaskbarHandle;
        if (taskbarHandle == IntPtr.Zero ||
            !_taskBarService.TryGetTaskbarRect(taskbarHandle, out var taskbarRect) ||
            !GetCursorPos(out var cursor))
        {
            return;
        }

        var dpiScale = _taskBarService.GetTaskbarDpiScale(taskbarHandle);
        if (dpiScale <= 0)
            return;

        var isVertical = _appliedOrientation == LayoutOrientation.Vertical;
        var primaryLength = isVertical
            ? taskbarRect.Bottom - taskbarRect.Top
            : taskbarRect.Right - taskbarRect.Left;
        var canvas = MediaControl.CurrentLayout?.Canvas;
        var primarySizeDip = isVertical ? canvas?.Height ?? 168 : canvas?.Width ?? 300;
        var primarySize = (int)Math.Round(primarySizeDip * dpiScale);

        _dragStartCursorPrimary = isVertical ? cursor.Y : cursor.X;
        _dragStartBarPrimary = (int)Math.Round((isVertical
            ? Canvas.GetTop(GetActiveControl())
            : Canvas.GetLeft(GetActiveControl())) * dpiScale);
        _dragPrimaryLimit = Math.Max(0, primaryLength - primarySize);
        _isDragPending = true;
        Mouse.Capture(GetActiveControl(), CaptureMode.SubTree);
    }

    private void MediaControl_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging && !_isDragPending)
            return;

        if (e.LeftButton != MouseButtonState.Pressed || SettingsManager.Current.TaskbarBarPositionLocked)
        {
            EndTaskbarDrag();
            return;
        }

        if (!GetCursorPos(out var cursor))
            return;

        var cursorPrimary = _appliedOrientation == LayoutOrientation.Vertical ? cursor.Y : cursor.X;
        if (_isDragPending)
        {
            var dpiScale = _taskBarService.GetTaskbarDpiScale(_lastTaskbarHandle);
            if (Math.Abs(cursorPrimary - _dragStartCursorPrimary) < 4 * Math.Max(1, dpiScale))
                return;
            _isDragPending = false;
            _isDragging = true;
        }
        var targetPrimary = Math.Clamp(
            _dragStartBarPrimary + cursorPrimary - _dragStartCursorPrimary,
            0,
            _dragPrimaryLimit);
        SettingsManager.Current.Position = TaskbarBarPosition.Start;
        SettingsManager.Current.TaskbarBarManualPadding = targetPrimary - EdgePadding;

        var windowHandle = new WindowInteropHelper(this).Handle;
        if (_lastTaskbarHandle != IntPtr.Zero && windowHandle != IntPtr.Zero)
            CalculateAndSetPosition(_lastTaskbarHandle, windowHandle);
        e.Handled = true;
    }

    private void MediaControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((!_isDragging && !_isDragPending) || e.ChangedButton != MouseButton.Left)
            return;

        EndTaskbarDrag();
        e.Handled = true;
    }

    private void MediaControl_DesiredSizeChanged(object? sender, MediaBarSizeRequestEventArgs eventArgs)
    {
        var request = eventArgs.Request;
        if (_isClosing || _appliedOrientation is not { } orientation)
            return;

        _lastDesiredSizeRequest = request;
        if (_isDragging || DateTime.UtcNow < _skipOccupiedAreaProbeUntilUtc)
        {
            _pendingSizeRequest = request;
            return;
        }

        ApplyDesiredSizeRequest(request, orientation);
    }

    private void ApplyDesiredSizeRequest(MediaBarSizeRequest request, LayoutOrientation orientation)
    {
        var maximum = GetAvailablePrimaryLengthDip(orientation);
        var target = request.PrimaryLength;
        if (maximum > 0)
            target = Math.Min(target, maximum);

        var current = MediaControl.CurrentLayout is { } layout
            ? orientation == LayoutOrientation.Horizontal ? layout.Canvas.Width : layout.Canvas.Height
            : target;
        if (Math.Abs(target - current) < LayoutSizeCalculator.MinimumChangeDip)
        {
            ApplyPrimaryLength(target);
            UpdatePosition();
            return;
        }

        _sizeAnimationStart = current;
        _sizeAnimationTarget = target;
        _sizeAnimationProgress = 0;
        _sizeAnimationTimer.Start();
    }

    private void ReloadTaskbarHostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        PlayerMenu.IsOpen = false;
        _hostActions.RequestTaskbarHostReload();
    }

    private double GetAvailablePrimaryLengthDip(LayoutOrientation orientation)
    {
        if (_lastTaskbarHandle == IntPtr.Zero || !_taskBarService.TryGetTaskbarRect(_lastTaskbarHandle, out var rect))
            return 0;

        var dpi = _taskBarService.GetTaskbarDpiScale(_lastTaskbarHandle);
        if (dpi <= 0)
            return 0;

        var range = GetPreferredSafeRange(rect, orientation, dpi);
        return Math.Max(1, range.Length / dpi);
    }

    private TaskbarPrimaryRange GetPreferredSafeRange(RECT taskbarRect, LayoutOrientation orientation, double dpiScale)
    {
        var primaryLength = orientation == LayoutOrientation.Horizontal
            ? taskbarRect.Right - taskbarRect.Left
            : taskbarRect.Bottom - taskbarRect.Top;
        var fallback = new TaskbarPrimaryRange(
            Math.Min(EdgePadding, primaryLength),
            Math.Max(Math.Min(EdgePadding, primaryLength), primaryLength - EdgePadding));

        if (!SettingsManager.Current.TaskbarBarAvoidIcons ||
            _lastTaskbarHandle == IntPtr.Zero ||
            _hostActions.IsEnvironmentRecovering ||
            DateTime.UtcNow < _skipOccupiedAreaProbeUntilUtc)
            return fallback;

        var ranges = _occupiedAreaService.GetSafePrimaryRanges(
            _lastTaskbarHandle,
            taskbarRect,
            orientation,
            dpiScale,
            EdgePadding);
        if (ranges.Count == 0)
            return fallback;

        return SettingsManager.Current.Position switch
        {
            TaskbarBarPosition.End => ranges[^1],
            TaskbarBarPosition.Center => ranges.OrderByDescending(range => range.Length).First(),
            _ => ranges[0]
        };
    }

    private void AdvanceSizeAnimation()
    {
        if (_isClosing || _isDragging)
        {
            _sizeAnimationTimer.Stop();
            return;
        }

        var frame = MediaBarSizeAnimationCalculator.Advance(
            _sizeAnimationStart,
            _sizeAnimationTarget,
            _sizeAnimationProgress,
            elapsedMilliseconds: 16);
        _sizeAnimationProgress = frame.Progress;
        ApplyPrimaryLength(frame.Value);
        UpdatePosition();
        if (frame.IsCompleted)
        {
            ApplyPrimaryLength(_sizeAnimationTarget);
            UpdatePosition();
            _sizeAnimationTimer.Stop();
        }
    }

    private void MediaControl_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging)
            EndTaskbarDrag(releaseCapture: false);
    }

    private void EndTaskbarDrag(bool releaseCapture = true)
    {
        _isDragging = false;
        _isDragPending = false;
        if (releaseCapture && MediaControl.IsMouseCaptured)
            MediaControl.ReleaseMouseCapture();

        if (_pendingSizeRequest is { } request && _appliedOrientation is { } orientation)
        {
            _pendingSizeRequest = null;
            MediaControl_DesiredSizeChanged(this, new MediaBarSizeRequestEventArgs(request));
        }
        UpdatePosition();
    }

    private void Window_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DateTime.UtcNow < _suppressContextMenuUntilUtc)
        {
            e.Handled = true;
            PlayerMenu.IsOpen = false;
        }
    }

    private static bool IsMediaAction(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: "MediaAction" })
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    /// <summary>释放任务栏宿主事件、计时器和消息钩子。/ Releases taskbar-host events, timers, and message hooks.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _timer.Stop();
        _sizeAnimationTimer.Stop();
        _spectrumTimer.Stop();
        MediaControl.TogglePlayPauseRequested -= MediaControl_TogglePlayPauseRequested;
        MediaControl.SkipPreviousRequested -= MediaControl_SkipPreviousRequested;
        MediaControl.SkipNextRequested -= MediaControl_SkipNextRequested;
        MediaControl.ActivateSourceRequested -= MediaControl_ActivateSourceRequested;
        MediaControl.OpenFullPanelRequested -= MediaControl_OpenFullPanelRequested;
        MediaControl.AudioControlRequested -= MediaControl_AudioControlRequested;
        MediaControl.OutputDeviceCycleRequested -= MediaControl_OutputDeviceCycleRequested;
        MediaControl.SeekRequested -= MediaControl_SeekRequested;
        MediaControl.WheelRequested -= MediaControl_WheelRequested;
        MediaControl.DesiredSizeChanged -= MediaControl_DesiredSizeChanged;
        base.OnClosed(e);
    }

    private FrameworkElement GetActiveControl() => MediaControl;

    private void ApplyPrimaryLength(double primaryLength) => MediaControl.ApplyPrimaryLength(primaryLength);
}
