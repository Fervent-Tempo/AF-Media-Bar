using System.Diagnostics;
using System.Text;
using AFMediaBar.Interop;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 在 Explorer 任务栏中挂载或恢复 WPF HWND，使窗口共享任务栏动画树。
/// Hosts or restores the WPF HWND in Explorer so the window shares the taskbar animation tree.
/// </summary>
public sealed class TaskbarHostService : IDisposable
{
    private readonly nint _window;
    private readonly long _originalStyle;
    private readonly nint _originalParent;
    private bool _disposed;

    public TaskbarHostService(nint window)
    {
        _window = window;
        _originalStyle = NativeMethods.GetWindowLongPtr(
            window,
            NativeMethods.GwlStyle).ToInt64();
        _originalParent = NativeMethods.GetWindowLongPtr(
            window,
            NativeMethods.GwlpHwndParent);
    }

    public nint TaskbarHandle { get; private set; }

    public bool IsEmbedded { get; private set; }

    public bool IsFloating { get; private set; }

    public bool SetFloating(bool floating)
    {
        if (_disposed || _window == nint.Zero || !NativeMethods.IsWindow(_window))
        {
            return false;
        }

        // WPF may assign a hidden owner when ShowInTaskbar is false. Restoring
        // that HWND as the floating owner makes Windows hide the media bar with
        // the owner. A floating media bar must be an unowned top-level window.
        var expectedParent = floating ? nint.Zero : FindTaskbar();
        if (!floating && expectedParent == nint.Zero)
        {
            TaskbarHandle = nint.Zero;
            IsEmbedded = false;
            return false;
        }

        var actualParent = NativeMethods.GetWindowLongPtr(_window, NativeMethods.GwlpHwndParent);
        var actualStyle = NativeMethods.GetWindowLongPtr(_window, NativeMethods.GwlStyle).ToInt64();
        var expectedStyle = floating
            ? _originalStyle
            : (_originalStyle & ~NativeMethods.WsPopup) | NativeMethods.WsChild;
        if (floating &&
            actualParent == _originalParent &&
            actualStyle == expectedStyle)
        {
            IsFloating = true;
            TaskbarHandle = nint.Zero;
            IsEmbedded = false;
            return true;
        }

        if (IsFloating == floating &&
            actualParent == expectedParent &&
            actualStyle == expectedStyle)
        {
            TaskbarHandle = floating ? nint.Zero : expectedParent;
            IsEmbedded = !floating;
            return true;
        }

        // 保存当前状态，以便原生调用失败时完整回滚。
        // Capture the current state so a failed native transition can be rolled back.
        var previousParent = actualParent;
        var previousStyle = actualStyle;
        var previousTaskbarHandle = TaskbarHandle;
        var previousEmbedded = IsEmbedded;
        var previousFloating = IsFloating;
        if (!floating)
        {
            NativeMethods.ShowWindow(_window, NativeMethods.SwHide);
        }
        NativeMethods.SetWindowRgn(_window, nint.Zero, redraw: false);

        if (floating)
        {
            if (!TrySetWindowState(expectedParent, _originalStyle))
            {
                RestoreWindowState(previousParent, previousStyle);
                return false;
            }

            TaskbarHandle = nint.Zero;
            IsEmbedded = false;
        }
        else
        {
            if (!TrySetWindowState(expectedParent, expectedStyle))
            {
                RestoreWindowState(previousParent, previousStyle);
                TaskbarHandle = nint.Zero;
                IsEmbedded = false;
                return false;
            }

            if (!IsValidTaskbar(expectedParent))
            {
                RestoreWindowState(previousParent, previousStyle);
                TaskbarHandle = nint.Zero;
                IsEmbedded = false;
                return false;
            }

            TaskbarHandle = expectedParent;
            IsEmbedded = true;
        }

        IsFloating = floating;
        if (!RefreshFrame())
        {
            if (floating)
            {
                // The parent/style transition already succeeded. A frame refresh
                // failure must not put the top-level window back under Explorer.
                DiagnosticsLogService.Write("floating-frame-refresh-failed");
                return true;
            }

            RestoreWindowState(previousParent, previousStyle);
            TaskbarHandle = previousTaskbarHandle;
            IsEmbedded = previousEmbedded;
            IsFloating = previousFloating;
            return false;
        }

        return true;
    }

    public bool EnsureAttached()
    {
        if (_disposed || _window == nint.Zero || IsFloating)
        {
            return false;
        }

        var taskbar = FindTaskbar();
        if (taskbar == nint.Zero || !IsValidTaskbar(taskbar))
        {
            TaskbarHandle = nint.Zero;
            IsEmbedded = false;
            return false;
        }

        if (taskbar == TaskbarHandle &&
            NativeMethods.GetWindowLongPtr(_window, NativeMethods.GwlpHwndParent) == taskbar &&
            IsEmbedded)
        {
            return true;
        }

        return SetFloating(false) && IsEmbedded;
    }

