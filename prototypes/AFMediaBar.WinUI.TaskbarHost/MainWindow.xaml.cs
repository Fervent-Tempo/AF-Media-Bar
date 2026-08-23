using AFMediaBar.Interop;
using AFMediaBar.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace AFMediaBar.WinUI.TaskbarHost;

public sealed partial class MainWindow : Window
{
    private const int ContentWidthDip = 320;
    private const int ContentHeightDip = 48;
    private const int TriggerWidthDip = 64;
    private const int TriggerHeightDip = 24;
    private const uint DefaultDpi = 96;
    private const int EnvironmentRecoveryDelayMilliseconds = 900;
    private const int EnvironmentRecoveryRetryMilliseconds = 600;
    private const int EnvironmentRecoveryMaxAttempts = 8;
    private const int PositionRefreshMilliseconds = 1000;
    private const int GwlWndProc = -4;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly WinUiDispatcher _dispatcher;
    private readonly TaskbarEventWatcher _taskbarWatcher;
    private DispatcherQueueTimer? _positionTimer;
    private DispatcherQueueTimer? _recoveryTimer;
    private TaskbarHostService? _hostService;
    private nint _windowHandle;
    private bool _regionEnabled = true;
    private bool _taskbarMode;
    private bool _closing;
    private uint _lastDpi;
    private int _windowWidthPx;
    private int _windowHeightPx;
    private bool _hasPlacedWindow;
    private nint _lastTaskbarHandle;
    private bool _lastTaskbarMode;
    private int _lastLeft;
    private int _lastTop;
    private bool _lastRegionEnabled;
    private uint _lastPlacedDpi;
    private int _lastPlacedWidthPx;
    private int _lastPlacedHeightPx;
    private int _lastTaskbarAreaWidthPx;
    private int _lastTaskbarAreaHeightPx;
    private string? _lastStatus;
    private bool _recoveryRunning;
    private bool _recoveryScheduled;
    private int _recoveryAttempts;
    private string _recoveryReason = string.Empty;
    private uint _taskbarCreatedMessage;
    private nint _previousWindowProc;
    private WindowProcDelegate? _windowProc;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcDelegate(
        nint window,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(
        nint previousWindowProc,
        nint window,
        uint message,
        nint wParam,
        nint lParam);

    public MainWindow()
    {
        InitializeComponent();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _dispatcher = new WinUiDispatcher(_dispatcherQueue);
        _taskbarWatcher = new TaskbarEventWatcher(_dispatcher);
        _taskbarWatcher.TaskbarChanged += TaskbarWatcher_OnChanged;
        Closed += MainWindow_OnClosed;
        Activated += MainWindow_OnActivated;
    }

    private void MainWindow_OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_windowHandle != nint.Zero)
        {
            return;
        }

