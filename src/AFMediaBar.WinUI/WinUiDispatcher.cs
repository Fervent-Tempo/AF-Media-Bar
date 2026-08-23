using AFMediaBar.Abstractions;
using Microsoft.UI.Dispatching;

namespace AFMediaBar.WinUI;

internal sealed class WinUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    private int _shuttingDown;

    public bool IsShuttingDown => Volatile.Read(ref _shuttingDown) != 0;

    public void Post(Action action, UiDispatchPriority priority)
    {
        if (IsShuttingDown)
        {
            return;
        }

        var queuePriority = priority == UiDispatchPriority.Send
            ? DispatcherQueuePriority.High
            : DispatcherQueuePriority.Normal;
        queue.TryEnqueue(queuePriority, new DispatcherQueueHandler(action));
    }

    public void Shutdown() => Interlocked.Exchange(ref _shuttingDown, 1);
}
