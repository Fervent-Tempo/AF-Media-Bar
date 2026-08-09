using System.Runtime.InteropServices;
using System.Windows.Threading;
using AFMediaBar.Interop;

namespace AFMediaBar.Services;

internal sealed class MouseHookService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly NativeMethods.LowLevelMouseDelegate _callback;
    private nint _hook;

    internal MouseHookService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _callback = OnMouseEvent;
    }

    internal event Action<NativeMethods.Point>? MouseButtonPressed;

    internal void Start()
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
    }

    internal void Stop()
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
        if (code >= 0 &&
            wParam.ToInt32() is NativeMethods.WmLeftButtonDown or NativeMethods.WmRightButtonDown)
        {
            var data = Marshal.PtrToStructure<NativeMethods.LowLevelMouseStruct>(lParam);
            if (!_dispatcher.HasShutdownStarted)
            {
                _dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    () => MouseButtonPressed?.Invoke(data.Point));
            }
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
    }
}
