using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
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
    private readonly Dispatcher _dispatcher;
    private readonly object _lifecycleGate = new();
    private readonly ManualResetEventSlim _messageLoopReady = new(false);
    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _hook;
    private volatile bool _disposed;

    /// <summary>创建全局鼠标监听器。/ Creates the global mouse monitor.</summary>
    public NativeMouseInputMonitor(ShellTrayIconService trayIcon)
    {
        _trayIcon = trayIcon;
        _callback = OnMouseEvent;
        _dispatcher = Application.Current.Dispatcher;
    }

    public event EventHandler<TrayWheelEventArgs>? WheelChanged;
    public event EventHandler<NativeMouseButtonEventArgs>? LeftButtonPressed;

    /// <summary>安装低级鼠标钩子。/ Installs the low-level mouse hook.</summary>
    public void Start()
    {
        lock (_lifecycleGate)
        {
            if (_disposed || _hookThread is not null)
            {
                return;
            }

            // A WH_MOUSE_LL callback is dispatched to the thread that installed it. Installing
            // on the WPF thread makes all system mouse input wait while startup is mounting the
            // taskbar window and initializing media services, which causes the visible freeze.
            _hookThread = new Thread(RunHookMessageLoop)
            {
                IsBackground = true,
                Name = "AFMediaBar.MouseHook"
            };
            _hookThread.Start();
        }
    }

    private void RunHookMessageLoop()
    {
        try
        {
            _hookThreadId = NativeMethods.GetCurrentThreadId();
            // PostThreadMessage requires the destination thread to have created its queue.
            NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);
            if (_disposed)
            {
                _messageLoopReady.Set();
                return;
            }

            _hook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL,
                _callback,
                NativeMethods.GetModuleHandle(null),
                0);
            _messageLoopReady.Set();
            if (_hook == IntPtr.Zero)
            {
                Debug.WriteLine($"[NativeMouseInputMonitor] Hook failed: {Marshal.GetLastWin32Error()}");
                return;
            }

            while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessage(ref message);
            }
        }
        finally
        {
            var hook = Interlocked.Exchange(ref _hook, IntPtr.Zero);
            if (hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(hook);
            }

            _hookThreadId = 0;
        }
    }

    private IntPtr OnMouseEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if (wParam.ToInt32() == NativeMethods.WM_MOUSEWHEEL)
                {
                    var delta = unchecked((short)(data.MouseData >> 16));
                    var screenX = data.Point.X;
                    var screenY = data.Point.Y;
                    Post(() =>
                    {
                        if (_trayIcon.TryGetBounds(out var bounds) && bounds.Contains(screenX, screenY))
                        {
                            WheelChanged?.Invoke(this, new TrayWheelEventArgs(delta));
                        }
                    });
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
        if (_disposed || _dispatcher.HasShutdownStarted)
        {
            return;
        }

        _dispatcher.BeginInvoke(callback, DispatcherPriority.Input);
    }

    /// <summary>移除鼠标钩子并清理事件。/ Removes the mouse hook and clears events.</summary>
    public void Dispose()
    {
        Thread? hookThread;
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            hookThread = _hookThread;
        }

        if (hookThread is not null)
        {
            _messageLoopReady.Wait(TimeSpan.FromSeconds(1));
            var threadId = _hookThreadId;
            if (threadId != 0)
            {
                NativeMethods.PostThreadMessage(
                    threadId,
                    NativeMethods.WM_QUIT,
                    UIntPtr.Zero,
                    IntPtr.Zero);
            }

            hookThread.Join(TimeSpan.FromSeconds(1));
        }

        WheelChanged = null;
        LeftButtonPressed = null;
    }
}
