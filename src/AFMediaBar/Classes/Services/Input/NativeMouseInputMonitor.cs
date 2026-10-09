using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;

namespace AFMediaBar.Classes.Services;

/// <summary>托盘滚轮事件参数。/ Tray-wheel event arguments.</summary>
public sealed record TrayWheelEventArgs(int Delta, bool IsShiftDown, bool IsLeftButtonDown, bool IsRightButtonDown);

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
    private bool _isLeftButtonDown;
    private bool _isRightButtonDown;
    // 这三个字段由钩子线程写、由 UI 线程读：钩子回调一定先于目标窗口的鼠标消息执行，因此 UI 线程读到的状态
    // 属于"这次按压"的结果，不依赖 Dispatcher 投递顺序。
    // These three fields are written on the hook thread and read on the UI thread: a hook callback always runs before the target
    // window's mouse message, so what the UI thread reads describes the very press it is handling and does not depend on the
    // order dispatcher posts are delivered in.
    private bool _chordWheelDuringPress;
    private long _suppressNextClickUntilTicks;

    /// <summary>
    /// 组合滚轮在本次按压中是否已经用过；用于判断"松开后合成的那次点击"是否需要抑制。
    /// Whether a chord wheel was consumed during the current press, used to decide whether the click synthesized on release has to
    /// be swallowed.
    /// </summary>
    private const int SuppressedClickWindowMilliseconds = 750;

    /// <summary>创建全局鼠标监听器。/ Creates the global mouse monitor.</summary>
    public NativeMouseInputMonitor(ShellTrayIconService trayIcon)
    {
        _trayIcon = trayIcon;
        _callback = OnMouseEvent;
        _dispatcher = Application.Current.Dispatcher;
    }

    public event EventHandler<TrayWheelEventArgs>? WheelChanged;
    public event EventHandler<NativeMouseButtonEventArgs>? LeftButtonPressed;

    /// <summary>
    /// 全局左键抬起（钩子线程观察到后投递到 UI 线程）。陈旧鼠标捕获存续期间 WPF 会把命中测试整体锁进捕获子树，
    /// 主窗口的预览隧道一件事件都收不到——宿主的修复层挂在这条钩子事件上才能在「抬起」这个确定性终点仍然运行。
    /// Global left-button release (observed on the hook thread, posted to the UI thread). While a stale mouse capture lasts, WPF
    /// locks hit-testing inside the captured subtree and the main window's preview tunnel receives nothing at all — the host's
    /// repair layer hooks this hook-fed event so it still runs at the deterministic endpoint of a release.
    /// </summary>
    public event EventHandler<NativeMouseButtonEventArgs>? LeftButtonReleased;

    /// <summary>鼠标左键当前是否按住（钩子线程维护的物理状态）。 / Whether the left button is currently held, as tracked by the hook thread.</summary>
    public bool IsLeftButtonDown => _isLeftButtonDown;

    /// <summary>鼠标右键当前是否按住。 / Whether the right button is currently held.</summary>
    public bool IsRightButtonDown => _isRightButtonDown;

    /// <summary>
    /// Shift 当前是否按住。组合键可能是 Shift，而托盘图标与任务栏窗口都不接收键盘事件，因此这里直接读物理键状态。
    /// Whether Shift is currently held. The chord key can be Shift, and neither the tray icon nor the taskbar window receives key
    /// events, so the physical key state is read directly.
    /// </summary>
    public bool IsShiftDown => (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;

    /// <summary>
    /// 指针当前是否停在托盘图标上。悬停状态只能由这里判断：托盘图标是 Shell 图标，
    /// 它的悬停提示不会在指针离开时通知我们，Win32 调用也留在本服务里。
    /// Whether the pointer currently sits on the tray icon. Only this service can judge that: the icon belongs to the Shell, its
    /// hover bubble never tells us when the pointer left, and Win32 calls stay inside this service.
    /// </summary>
    public bool IsPointerOverTray() =>
        _trayIcon.TryGetBounds(out var bounds) &&
        NativeMethods.GetCursorPos(out var point) &&
        bounds.Contains(point.X, point.Y);

    /// <summary>
    /// 取走"这一次点击应当被抑制"的标记。组合滚轮（按住鼠标左键或右键再滚动）结束时的松键会合成一次点击或右键菜单，
    /// 用户按下组合键的意图只是滚轮，因此该点击必须被吞掉。标记由钩子线程在松键时写入，UI 线程在处理该点击时取走，
    /// 并带一个有界时限：松键后没有落在我们表面上的点击时，标记会自行过期，不会误吞之后真正的点击。
    /// Takes the "this click must be swallowed" flag. Releasing the button after a chord wheel (a wheel scrolled while a mouse
    /// button was held) synthesizes a click or a context menu, while the user only meant to scroll, so that click has to be
    /// swallowed. The hook thread sets the flag on release and the UI thread takes it while handling that click; it also carries a
    /// bounded deadline, so a release that produced no click on our surfaces expires instead of eating a later, real one.
    /// </summary>
    public bool ConsumeSuppressedClick()
    {
        var deadline = Interlocked.Read(ref _suppressNextClickUntilTicks);
        if (deadline == 0)
            return false;

        Interlocked.Exchange(ref _suppressNextClickUntilTicks, 0);
        return Environment.TickCount64 <= deadline;
    }

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
    {        try
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var message = wParam.ToInt32();
                if (message == NativeMethods.WM_LBUTTONDOWN)
                {
                    _isLeftButtonDown = true;
                    Post(() => LeftButtonPressed?.Invoke(
                        this,
                        new NativeMouseButtonEventArgs(data.Point.X, data.Point.Y)));
                }
                else if (message == NativeMethods.WM_LBUTTONUP)
                {
                    _isLeftButtonDown = false;
                    MarkChordWheelClickSuppression();
                    Post(() => LeftButtonReleased?.Invoke(
                        this,
                        new NativeMouseButtonEventArgs(data.Point.X, data.Point.Y)));
                }
                else if (message == NativeMethods.WM_RBUTTONDOWN)
                {
                    _isRightButtonDown = true;
                }
                else if (message == NativeMethods.WM_RBUTTONUP)
                {
                    _isRightButtonDown = false;
                    MarkChordWheelClickSuppression();
                }
                else if (message == NativeMethods.WM_MOUSEWHEEL)
                {
                    // 组合滚轮在本次按压里出现过就记账，等松键时把合成的那次点击抑制掉；这里只记录状态，不拦截输入。
                    // Note a chord wheel inside this press so the synthesized click can be swallowed on release; this only records
                    // state and never consumes system input.
                    if (_isLeftButtonDown || _isRightButtonDown)
                        _chordWheelDuringPress = true;

                    var delta = unchecked((short)(data.MouseData >> 16));
                    var screenX = data.Point.X;
                    var screenY = data.Point.Y;
                    var isShiftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
                    var isLeftButtonDown = _isLeftButtonDown;
                    var isRightButtonDown = _isRightButtonDown;
                    Post(() =>
                    {
                        if (_trayIcon.TryGetBounds(out var bounds) && bounds.Contains(screenX, screenY))
                        {
                            WheelChanged?.Invoke(this, new TrayWheelEventArgs(
                                delta,
                                isShiftDown,
                                isLeftButtonDown,
                                isRightButtonDown));
                        }
                    });
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

    /// <summary>
    /// 松键时结算"这次按压里是否用过组合滚轮"。用过就给出一个有界的抑制窗口，供紧接着的合成点击或右键菜单取走。
    /// Settles whether a chord wheel happened inside the press that just ended. If it did, a bounded suppression window is opened
    /// for the synthesized click or context menu that follows.
    /// </summary>
    private void MarkChordWheelClickSuppression()
    {
        if (!_chordWheelDuringPress)
            return;

        _chordWheelDuringPress = false;
        Interlocked.Exchange(
            ref _suppressNextClickUntilTicks,
            Environment.TickCount64 + SuppressedClickWindowMilliseconds);
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
        LeftButtonReleased = null;
    }
}