        _windowHandle = WindowNative.GetWindowHandle(this);
        ConfigureWindowChrome();
        InstallWindowMessageHook();
        _hostService = new TaskbarHostService(_windowHandle);
        StartRecoveryChecks();
        SetFloatingMode();
    }

    private void ConfigureWindowChrome()
    {
        var appWindow = AppWindow.GetFromWindowId(
            Win32Interop.GetWindowIdFromWindow(_windowHandle));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(
            _windowHandle,
            NativeMethods.GwlExStyle).ToInt64();
        extendedStyle |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(
            _windowHandle,
            NativeMethods.GwlExStyle,
            new nint(extendedStyle));
        EnsureWindowSize(appWindow, DefaultDpi);
    }

    private void StartRecoveryChecks()
    {
        _positionTimer = _dispatcherQueue.CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(PositionRefreshMilliseconds);
        _positionTimer.IsRepeating = true;
        _positionTimer.Tick += PositionTimer_OnTick;
        _positionTimer.Start();

        _recoveryTimer = _dispatcherQueue.CreateTimer();
        _recoveryTimer.Interval = TimeSpan.FromMilliseconds(EnvironmentRecoveryDelayMilliseconds);
        _recoveryTimer.IsRepeating = false;
        _recoveryTimer.Tick += RecoveryTimer_OnTick;
        TryRefreshHostPosition("startup");
    }

    private void PositionTimer_OnTick(DispatcherQueueTimer sender, object args)
    {
        if (!_closing && !_recoveryScheduled)
        {
            TryRefreshHostPosition("position-timer");
        }
    }

    private void RecoveryTimer_OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_closing)
        {
            return;
        }

        _recoveryTimer!.Stop();
        _recoveryScheduled = false;
        if (_recoveryRunning)
        {
            return;
        }

        _recoveryRunning = true;
        try
        {
            _recoveryAttempts++;
            InvalidatePlacementState();
            if (_taskbarMode &&
                (_hostService?.SetFloating(floating: false) != true ||
                    !HasTaskbarBounds()))
            {
                RetryEnvironmentRecovery();
                return;
            }

            if (!RefreshHostPosition(scheduleRecovery: false))
            {
                RetryEnvironmentRecovery();
                return;
            }

            _positionTimer?.Start();
            DiagnosticsLogService.Write(
                "winui-taskbar-prototype-recovery-completed",
                details: $"Reason={_recoveryReason};Attempts={_recoveryAttempts}");
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write(
                "winui-taskbar-prototype-recovery-failed",
                exception,
                details: $"Reason={_recoveryReason};Attempts={_recoveryAttempts}");
            RetryEnvironmentRecovery();
        }
        finally
        {
            _recoveryRunning = false;
        }
    }

    private void TaskbarWatcher_OnChanged(TaskbarWindowEvent change)
    {
        if (!_closing)
        {
            DiagnosticsLogService.Write(
                "winui-taskbar-prototype-event",
                details: $"Source={change.Source};Window=0x{change.Window.ToInt64():X}");
            if (change.EventId == NativeMethods.EventObjectLocationChange &&
                !TaskbarBoundsChanged())
            {
                return;
            }

            TryRefreshHostPosition("taskbar-event");
        }
    }

    private void ToggleHostButton_OnClick(object sender, RoutedEventArgs args)
    {
        if (_taskbarMode)
        {
            SetFloatingMode();
        }
        else
        {
            SetTaskbarMode();
        }
    }

    private void ToggleRegionButton_OnClick(object sender, RoutedEventArgs args)
    {
        _regionEnabled = !_regionEnabled;
        TryRefreshHostPosition("region-toggle");
    }

    private void TriggerSurface_OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        SetStatus("Trigger: received");
        TryRefreshHostPosition("trigger");
        args.Handled = true;
    }

    private void ExitButton_OnClick(object sender, RoutedEventArgs args) => Close();

    private void SetFloatingMode()
    {
        if (_hostService is null || !_hostService.SetFloating(floating: true))
        {
            SetStatus("Floating: transition failed");
            return;
        }

        _taskbarMode = false;
        _hasPlacedWindow = false;
        TryRefreshHostPosition("floating-mode");
    }

    private void SetTaskbarMode()
    {
        _taskbarMode = true;
        if (_hostService is null || !_hostService.SetFloating(floating: false))
        {
            SetStatus("Taskbar: Shell_TrayWnd unavailable");
            ScheduleEnvironmentRecovery("taskbar-mode-transition-failed");
            return;
        }

        _hasPlacedWindow = false;
        TryRefreshHostPosition("taskbar-mode");
    }

    private bool RefreshHostPosition(bool scheduleRecovery = true)
    {
        if (_hostService is null || _windowHandle == nint.Zero)
        {
            return false;
        }

        if (_taskbarMode && !_hostService.EnsureAttached())
        {
            SetStatus("Taskbar: waiting for Explorer");
            if (scheduleRecovery)
            {
                ScheduleEnvironmentRecovery("taskbar-unavailable");
            }

            return false;
        }

        NativeMethods.Rect taskbarArea = default;
        uint dpi;
        if (_taskbarMode)
        {
            if (!_hostService.TryGetBounds(out var bounds))
            {
                SetStatus("Taskbar: waiting for bounds");
                if (scheduleRecovery)
                {
                    ScheduleEnvironmentRecovery("taskbar-bounds-unavailable");
                }

                return false;
            }

            taskbarArea = bounds.ScreenBounds;
            dpi = bounds.Dpi > 0 ? bounds.Dpi : DefaultDpi;
        }
        else
        {
            dpi = NativeMethods.GetDpiForWindow(_windowHandle);
            if (dpi == 0)
            {
                dpi = DefaultDpi;
            }
        }

        var widthPx = DipToPixels(ContentWidthDip, dpi);
        var heightPx = DipToPixels(ContentHeightDip, dpi);
        EnsureWindowSize(dpi, widthPx, heightPx);
        var (left, top) = _taskbarMode
            ? ResolveTaskbarPosition(taskbarArea, widthPx, heightPx)
            : (200, 200);
        var inputRects = _regionEnabled
            ? new[]
            {
                new NativeMethods.Rect
                {
                    Left = 0,
                    Top = 0,
                    Right = widthPx - DipToPixels(TriggerWidthDip, dpi),
                    Bottom = heightPx
                },
                new NativeMethods.Rect
                {
                    Left = widthPx - DipToPixels(TriggerWidthDip, dpi),
                    Top = Math.Max(0, (heightPx - DipToPixels(TriggerHeightDip, dpi)) / 2),
                    Right = widthPx,
                    Bottom = Math.Min(
                        heightPx,
                        (heightPx + DipToPixels(TriggerHeightDip, dpi)) / 2)
                }
            }
            : null;
        var taskbarHandle = _taskbarMode ? _hostService.TaskbarHandle : nint.Zero;
        var taskbarAreaChanged = _taskbarMode &&
            (_lastTaskbarAreaWidthPx != taskbarArea.Width ||
                _lastTaskbarAreaHeightPx != taskbarArea.Height);
        var locationChanged = !_taskbarMode || taskbarAreaChanged;
        var needsPosition = !_hasPlacedWindow ||
            _lastTaskbarMode != _taskbarMode ||
            _lastTaskbarHandle != taskbarHandle ||
            taskbarAreaChanged ||
            (locationChanged && (_lastLeft != left || _lastTop != top)) ||
            _lastPlacedDpi != dpi ||
            _lastPlacedWidthPx != widthPx ||
            _lastPlacedHeightPx != heightPx ||
            _lastRegionEnabled != _regionEnabled;
        if (!needsPosition)
        {
            SetStatus(_taskbarMode
                ? $"Taskbar: embedded  region={(_regionEnabled ? "on" : "off")}"
                : $"Floating: top-level  region={(_regionEnabled ? "on" : "off")}");
            return true;
        }

        if (!_hostService.Position(
                left,
                top,
                widthPx,
                heightPx,
                visible: true,
                refresh: true,
                inputRects: inputRects))
        {
            SetStatus("Host: position failed");
            if (scheduleRecovery)
            {
                ScheduleEnvironmentRecovery("window-position-failed");
            }

            return false;
        }

        _hasPlacedWindow = true;
        _lastTaskbarMode = _taskbarMode;
        _lastTaskbarHandle = taskbarHandle;
        _lastLeft = left;
        _lastTop = top;
        _lastRegionEnabled = _regionEnabled;
        _lastPlacedDpi = dpi;
        _lastPlacedWidthPx = widthPx;
        _lastPlacedHeightPx = heightPx;
        _lastTaskbarAreaWidthPx = taskbarArea.Width;
        _lastTaskbarAreaHeightPx = taskbarArea.Height;

        SetStatus(_taskbarMode
            ? $"Taskbar: embedded  region={(_regionEnabled ? "on" : "off")}"
            : $"Floating: top-level  region={(_regionEnabled ? "on" : "off")}");
        return true;
    }

    private (int Left, int Top) ResolveTaskbarPosition(
        NativeMethods.Rect area,
        int widthPx,
        int heightPx)
    {
        return (
            area.Left + Math.Max(0, (area.Width - widthPx) / 2),
            area.Top + Math.Max(0, (area.Height - heightPx) / 2));
    }

    private void EnsureWindowSize(uint dpi, int widthPx, int heightPx)
    {
        var appWindow = AppWindow.GetFromWindowId(
            Win32Interop.GetWindowIdFromWindow(_windowHandle));
        EnsureWindowSize(appWindow, dpi, widthPx, heightPx);
    }

    private void EnsureWindowSize(AppWindow appWindow, uint dpi)
    {
        EnsureWindowSize(
            appWindow,
            dpi,
            DipToPixels(ContentWidthDip, dpi),
            DipToPixels(ContentHeightDip, dpi));
    }

    private void EnsureWindowSize(AppWindow appWindow, uint dpi, int widthPx, int heightPx)
    {
        if (_lastDpi == dpi && _windowWidthPx == widthPx && _windowHeightPx == heightPx)
        {
            return;
        }

        // The host service positions the Win32 client rectangle. Keep the
        // AppWindow client size in the same physical-pixel coordinate space.
        appWindow.ResizeClient(new Windows.Graphics.SizeInt32(widthPx, heightPx));
        _lastDpi = dpi;
        _windowWidthPx = widthPx;
        _windowHeightPx = heightPx;
    }

    private static int DipToPixels(int dip, uint dpi) =>
        Math.Max(1, (int)Math.Round(dip * dpi / 96d));

    private bool HasTaskbarBounds()
    {
        return _hostService?.TryGetBounds(out _) == true;
    }

    private bool TaskbarBoundsChanged()
    {
        if (!_taskbarMode || _hostService?.TryGetBounds(out var bounds) != true)
        {
            return true;
        }

        return _lastTaskbarAreaWidthPx != bounds.ScreenBounds.Width ||
            _lastTaskbarAreaHeightPx != bounds.ScreenBounds.Height;
    }

    private void InvalidatePlacementState()
    {
        _hasPlacedWindow = false;
        _lastTaskbarHandle = nint.Zero;
        _lastTaskbarAreaWidthPx = 0;
        _lastTaskbarAreaHeightPx = 0;
        _lastPlacedDpi = 0;
        _lastPlacedWidthPx = 0;
        _lastPlacedHeightPx = 0;
    }

    private void ScheduleEnvironmentRecovery(string reason)
    {
        if (_closing || _recoveryTimer is null)
        {
            return;
        }

        if (!_recoveryRunning && !_recoveryScheduled)
        {
            _recoveryAttempts = 0;
        }

        _recoveryReason = reason;
        _positionTimer?.Stop();
        _recoveryTimer.Stop();
        _recoveryTimer.Interval = TimeSpan.FromMilliseconds(
            _recoveryAttempts == 0
                ? EnvironmentRecoveryDelayMilliseconds
                : EnvironmentRecoveryRetryMilliseconds);
        _recoveryScheduled = true;
        _recoveryTimer.Start();
    }

    private void RetryEnvironmentRecovery()
    {
        if (_recoveryTimer is null)
        {
            return;
        }

        if (_recoveryAttempts >= EnvironmentRecoveryMaxAttempts)
        {
            DiagnosticsLogService.Write(
                "winui-taskbar-prototype-recovery-exhausted",
                details: $"Reason={_recoveryReason};Attempts={_recoveryAttempts}");
            _recoveryScheduled = false;
            _positionTimer?.Start();
            return;
        }

        _recoveryTimer.Interval = TimeSpan.FromMilliseconds(
            EnvironmentRecoveryRetryMilliseconds);
        _recoveryScheduled = true;
        _recoveryTimer.Start();
    }

    private void InstallWindowMessageHook()
    {
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _windowProc = WindowProc;
        var callback = Marshal.GetFunctionPointerForDelegate(_windowProc);
        _previousWindowProc = NativeMethods.SetWindowLongPtr(
            _windowHandle,
            GwlWndProc,
            callback);
        if (_previousWindowProc == nint.Zero)
        {
            DiagnosticsLogService.Write(
                "winui-taskbar-prototype-window-hook-failed",
                details: $"Win32={Marshal.GetLastWin32Error()}");
        }
    }

    private nint WindowProc(
        nint window,
        uint message,
        nint wParam,
        nint lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            ScheduleEnvironmentRecovery("explorer-restarted");
        }
        else if (message == NativeMethods.WmDpiChanged)
        {
            ScheduleEnvironmentRecovery("dpi-changed");
        }
        else if (message == NativeMethods.WmDisplayChange)
        {
            ScheduleEnvironmentRecovery("display-changed");
        }

        return _previousWindowProc == nint.Zero
            ? nint.Zero
            : CallWindowProc(_previousWindowProc, window, message, wParam, lParam);
    }

    private void SetStatus(string text)
    {
        if (string.Equals(_lastStatus, text, StringComparison.Ordinal))
        {
            return;
        }

        _lastStatus = text;
        StatusText.Text = text;
    }

    private void TryRefreshHostPosition(string source)
    {
        if (_closing)
        {
            return;
        }

        try
        {
            RefreshHostPosition();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write(
                "winui-taskbar-prototype-refresh-failed",
                exception,
                details: $"Source={source}");
            SetStatus("Host: retrying");
        }
    }

    private void MainWindow_OnClosed(object sender, WindowEventArgs args)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        if (_positionTimer is not null)
        {
            _positionTimer.Stop();
            _positionTimer.Tick -= PositionTimer_OnTick;
        }

        if (_recoveryTimer is not null)
        {
            _recoveryTimer.Stop();
            _recoveryTimer.Tick -= RecoveryTimer_OnTick;
        }

        if (_previousWindowProc != nint.Zero &&
            _windowHandle != nint.Zero)
        {
            NativeMethods.SetWindowLongPtr(
                _windowHandle,
                GwlWndProc,
                _previousWindowProc);
            _previousWindowProc = nint.Zero;
        }

        _taskbarWatcher.TaskbarChanged -= TaskbarWatcher_OnChanged;
        _taskbarWatcher.Dispose();
        _dispatcher.Shutdown();
        _hostService?.Dispose();
        _hostService = null;
    }
}
