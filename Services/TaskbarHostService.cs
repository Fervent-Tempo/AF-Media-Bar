using AFMediaBar.Interop;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// Hosts the WPF HWND inside the Explorer taskbar so both windows share one animation tree.
/// </summary>
internal sealed class TaskbarHostService : IDisposable
{
    private readonly nint _window;
    private readonly long _originalStyle;
    private readonly nint _originalParent;
    private bool _disposed;

    internal TaskbarHostService(nint window)
    {
        _window = window;
        _originalStyle = NativeMethods.GetWindowLongPtr(
            window,
            NativeMethods.GwlStyle).ToInt64();
        _originalParent = NativeMethods.GetWindowLongPtr(
            window,
            NativeMethods.GwlpHwndParent);
    }

    internal nint TaskbarHandle { get; private set; }

    internal bool IsEmbedded { get; private set; }

    internal bool EnsureAttached()
    {
        if (_disposed || _window == nint.Zero)
        {
            return false;
        }

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero || !NativeMethods.IsWindow(taskbar))
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

        TaskbarHandle = taskbar;
        var childStyle = (_originalStyle & ~NativeMethods.WsPopup) | NativeMethods.WsChild;
        NativeMethods.SetWindowLongPtr(
            _window,
            NativeMethods.GwlStyle,
            new nint(childStyle));
        NativeMethods.SetParent(_window, taskbar);

        IsEmbedded =
            NativeMethods.GetWindowLongPtr(_window, NativeMethods.GwlpHwndParent) == taskbar;
        if (!IsEmbedded)
        {
            RestoreTopLevelStyle(taskbar);
        }

        return IsEmbedded;
    }

    internal bool TryGetBounds(out TaskbarHostBounds bounds)
    {
        bounds = default;
        EnsureAttached();
        var taskbar = TaskbarHandle;
        if (taskbar == nint.Zero || !NativeMethods.IsWindow(taskbar))
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

    internal bool Position(
        int screenLeft,
        int screenTop,
        int width,
        int height,
        bool visible)
    {
        if (_disposed || width <= 0 || height <= 0)
        {
            return false;
        }

        var x = screenLeft;
        var y = screenTop;
        var insertAfter = NativeMethods.HwndTopmost;
        if (EnsureAttached())
        {
            var clientPoint = new NativeMethods.Point { X = screenLeft, Y = screenTop };
            if (!NativeMethods.ScreenToClient(TaskbarHandle, ref clientPoint))
            {
                return false;
            }

            x = clientPoint.X;
            y = clientPoint.Y;
            insertAfter = NativeMethods.HwndTop;
        }

        ApplyInputRegion(width, height);
        var flags = NativeMethods.SwpNoActivate;
        if (visible)
        {
            flags |= NativeMethods.SwpShowWindow;
        }

        return NativeMethods.SetWindowPos(
            _window,
            insertAfter,
            x,
            y,
            width,
            height,
            flags);
    }

    private void ApplyInputRegion(int width, int height)
    {
        var region = NativeMethods.CreateRectRgn(0, 0, width, height);
        if (region == nint.Zero)
        {
            return;
        }

        // SetWindowRgn takes ownership after success.
        if (NativeMethods.SetWindowRgn(_window, region, redraw: true) == 0)
        {
            NativeMethods.DeleteObject(region);
        }
    }

    private void RestoreTopLevelStyle(nint taskbarOwner)
    {
        NativeMethods.SetWindowLongPtr(
            _window,
            NativeMethods.GwlStyle,
            new nint(_originalStyle));
        NativeMethods.SetWindowLongPtr(
            _window,
            NativeMethods.GwlpHwndParent,
            taskbarOwner);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeMethods.SetWindowRgn(_window, nint.Zero, redraw: false);
        NativeMethods.SetParent(_window, _originalParent);
        NativeMethods.SetWindowLongPtr(
            _window,
            NativeMethods.GwlStyle,
            new nint(_originalStyle));
        NativeMethods.SetWindowLongPtr(
            _window,
            NativeMethods.GwlpHwndParent,
            _originalParent);
        TaskbarHandle = nint.Zero;
        IsEmbedded = false;
    }
}
