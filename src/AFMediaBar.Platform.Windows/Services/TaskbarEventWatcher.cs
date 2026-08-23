using System.Diagnostics;
using System.Text;
using AFMediaBar.Abstractions;
using AFMediaBar.Interop;

namespace AFMediaBar.Services;

[Flags]
public enum TaskbarEventSource
{
    None = 0,
    PrimaryTaskbar = 1,
    TaskbarChild = 2,
    ShellSurface = 4
}

public readonly record struct TaskbarWindowEvent(
    uint EventId,
    nint Window,
    TaskbarEventSource Source);

/// <summary>
/// 监听任务栏与 Shell 表面的 WinEvent，并将相关变化投递到 UI 调度器。
/// Watches taskbar and Shell WinEvents and dispatches relevant changes to WPF.
/// </summary>
public sealed class TaskbarEventWatcher : IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    // 原生钩子只保存函数指针，字段引用用于防止委托被 GC 回收。
    // Native hooks keep only a function pointer; this field roots the delegate.
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly List<nint> _hooks = [];
    private readonly object _locationEventLock = new();
    // 位置事件可能每帧多次到达，只保留尚未投递的最新矩形事件。
    // Location events can burst per frame; retain only the newest queued event.
    private TaskbarWindowEvent _pendingLocationEvent;
    private bool _locationUpdateQueued;
    private bool _disposed;

    public TaskbarEventWatcher(IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _callback = OnWinEvent;

        AddHook(NativeMethods.EventSystemForeground);
        AddHook(NativeMethods.EventObjectShow);
        AddHook(NativeMethods.EventObjectHide);
        AddHook(NativeMethods.EventObjectLocationChange);
    }

    public event Action<TaskbarWindowEvent>? TaskbarChanged;

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
            return;
        }

        DiagnosticsLogService.Write(
            "win-event-hook-registration-failed",
            details: $"Event={eventId};Win32={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
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
        try
        {
            OnWinEventCore(
                hook,
                eventId,
                window,
                objectId,
                childId,
                eventThread,
                eventTime);
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("win-event-callback", exception);
        }
    }

    private void OnWinEventCore(
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

        if (_dispatcher.IsShuttingDown)
        {
            return;
        }

        var taskbarEvent = new TaskbarWindowEvent(eventId, window, source);
        if (eventId == NativeMethods.EventObjectLocationChange)
        {
            // SHOW/HIDE/FOREGROUND 不合并；只有高频位置事件可以安全覆盖。
            // Preserve SHOW/HIDE/FOREGROUND; only location bursts are coalesced.
            lock (_locationEventLock)
            {
                _pendingLocationEvent = taskbarEvent;
                if (_locationUpdateQueued)
                {
                    return;
                }

                _locationUpdateQueued = true;
            }

            _dispatcher.Post(
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
                        PublishTaskbarChanged(pendingEvent);
                    }
                },
                UiDispatchPriority.Send);
            return;
        }

        _dispatcher.Post(
            () =>
            {
                if (!_disposed)
                {
                    PublishTaskbarChanged(taskbarEvent);
                }
            },
            UiDispatchPriority.Send);
    }

    private void PublishTaskbarChanged(TaskbarWindowEvent taskbarEvent)
    {
        try
        {
            TaskbarChanged?.Invoke(taskbarEvent);
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("taskbar-event-subscriber", exception);
        }
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

        // WinEvent 钩子是进程外系统资源，窗口关闭时必须逐一解除。
        // WinEvent hooks are external system resources and must all be removed.
        _disposed = true;
        foreach (var hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }

        _hooks.Clear();
        TaskbarChanged = null;
    }
}
