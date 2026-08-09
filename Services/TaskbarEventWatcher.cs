using System.Diagnostics;
using System.Text;
using System.Windows.Threading;
using AFMediaBar.Interop;

namespace AFMediaBar.Services;

internal sealed class TaskbarEventWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly List<nint> _hooks = [];
    private int _updateQueued;
    private int _foregroundUpdateQueued;
    private bool _disposed;

    internal TaskbarEventWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _callback = OnWinEvent;

        AddHook(NativeMethods.EventSystemForeground);
        AddHook(NativeMethods.EventObjectShow);
        AddHook(NativeMethods.EventObjectHide);
        AddHook(NativeMethods.EventObjectLocationChange);
    }

    internal event EventHandler? TaskbarChanged;

    private void AddHook(uint eventId)
    {
        var hook = NativeMethods.SetWinEventHook(
            eventId,
            eventId,
            nint.Zero,
            _callback,
            0,
            0,
            NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess);

        if (hook != nint.Zero)
        {
            _hooks.Add(hook);
        }
    }

    private void OnWinEvent(
        nint hook,
        uint eventId,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (_disposed)
        {
            return;
        }

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        var isTopLevelWindowVisibilityEvent =
            eventId is NativeMethods.EventObjectShow or NativeMethods.EventObjectHide &&
            objectId == NativeMethods.ObjIdWindow &&
            childId == 0 &&
            IsShellSurfaceWindow(window, taskbar);
        var isRelevant = eventId == NativeMethods.EventSystemForeground ||
            window == taskbar ||
            isTopLevelWindowVisibilityEvent;
        if (!isRelevant)
        {
            return;
        }

        if (_dispatcher.HasShutdownStarted)
        {
            Interlocked.Exchange(ref _updateQueued, 0);
            Interlocked.Exchange(ref _foregroundUpdateQueued, 0);
            return;
        }

        if (eventId == NativeMethods.EventSystemForeground)
        {
            if (Interlocked.Exchange(ref _foregroundUpdateQueued, 1) != 0)
            {
                return;
            }

            _dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                () =>
                {
                    Interlocked.Exchange(ref _foregroundUpdateQueued, 0);
                    if (!_disposed)
                    {
                        TaskbarChanged?.Invoke(this, EventArgs.Empty);
                    }
                });
            return;
        }

        if (Interlocked.Exchange(ref _updateQueued, 1) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
        {
            Interlocked.Exchange(ref _updateQueued, 0);
            if (!_disposed)
            {
                TaskbarChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private static bool IsShellSurfaceWindow(nint window, nint taskbar)
    {
        if (window == nint.Zero || window == taskbar)
        {
            return window != nint.Zero;
        }

        var classNameBuffer = new StringBuilder(128);
        if (NativeMethods.GetClassName(
                window,
                classNameBuffer,
                classNameBuffer.Capacity) <= 0)
        {
            return false;
        }

        var className = classNameBuffer.ToString();
        if (className is
            "Shell_SecondaryTrayWnd" or
            "XamlExplorerHostIslandWindow" or
            "ControlCenterWindow")
        {
            return true;
        }

        if (className is not "ApplicationFrameWindow" and not "Windows.UI.Core.CoreWindow")
        {
            return false;
        }

        if (NativeMethods.GetWindowThreadProcessId(window, out var processId) == 0 ||
            processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName is
                "StartMenuExperienceHost" or
                "ShellExperienceHost" or
                "ShellHost" or
                "SearchHost" or
                "SearchApp" or
                "explorer";
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
        foreach (var hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }

        _hooks.Clear();
        TaskbarChanged = null;
    }
}
