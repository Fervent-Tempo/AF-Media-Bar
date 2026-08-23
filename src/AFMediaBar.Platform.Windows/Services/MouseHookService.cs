using System.Runtime.InteropServices;
using AFMediaBar.Abstractions;
using AFMediaBar.Interop;

namespace AFMediaBar.Services;

/// <summary>
/// 在弹窗打开期间监听全局鼠标按键，用于可靠关闭非激活式 WPF 窗口。
/// Watches global mouse buttons while popups are open so no-activate WPF UI can dismiss reliably.
/// </summary>
public sealed class MouseHookService : IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    // SetWindowsHookEx 不会托管委托生命周期，必须保持强引用直到 Unhook。
    // SetWindowsHookEx does not root delegates; keep this alive until Unhook.
    private readonly NativeMethods.LowLevelMouseDelegate _callback;
    private nint _hook;

    public MouseHookService(IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _callback = OnMouseEvent;
    }

    public event Action<NativeMethods.Point>? MouseButtonPressed;

    public void Start()
    {
        if (_hook != nint.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLowLevel,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_hook == nint.Zero)
        {
            DiagnosticsLogService.Write(
                "mouse-hook-registration-failed",
                details: $"Win32={Marshal.GetLastWin32Error()}");
        }
    }

    public void Stop()
    {
        if (_hook == nint.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = nint.Zero;
    }

    private nint OnMouseEvent(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code >= 0 &&
                wParam.ToInt32() is NativeMethods.WmLeftButtonDown or NativeMethods.WmRightButtonDown)
            {
                var data = Marshal.PtrToStructure<NativeMethods.LowLevelMouseStruct>(lParam);
                if (!_dispatcher.IsShuttingDown)
                {
                    _dispatcher.Post(
                        () => PublishMouseButtonPressed(data.Point),
                        UiDispatchPriority.Input);
                }
            }
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("mouse-hook-callback", exception);
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void PublishMouseButtonPressed(NativeMethods.Point point)
    {
        try
        {
            MouseButtonPressed?.Invoke(point);
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("mouse-hook-subscriber", exception);
        }
    }

    public void Dispose()
    {
        // WH_MOUSE_LL 是系统钩子，退出时必须解除，避免回调访问已释放状态。
        // WH_MOUSE_LL must be unhooked before teardown to avoid callbacks into disposed state.
        Stop();
        MouseButtonPressed = null;
    }
}
