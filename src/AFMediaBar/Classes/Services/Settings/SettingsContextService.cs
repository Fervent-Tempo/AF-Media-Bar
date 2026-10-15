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
    private SettingsMode? _editingMode;
    private SettingsEnvironmentSnapshot? _snapshot;

    /// <summary>Creates a window-scoped context session; the reader owns platform access.</summary>
    public SettingsContextService(ISettingsEnvironmentReader reader)
    {
        _reader = reader;
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
        if (_disposed || !await _refreshGate.WaitAsync(0)) return;
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
        finally { _refreshGate.Release(); DisposeResourcesIfIdle(); }
    }

    /// <summary>Changes only the display being edited, not configured runtime targets or taskbar orientation.</summary>
    public void SelectMonitor(string? deviceId)
    {
        _dispatcher.VerifyAccess();
        if (_disposed || !Monitors.Any(value => string.Equals(value.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))) return;
        _preferredDeviceId = deviceId;
        Apply(_snapshot ?? new(Current.Mode, Monitors));
    }

    /// <summary>只切换现有页面的配置目标，不创建宿主或修改运行模式。</summary>
    public void SelectConfigurationTarget(SettingsMode mode)
    {
        _dispatcher.VerifyAccess();
        if (_disposed || !SettingsPageCatalog.CanEditProfile(mode)) return;
        _editingMode = mode;
        Apply(_snapshot ?? new(SettingsMode.Taskbar, Monitors));
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_disposed || args.PropertyName is not (null or nameof(AppSettings.Experiments) or nameof(AppSettings.FloatingBar))) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(new Action(() => OnSettingsChanged(sender, args)));
            return;
        }
        if (_editingMode == SettingsMode.FloatingBar && !SettingsManager.Current.Experiments.FloatingBar)
            _editingMode = SettingsMode.Taskbar;
        var previous = Current;
        Apply(_snapshot ?? new(SettingsMode.Taskbar, Monitors));
        if (previous == Current) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(SettingsEnvironmentSnapshot snapshot)
    {
        _snapshot = snapshot;
        var monitor = snapshot.Monitors.FirstOrDefault(value => string.Equals(value.DeviceId, _preferredDeviceId, StringComparison.OrdinalIgnoreCase))
            ?? snapshot.Monitors.FirstOrDefault(value => value.IsPrimary) ?? snapshot.Monitors.FirstOrDefault();
        var mode = _editingMode ?? snapshot.RunningMode;
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
