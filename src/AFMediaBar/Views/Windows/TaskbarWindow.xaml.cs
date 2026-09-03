// The media bar docked into the Explorer taskbar, ported from FluentFlyout's TaskbarWindow
// (https://github.com/ManualDinosaur/FluentFlyout, GPL-3.0-or-later).

using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// A transparent child window spanning the whole taskbar. The media bar itself is placed
/// on a canvas and the window is clipped with SetWindowRgn, so the rest of the taskbar
/// stays visible and click-through.
/// </summary>
public partial class TaskbarWindow : Window
{
    // logical (DIP) size of the media bar, matching the previous MainWindow dimensions
    private const double BarWidth = 310;
    private const double BarHeight = 40;

    // physical px offsets from the taskbar edges
    private const int EdgePadding = 20;

    private readonly ITaskbarDockService _taskBarService;
    private readonly MainWindow _mainWindow;
    private readonly DispatcherTimer _timer;

    private IntPtr _lastTaskbarHandle;
    private bool _positionUpdateInProgress;
    private bool _isClosing;

    public TaskbarWindow(ITaskbarDockService taskBarService, MainWindow mainWindow)
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();

        // The context menu bindings rely on MainWindow.ViewModel
        DataContext = mainWindow;

        _taskBarService = taskBarService;
        _mainWindow = mainWindow;

        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1500); // slow auto-update for display changes
        _timer.Tick += (s, e) => UpdatePosition();
        _timer.Start();

        Loaded += Window_Loaded;

        Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource source = (HwndSource)PresentationSource.FromDependencyObject(this);
        source.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WM_DPICHANGED or WM_DPICHANGED_AFTERPARENT)
        {
            // WPF processes WM_DPICHANGED itself. Refresh placement after that layout
            // pass; PMv2 child windows receive the AFTERPARENT variant instead.
            Dispatcher.BeginInvoke(() =>
            {
                InvalidateMeasure();
                InvalidateArrange();
                InvalidateVisual();
                UpdateLayout();
                UpdatePosition();
            }, DispatcherPriority.Loaded);
        }

        // Some interface mods (e.g. Nilesoft Shell, Windhawk) collect information from all
        // windows associated with the taskbar, which can freeze the widget and the whole
        // taskbar. Prevent the propagation of these messages, and stop the widget from
        // blocking the taskbar's message processing.
        switch (msg)
        {
            case WM_GETOBJECT: // MS UI Automation requests
            case WM_SHOWWINDOW:
            case WM_WINDOWPOSCHANGING: // triggers during alt-tabs, window changes
            case WM_NCCALCSIZE: // can trigger layout storms
            case WM_IME_SETCONTEXT:
            case WM_IME_NOTIFY:
                handled = true;
                return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupWindow();
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
        if (_isClosing || MainWindow.ExplorerRestarting)
        {
            // Explorer is restarting -- do NOTHING
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

            if (interop.Handle == IntPtr.Zero)
            {
                // Our HWND was destroyed with the old taskbar; let MainWindow recreate the window.
                if (MainWindow.ExplorerRestarting)
                    return;

                _timer.Stop();

                Dispatcher.BeginInvoke(() => { _mainWindow.RecreateTaskbarWindow(); }, DispatcherPriority.Background);

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

        int physicalWidth = (int)(BarWidth * dpiScale);
        int physicalHeight = (int)(BarHeight * dpiScale);

        int primaryPos = SettingsManager.Current.Position switch
        {
            TaskbarBarPosition.Start => EdgePadding,
            TaskbarBarPosition.End => taskbarWidth - physicalWidth - EdgePadding,
            _ => (taskbarWidth - physicalWidth) / 2
        };
        primaryPos += SettingsManager.Current.TaskbarBarManualPadding;

        int crossPos = (taskbarHeight - physicalHeight) / 2; // vertical center

        // Canvas coordinates and control size are DIPs, hence the dpiScale conversion
        Canvas.SetLeft(MediaControl, primaryPos / dpiScale);
        Canvas.SetTop(MediaControl, crossPos / dpiScale);
        MediaControl.Width = physicalWidth / dpiScale;
        MediaControl.Height = physicalHeight / dpiScale;

        return new RECT
        {
            Left = primaryPos,
            Top = crossPos,
            Right = primaryPos + physicalWidth,
            Bottom = crossPos + physicalHeight
        };
    }

    #endregion

    #region SMTC

    public void ApplySnapshot(MediaSnapshot snapshot)
    {
        if (!SettingsManager.Current.TaskbarBarEnabled || _isClosing)
            return;

        if (!_timer.IsEnabled)
            _timer.Start();

        // Delegate UI update to the media control
        MediaControl.UpdateSongInfo(snapshot);
        MediaControl.ApplyWindowsTheme();

        // Update position after UI change
        Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);

        Dispatcher.Invoke(() => { Visibility = Visibility.Visible; });
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
        if (_isClosing)
            return;

        // 将 LayoutOrientationMode 转换为 LayoutOrientation
        // Convert LayoutOrientationMode to LayoutOrientation
        LayoutOrientation orientation;

        if (orientationMode == LayoutOrientationMode.Auto)
        {
            // 自动模式：根据任务栏位置判断
            // Auto mode: determine based on taskbar position
            // TODO: 需要从 TaskbarDockService 获取任务栏方向
            // 暂时默认为横向
            // TODO: Need to get taskbar orientation from TaskbarDockService
            // Default to horizontal for now
            orientation = LayoutOrientation.Horizontal;
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
        MediaControl.ApplyLayout(windowMode, orientation);

        // 注意：窗口模式切换（任务栏/悬浮）需要重新创建窗口
        // Note: Window mode switching (taskbar/floating) requires window recreation
        // 当前仅更新布局，悬浮窗口模式的完整实现需要额外的窗口逻辑
        // Currently only updates layout, full floating mode implementation requires additional window logic
    }

    /// <summary>
    /// 用最新会话列表重建右键菜单的"切换媒体源"子菜单；点击通过命令执行。
    /// Rebuilds the "switch media source" submenu from the latest session list; clicks run through commands.
    /// </summary>
    public void ApplySessions(IReadOnlyList<MediaSessionOption> options)
    {
        if (_isClosing)
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
                    Command = _mainWindow.ViewModel.SelectMediaSessionCommand,
                    CommandParameter = option.Key
                };
                SessionsMenuItem.Items.Add(item);
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _timer.Stop();
        base.OnClosed(e);
    }
}