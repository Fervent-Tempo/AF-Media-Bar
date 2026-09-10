using System.Diagnostics;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Interop;

namespace AFMediaBar.Classes.Services;

/// <summary>托盘滚轮事件参数。/ Tray-wheel event arguments.</summary>
public sealed record TrayWheelEventArgs(int Delta);

/// <summary>全局左键事件的屏幕坐标。/ Screen coordinates for a global left-button event.</summary>
public sealed record NativeMouseButtonEventArgs(int ScreenX, int ScreenY);

/// <summary>
/// 通过低级鼠标钩子监听托盘滚轮和全局左键，不拦截系统输入。
/// Watches tray-wheel and global left-button input without consuming system input.
/// </summary>
public sealed class NativeMouseInputMonitor : IDisposable
{
    private readonly ShellTrayIconService _trayIcon;
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private readonly SynchronizationContext? _context;
    private IntPtr _hook;

    /// <summary>创建全局鼠标监听器。/ Creates the global mouse monitor.</summary>
    public NativeMouseInputMonitor(ShellTrayIconService trayIcon)
    {
        _trayIcon = trayIcon;
        _callback = OnMouseEvent;
        _context = SynchronizationContext.Current;
    }

    public event EventHandler<TrayWheelEventArgs>? WheelChanged;
    public event EventHandler<NativeMouseButtonEventArgs>? LeftButtonPressed;

    /// <summary>安装低级鼠标钩子。/ Installs the low-level mouse hook.</summary>
    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_hook == IntPtr.Zero)
        {
            Debug.WriteLine($"[NativeMouseInputMonitor] Hook failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private IntPtr OnMouseEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if (wParam.ToInt32() == NativeMethods.WM_MOUSEWHEEL &&
                    _trayIcon.TryGetBounds(out var bounds) && bounds.Contains(data.Point.X, data.Point.Y))
                {
                    var delta = unchecked((short)(data.MouseData >> 16));
                    Post(() => WheelChanged?.Invoke(this, new TrayWheelEventArgs(delta)));
                }
                else if (wParam.ToInt32() == NativeMethods.WM_LBUTTONDOWN)
                {
                    Post(() => LeftButtonPressed?.Invoke(
                        this,
                        new NativeMouseButtonEventArgs(data.Point.X, data.Point.Y)));
                }
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[NativeMouseInputMonitor] Callback failed: {exception}");
        }
        finally
        {
            // 钩子只观察托盘图标命中，任何路径都必须继续传递输入。
            // The hook only observes tray hits; every path must forward the input.
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void Post(Action callback)
    {
        if (_context is null)
        {
            callback();
            return;
        }

        _context.Post(_ => callback(), null);
    }

    /// <summary>移除鼠标钩子并清理事件。/ Removes the mouse hook and clears events.</summary>
    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        WheelChanged = null;
        LeftButtonPressed = null;
    }
}