    private static nint FindTaskbar()
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        return IsValidTaskbar(taskbar)
            ? taskbar
            : nint.Zero;
    }

    public bool TryGetBounds(out TaskbarHostBounds bounds)
    {
        bounds = default;
        if (!IsFloating)
        {
            EnsureAttached();
        }

        var taskbar = TaskbarHandle;
        if (taskbar == nint.Zero || !IsValidTaskbar(taskbar))
        {
            return false;
        }

        NativeMethods.Rect screenBounds;
        if (NativeMethods.GetClientRect(taskbar, out var clientBounds) &&
            clientBounds.Width > 0 &&
            clientBounds.Height > 0)
        {
            var clientOrigin = new NativeMethods.Point();
            if (!NativeMethods.ClientToScreen(taskbar, ref clientOrigin))
            {
                return false;
            }

            screenBounds = new NativeMethods.Rect
            {
                Left = clientOrigin.X,
                Top = clientOrigin.Y,
                Right = clientOrigin.X + clientBounds.Width,
                Bottom = clientOrigin.Y + clientBounds.Height
            };
        }
        else if (!NativeMethods.GetWindowRect(taskbar, out screenBounds) ||
            screenBounds.Width <= 0 ||
            screenBounds.Height <= 0)
        {
            return false;
        }

        bounds = new TaskbarHostBounds(
            taskbar,
            screenBounds,
            NativeMethods.GetDpiForWindow(taskbar));
        return true;
    }

    public bool Position(
        int screenLeft,
        int screenTop,
        int width,
        int height,
        bool visible,
        bool topmost = false,
        bool refresh = true,
        IReadOnlyList<NativeMethods.Rect>? inputRects = null)
    {
        if (_disposed || width <= 0 || height <= 0)
        {
            return false;
        }

        var x = screenLeft;
        var y = screenTop;
        var insertAfter = topmost ? NativeMethods.HwndTopmost : NativeMethods.HwndTop;
        if (!IsFloating)
        {
            if (!EnsureAttached())
            {
                return false;
            }

            var clientPoint = new NativeMethods.Point { X = screenLeft, Y = screenTop };
            if (!NativeMethods.ScreenToClient(TaskbarHandle, ref clientPoint))
            {
                return false;
            }

            x = clientPoint.X;
            y = clientPoint.Y;
            insertAfter = NativeMethods.HwndTop;
        }

        var regionApplied = !refresh || ApplyInputRegion(width, height, inputRects);
        if (refresh && !regionApplied)
        {
            // A WPF layered window can reject SetWindowRgn on some desktop/DPI
            // combinations. The region is only an input clip; it must not prevent
            // the top-level window from being positioned and shown.
            DiagnosticsLogService.Write(
                "window-region-update-failed",
                details: $"Width={width};Height={height};Win32={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
            NativeMethods.SetWindowRgn(_window, nint.Zero, redraw: false);
        }
        var flags = NativeMethods.SwpNoActivate;
        if (IsFloating && !topmost)
        {
            flags |= NativeMethods.SwpNoZOrder;
        }
        if (visible)
        {
            flags |= NativeMethods.SwpShowWindow;
        }

        var positioned = NativeMethods.SetWindowPos(
            _window,
            insertAfter,
            x,
            y,
            width,
            height,
            flags);
        if (positioned && visible && refresh)
        {
            NativeMethods.ShowWindow(_window, NativeMethods.SwShowNoActivate);
            if (regionApplied)
            {
                Redraw();
            }
        }

        if (!positioned)
        {
            DiagnosticsLogService.Write(
                "window-position-native-failed",
                details: $"X={x};Y={y};Width={width};Height={height};Win32={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        }

        return positioned;
    }

    private bool RefreshFrame()
    {
        return NativeMethods.SetWindowPos(
            _window,
            NativeMethods.HwndTop,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove |
                NativeMethods.SwpNoSize |
                NativeMethods.SwpNoZOrder |
                NativeMethods.SwpNoActivate |
                NativeMethods.SwpNoOwnerZOrder |
                NativeMethods.SwpFrameChanged);
    }

    public void Redraw()
    {
        if (_disposed)
        {
            return;
        }

        NativeMethods.RedrawWindow(
            _window,
            nint.Zero,
            nint.Zero,
            NativeMethods.RdwInvalidate |
                NativeMethods.RdwErase |
                NativeMethods.RdwAllChildren |
                NativeMethods.RdwUpdateNow);
    }

    /// <summary>
    /// 为布局窗口建立真实输入区域；折叠时只把条带与窄触发条放入并集，隐藏内容所在矩形保持穿透。
    /// Builds the real input region for a layout window; while collapsed, only the strip and narrow trigger bars are unioned, leaving hidden content click-through.
    /// </summary>
    private bool ApplyInputRegion(
        int width,
        int height,
        IReadOnlyList<NativeMethods.Rect>? inputRects)
    {
        var region = CreateInputRegion(width, height, inputRects);
        if (region == nint.Zero)
        {
            return false;
        }

        // SetWindowRgn takes ownership after success.
        if (NativeMethods.SetWindowRgn(_window, region, redraw: true) == 0)
        {
            NativeMethods.DeleteObject(region);
            return false;
        }

        return true;
    }

    private static nint CreateInputRegion(
        int width,
        int height,
        IReadOnlyList<NativeMethods.Rect>? inputRects)
    {
        if (inputRects is null || inputRects.Count == 0)
        {
            return NativeMethods.CreateRectRgn(0, 0, width, height);
        }

        var combined = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (combined == nint.Zero)
        {
            return nint.Zero;
        }

        var hasArea = false;
        foreach (var candidate in inputRects)
        {
            var left = Math.Clamp(candidate.Left, 0, width);
            var top = Math.Clamp(candidate.Top, 0, height);
            var right = Math.Clamp(candidate.Right, 0, width);
            var bottom = Math.Clamp(candidate.Bottom, 0, height);
            if (right <= left || bottom <= top)
            {
                continue;
            }

            var part = NativeMethods.CreateRectRgn(left, top, right, bottom);
            if (part == nint.Zero)
            {
                NativeMethods.DeleteObject(combined);
                return nint.Zero;
            }

            var result = NativeMethods.CombineRgn(
                combined,
                combined,
                part,
                NativeMethods.RgnOr);
            NativeMethods.DeleteObject(part);
            if (result == 0)
            {
                NativeMethods.DeleteObject(combined);
                return nint.Zero;
            }

            hasArea = true;
        }

        if (hasArea)
        {
            return combined;
        }

        NativeMethods.DeleteObject(combined);
        return NativeMethods.CreateRectRgn(0, 0, width, height);
    }

    private bool TrySetWindowState(nint parent, long style)
    {
        NativeMethods.SetWindowLongPtr(_window, NativeMethods.GwlStyle, new nint(style));
        if (NativeMethods.GetWindowLongPtr(_window, NativeMethods.GwlStyle).ToInt64() != style)
        {
            return false;
        }

        NativeMethods.SetParent(_window, parent);
        NativeMethods.SetWindowLongPtr(_window, NativeMethods.GwlpHwndParent, parent);
        return NativeMethods.GetWindowLongPtr(_window, NativeMethods.GwlpHwndParent) == parent &&
            NativeMethods.GetWindowLongPtr(_window, NativeMethods.GwlStyle).ToInt64() == style;
    }

    private bool RestoreWindowState(nint parent, long style)
    {
        if (_window == nint.Zero || !NativeMethods.IsWindow(_window))
        {
            return false;
        }

        var restored = TrySetWindowState(parent, style);
        NativeMethods.SetWindowRgn(_window, nint.Zero, redraw: false);
        RefreshFrame();
        return restored;
    }

    private static bool IsValidTaskbar(nint taskbar)
    {
        if (taskbar == nint.Zero || !NativeMethods.IsWindow(taskbar))
        {
            return false;
        }

        var className = new StringBuilder(64);
        if (NativeMethods.GetClassName(taskbar, className, className.Capacity) <= 0 ||
            !string.Equals(className.ToString(), "Shell_TrayWnd", StringComparison.Ordinal))
        {
            return false;
        }

        if (NativeMethods.GetWindowThreadProcessId(taskbar, out var processId) == 0 ||
            processId == 0)
        {
            return false;
        }

        if (!NativeMethods.GetClientRect(taskbar, out var clientBounds) ||
            clientBounds.Width <= 0 ||
            clientBounds.Height <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_window == nint.Zero || !NativeMethods.IsWindow(_window))
        {
            TaskbarHandle = nint.Zero;
            IsEmbedded = false;
            IsFloating = false;
            return;
        }

        IsFloating = false;
        NativeMethods.SetWindowRgn(_window, nint.Zero, redraw: false);
        RestoreWindowState(_originalParent, _originalStyle);
        TaskbarHandle = nint.Zero;
        IsEmbedded = false;
    }
}
