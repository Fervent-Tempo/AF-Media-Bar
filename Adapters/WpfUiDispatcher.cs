using System.Windows.Threading;
using AFMediaBar.Abstractions;

namespace AFMediaBar.Adapters;

internal sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public bool IsShuttingDown => dispatcher.HasShutdownStarted;

    public void Post(Action action, UiDispatchPriority priority)
    {
        dispatcher.BeginInvoke(
            priority == UiDispatchPriority.Send
                ? DispatcherPriority.Send
                : DispatcherPriority.Input,
            action);
    }
}
