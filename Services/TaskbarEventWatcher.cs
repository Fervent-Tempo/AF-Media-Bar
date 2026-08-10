using System.Diagnostics;
using System.Text;
using System.Windows.Threading;
using AFMediaBar.Interop;

namespace AFMediaBar.Services;

[Flags]
internal enum TaskbarEventSource
{
    None = 0,
    PrimaryTaskbar = 1,
    TaskbarChild = 2,
    ShellSurface = 4
}

internal readonly record struct TaskbarWindowEvent(
    uint EventId,
    nint Window,
    TaskbarEventSource Source);

internal sealed class TaskbarEventWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly List<nint> _hooks = [];
    private readonly object _locationEventLock = new();
    private TaskbarWindowEvent _pendingLocationEvent;
    private bool _locationUpdateQueued;
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

    internal event Action<TaskbarWindowEvent>? TaskbarChanged;

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
        var source = GetEventSource(window, taskbar);
        var isWindowObjectEvent =
            objectId == NativeMethods.ObjIdWindow &&
            childId == 0;
        var isRelevantObjectEvent =
            (eventId is NativeMethods.EventObjectShow or
                NativeMethods.EventObjectHide or
                NativeMethods.EventObjectLocationChange) &&
            source != TaskbarEventSource.None &&
            (isWindowObjectEvent || source.HasFlag(TaskbarEventSource.PrimaryTaskbar));
        var isRelevant = eventId == NativeMethods.EventSystemForeground ||
            isRelevantObjectEvent;
        if (!isRelevant)
        {
            return;
        }

        if (_dispatcher.HasShutdownStarted)
        {
            return;
        }

        var taskbarEvent = new TaskbarWindowEvent(eventId, window, source);
        if (eventId == NativeMethods.EventObjectLocationChange)
        {
            lock (_locationEventLock)
            {
                _pendingLocationEvent = taskbarEvent;
                if (_locationUpdateQueued)
                {
                    return;
                }

                _locationUpdateQueued = true;
            }

            _dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                () =>
                {
                    TaskbarWindowEvent pendingEvent;
                    lock (_locationEventLock)
                    {
                        pendingEvent = _pendingLocationEvent;
                        _locationUpdateQueued = false;
                    }

                    if (!_disposed)
                    {
                        TaskbarChanged?.Invoke(pendingEvent);
                    }
                });
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
        {
            if (!_disposed)
            {
                TaskbarChanged?.Invoke(taskbarEvent);
            }
        });
    }

    private static TaskbarEventSource GetEventSource(nint window, nint taskbar)
    {
        if (window == nint.Zero)
        {
            return TaskbarEventSource.None;
        }

        if (window == taskbar)
        {
            return TaskbarEventSource.PrimaryTaskbar;
        }

        if (taskbar != nint.Zero && NativeMethods.IsChild(taskbar, window))
        {
            return TaskbarEventSource.TaskbarChild;
        }

        return IsShellSurfaceWindow(window)
            ? TaskbarEventSource.ShellSurface
            : TaskbarEventSource.None;
    }

    private static bool IsShellSurfaceWindow(nint window)
    {
        if (window == nint.Zero)
        {
            return false;
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
