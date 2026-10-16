// Owns one settings-window session's background refresh and cancellation; its DI scope disposes the session on close.
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>Publishes the current runtime context and an editor-only monitor selection on the UI thread.</summary>
public sealed class SettingsContextService : IDisposable
{
    private readonly ISettingsEnvironmentReader _reader;
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly DispatcherTimer _timer;
    private int _generation;
    private bool _started;
    private bool _disposed;
    private string? _preferredDeviceId;
    private int _resourcesDisposed;
    private readonly BarRuntimeCoordinator? _runtime;
    private bool _refreshPending;
    private SettingsEnvironmentSnapshot? _snapshot;

    /// <summary>Creates a window-scoped context session; the reader owns platform access.</summary>
    public SettingsContextService(ISettingsEnvironmentReader reader, BarRuntimeCoordinator? runtime = null)
    {
        _reader = reader;
        _runtime = runtime;
        if (_runtime is not null) _runtime.Changed += OnRuntimeChanged;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>Cancels window-owned operations, including open reset dialogs, on close.</summary>
    public CancellationToken CancellationToken => _lifetime.Token;

    /// <summary>Current immutable editing context; runtime orientation is never written by monitor selection.</summary>
    public SettingsContext Current { get; private set; } = SettingsContext.Initial;
    /// <summary>Enabled displays currently available to the editor.</summary>
    public IReadOnlyList<SettingsMonitorContext> Monitors { get; private set; } = [];
    /// <summary>Allows the window to commit drafts before replacing their context.</summary>
    public event EventHandler? Changing;
    /// <summary>Raised after current context or monitor choices change.</summary>
    public event EventHandler? Changed;
    /// <summary>Reports observed background failures without replacing a valid snapshot.</summary>
    public event EventHandler<Exception>? RefreshFailed;

    /// <summary>Starts refresh for a visible settings session. Repeated calls do not create another timer.</summary>
    public async Task StartAsync()
    {
        if (_disposed || _started) return;
        _started = true;
        await RefreshAsync();
        if (!_disposed) _timer.Start();
    }

    /// <summary>Refreshes asynchronously, dropping stale results and never blocking platform work on the UI thread.</summary>
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        if (!await _refreshGate.WaitAsync(0)) { _refreshPending = true; return; }
        var generation = _generation;
        try
        {
            var snapshot = await _reader.ReadAsync(_lifetime.Token);
            if (_disposed || generation != _generation) return;
            await _dispatcher.InvokeAsync(() => { if (!_disposed && generation == _generation) Apply(snapshot); });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_disposed) await _dispatcher.InvokeAsync(() => { if (!_disposed) RefreshFailed?.Invoke(this, exception); });
        }
        finally
        {
            _refreshGate.Release();
            DisposeResourcesIfIdle();
            if (_refreshPending && !_disposed)
            {
                _refreshPending = false;
                _ = _dispatcher.BeginInvoke(new Action(() => OnTick(this, EventArgs.Empty)));
            }
        }
    }

    /// <summary>Changes only the display being edited, not configured runtime targets or taskbar orientation.</summary>
    public void SelectMonitor(string? deviceId)
    {
        _dispatcher.VerifyAccess();
        if (_disposed || !Monitors.Any(value => string.Equals(value.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))) return;
        _preferredDeviceId = deviceId;
        Apply(_snapshot ?? new(Current.Mode, Monitors));
    }

    private async void OnRuntimeChanged(object? sender, EventArgs args)
    {
        if (_disposed || _runtime is null || _runtime.IsSwitching) return;
        if (!_dispatcher.CheckAccess())
        {
            _ = _dispatcher.BeginInvoke(new Action(() => OnRuntimeChanged(sender, args)));
            return;
        }
        _generation++;
        var mode = _runtime.RunningMode == AFMediaBar.Classes.Models.Layout.WindowMode.FloatingBar ? SettingsMode.FloatingBar : SettingsMode.Taskbar;
        if (mode != Current.Mode) Apply(new(mode, []));
        await RefreshAsync();
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_disposed || args.PropertyName is not (null or nameof(AppSettings.Experiments) or nameof(AppSettings.FloatingBar))) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(new Action(() => OnSettingsChanged(sender, args)));
            return;
        }
        var previous = Current;
        Apply(_snapshot ?? new(SettingsMode.Taskbar, Monitors));
        if (previous == Current) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(SettingsEnvironmentSnapshot snapshot)
    {
        _snapshot = snapshot;
        var monitor = snapshot.Monitors.FirstOrDefault(value => string.Equals(value.DeviceId, _preferredDeviceId, StringComparison.OrdinalIgnoreCase))
            ?? snapshot.Monitors.FirstOrDefault(value => value.IsPrimary) ?? snapshot.Monitors.FirstOrDefault();
        var mode = snapshot.RunningMode;
        var next = new SettingsContext(mode, monitor?.DeviceId,
            mode == SettingsMode.FloatingBar ? SettingsManager.Current.FloatingBar.Orientation : monitor?.Orientation,
            monitor?.IsCompact ?? false, monitor?.EnvironmentAvailable ?? false);
        var choicesChanged = !Monitors.SequenceEqual(snapshot.Monitors);
        if (next == Current && !choicesChanged) return;
        if (next != Current) Changing?.Invoke(this, EventArgs.Empty);
        Monitors = snapshot.Monitors;
        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async void OnTick(object? sender, EventArgs args) => await RefreshAsync();

    /// <summary>Stops refresh and rejects late publications. No platform handle or settings mutation is retained.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generation++;
        if (_runtime is not null) _runtime.Changed -= OnRuntimeChanged;
        _timer.Stop();
        _timer.Tick -= OnTick;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _lifetime.Cancel();
        DisposeResourcesIfIdle();
    }

    private void DisposeResourcesIfIdle()
    {
        if (!_disposed || _refreshGate.CurrentCount == 0 || Interlocked.Exchange(ref _resourcesDisposed, 1) != 0) return;
        _lifetime.Dispose();
        _refreshGate.Dispose();
    }
}
