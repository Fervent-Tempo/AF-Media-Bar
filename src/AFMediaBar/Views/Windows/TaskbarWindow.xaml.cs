// The media bar docked into the Explorer taskbar, ported from FluentFlyout's TaskbarWindow
// (https://github.com/ManualDinosaur/FluentFlyout, GPL-3.0-or-later).
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
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
    private IReadOnlyList<MediaSessionOption>? _currentSessionOptions;

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
            case WM_GETOBJECT:          // MS UI Automation requests
            case WM_SHOWWINDOW:
            case WM_WINDOWPOSCHANGING:  // triggers during alt-tabs, window changes
            case WM_NCCALCSIZE:         // can trigger layout storms
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

                Dispatcher.BeginInvoke(() =>
                {
                    _mainWindow.RecreateTaskbarWindow();
                }, DispatcherPriority.Background);

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
                Dispatcher.BeginInvoke(() =>
                {
                    CalculateAndSetPosition(taskbarHandle, interop.Handle);
                }, DispatcherPriority.Background);
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

    /// <summary>
    /// 用最新会话列表重建右键菜单的"切换媒体源"子菜单。
    /// Rebuilds the "switch media source" submenu from the latest session list.
    /// </summary>
    public void ApplySessions(IReadOnlyList<MediaSessionOption> options)
    {
        if (_isClosing)
            return;

        _currentSessionOptions = options;

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
                    Tag = option.Key
                };
                item.Click += SessionMenuItem_OnClick;
                SessionsMenuItem.Items.Add(item);
            }
        });
    }

    private void SessionMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string key } item)
        {
            return;
        }

        // 点击已选中的会话保持勾选状态；其余交给服务切换，列表事件会重建菜单。
        if (_currentSessionOptions?.FirstOrDefault(option => option.Key == key)?.IsSelected == true)
        {
            item.IsChecked = true;
            return;
        }

        _mainWindow.SelectMediaSession(key);
    }

    private void ReconnectMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _mainWindow.ReconnectMediaSession();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _timer.Stop();
        base.OnClosed(e);
    }
}
