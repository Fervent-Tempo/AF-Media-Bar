namespace AFMediaBar.Abstractions;

public enum UiDispatchPriority
{
    Input = 0,
    Send = 1
}

public interface IUiDispatcher
{
    bool IsShuttingDown { get; }

    void Post(Action action, UiDispatchPriority priority);
}
