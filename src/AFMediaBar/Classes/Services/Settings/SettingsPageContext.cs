// Owns one cached page-scope context and its activation cancellation; the page provider disposes its DI scope.
using AFMediaBar.Classes.Models.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>Separates the immutable context of a cached page from the window's changing environment.</summary>
public sealed class SettingsPageContext : IDisposable
{
    private CancellationTokenSource _activation = new();
    /// <summary>Context assigned before the page or view model is resolved.</summary>
    public SettingsContext Snapshot { get; private set; } = SettingsContext.Initial;
    /// <summary>Whether this cached scope is currently allowed to edit settings.</summary>
    public bool IsActive { get; private set; } = true;
    /// <summary>Cancellation for work belonging to this activation, including asynchronous font choices.</summary>
    public CancellationToken CancellationToken => _activation.Token;

    /// <summary>Signals reactivation so cached editors can refresh their values.</summary>
    public event EventHandler? Activated;

    /// <summary>Seeds a new scope with its permanent context.</summary>
    public void Initialize(SettingsContext context) => Snapshot = context;
    /// <summary>Activates an existing cached scope with a fresh cancellation token.</summary>
    public void Activate()
    {
        if (IsActive) return;
        _activation.Dispose();
        _activation = new();
        IsActive = true;
        Activated?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Stops edits and cancels work before another scope becomes active.</summary>
    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        _activation.Cancel();
    }
    /// <summary>Releases cancellation registrations after cached pages and view models are disposed.</summary>
    public void Dispose() { Deactivate(); _activation.Dispose(); }
}
