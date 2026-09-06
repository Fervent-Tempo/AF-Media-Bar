using System.Diagnostics;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Interop;

namespace AFMediaBar.Classes.Services;

public sealed record TrayWheelEventArgs(int Delta, bool IsShiftPressed);

/// <summary>
/// 通过低级鼠标钩子监听 AF Media Bar 托盘图标上的滚轮，不拦截系统输入。
/// Watches wheel input over the AF Media Bar tray icon without consuming system input.
/// </summary>
public sealed class NativeMouseWheelMonitor : IDisposable
{
    private readonly ShellTrayIconService _trayIcon;
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private readonly SynchronizationContext? _context;
    private IntPtr _hook;

    public NativeMouseWheelMonitor(ShellTrayIconService trayIcon)
    {
        _trayIcon = trayIcon;
        _callback = OnMouseEvent;
        _context = SynchronizationContext.Current;
    }

    public event EventHandler<TrayWheelEventArgs>? WheelChanged;

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
            Debug.WriteLine($"[NativeMouseWheelMonitor] Hook failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private IntPtr OnMouseEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0 && wParam.ToInt32() == NativeMethods.WM_MOUSEWHEEL)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if (_trayIcon.TryGetBounds(out var bounds) && bounds.Contains(data.Point.X, data.Point.Y))
                {
                    var delta = unchecked((short)(data.MouseData >> 16));
                    var shift = (NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
                    if (_context is null)
                    {
                        WheelChanged?.Invoke(this, new TrayWheelEventArgs(delta, shift));
                    }
                    else
                    {
                        _context.Post(_ => WheelChanged?.Invoke(this, new TrayWheelEventArgs(delta, shift)), null);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[NativeMouseWheelMonitor] Callback failed: {exception}");
        }
        finally
        {
            // 钩子只观察托盘图标命中，任何路径都必须继续传递输入。
            // The hook only observes tray hits; every path must forward the input.
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        WheelChanged = null;
    }
}
