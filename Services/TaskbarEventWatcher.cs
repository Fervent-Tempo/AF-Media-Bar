using System.Windows.Threading;
using TaskbarPlayer.Interop;

namespace TaskbarPlayer.Services;

internal sealed class TaskbarEventWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly List<nint> _hooks = [];
    private int _updateQueued;

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
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        var isRelevant = eventId == NativeMethods.EventSystemForeground || window == taskbar;
        if (!isRelevant || Interlocked.Exchange(ref _updateQueued, 1) != 0)
        {
            return;
        }

        if (_dispatcher.HasShutdownStarted)
        {
            Interlocked.Exchange(ref _updateQueued, 0);
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Interlocked.Exchange(ref _updateQueued, 0);
            TaskbarChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }
}
