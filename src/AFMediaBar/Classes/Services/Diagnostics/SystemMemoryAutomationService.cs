// 应用级低频调度器拥有计时器和订阅；锁屏/睡眠重建基线，退出后不再调度系统任务。
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Settings;
using Microsoft.Extensions.Hosting;

namespace AFMediaBar.Classes.Services;

/// <summary>定时与持续阈值共用清理入口，冷却、回差及失败暂停防止重复清理。</summary>
public sealed class SystemMemoryAutomationService(SystemMemoryCleanupService cleanup, PowerStateMonitor power) : IHostedService, IDisposable
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher) { Interval = TimeSpan.FromSeconds(10) };
    private long _intervalStart;
    private long? _highSince;
    private bool _thresholdArmed = true;
    private bool _ticking;
    private bool _disposed;
    private bool _started;
    private int _generation;
    private int _failures;
    public string StatusKey { get; private set; } = "Memory.Auto.Disabled";
    public event EventHandler? Changed;
    public Task StartAsync(CancellationToken cancellationToken) => _timer.Dispatcher.InvokeAsync(() =>
    {
        if (_started || _disposed) return;
        _started = true;
        _timer.Tick += OnTick;
        power.StateChanged += OnPowerChanged;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        Reconfigure();
    }, DispatcherPriority.Background, cancellationToken).Task;
    public void Resume() { if (!_disposed) Reconfigure(); }
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (args.PropertyName is null or nameof(AppSettings.SystemMemoryOptimization) or nameof(AppSettings.Experiments)) Reconfigure();
    }
    private bool Enabled => SettingsManager.Current.Experiments.MemoryTools && SettingsManager.Current.Experiments.SystemMemoryCleanup &&
        (SettingsManager.Current.SystemMemoryOptimization.TimerEnabled || SettingsManager.Current.SystemMemoryOptimization.ThresholdEnabled);
    private bool Suspended => power.IsSuspended || power.IsSessionLocked;
    private void OnPowerChanged(object? sender, EventArgs args) => Reconfigure();
    private void Reconfigure()
    {
        if (_disposed) return;
        _generation++;
        _timer.Stop();
        _highSince = null; _thresholdArmed = true; _intervalStart = Stopwatch.GetTimestamp(); _failures = 0;
        SetStatus(!Enabled ? "Memory.Auto.Disabled" : Suspended ? "Memory.Auto.Suspended" : "Memory.Auto.Waiting");
        if (!_disposed && Enabled && !Suspended) _timer.Start();
    }
    private async void OnTick(object? sender, EventArgs args)
    {
        if (_disposed || _ticking || !Enabled || Suspended) return;
        _ticking = true;
        var generation = _generation;
        try
        {
            var settings = SettingsManager.Current.SystemMemoryOptimization;
            var memory = settings.ThresholdEnabled ? await Task.Run(ReadLoad) : null;
            if (_disposed || generation != _generation || !Enabled || Suspended) return;
            var now = Stopwatch.GetTimestamp();
            if (memory is { } load && load <= settings.ThresholdPercent - 5) _thresholdArmed = true;
            if (memory is { } percent && percent >= settings.ThresholdPercent && _thresholdArmed) _highSince ??= now;
            else _highSince = null;
            var intervalDue = settings.TimerEnabled && Stopwatch.GetElapsedTime(_intervalStart, now) >= TimeSpan.FromMinutes(settings.IntervalMinutes);
            var thresholdDue = settings.ThresholdEnabled && _highSince is { } high && Stopwatch.GetElapsedTime(high, now) >= TimeSpan.FromSeconds(30);
            if ((!intervalDue && !thresholdDue) || cleanup.IsRunning ||
                cleanup.LastAttemptTimestamp is { } last && Stopwatch.GetElapsedTime(last, now) < TimeSpan.FromMinutes(15)) return;
            SetStatus("Memory.Auto.Running");
            var result = await cleanup.RunAsync(settings.Level, requireAuthorization: true);
            if (_disposed || generation != _generation) return;
            _intervalStart = Stopwatch.GetTimestamp();
            if (thresholdDue) { _thresholdArmed = false; _highSince = null; }
            _failures = result.Status == SystemMemoryCleanupStatus.Completed ? 0 : _failures + 1;
            var pause = result.Status is SystemMemoryCleanupStatus.AuthorizationRequired or SystemMemoryCleanupStatus.PermissionDenied or SystemMemoryCleanupStatus.Unsupported || _failures >= 3;
            if (pause) { _timer.Stop(); SetStatus("Memory.Auto.Paused"); }
            else SetStatus("Memory.Auto.Waiting");
        }
        catch (Exception) { if (!_disposed && generation == _generation) { _timer.Stop(); SetStatus("Memory.Auto.Paused"); } }
        finally { _ticking = false; }
    }
    private static uint? ReadLoad()
    {
        var status = new NativeMethods.MemoryStatusEx { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
        return NativeMethods.GlobalMemoryStatusEx(ref status) ? status.MemoryLoad : null;
    }
    private void SetStatus(string key) { StatusKey = key; Changed?.Invoke(this, EventArgs.Empty); }
    public Task StopAsync(CancellationToken cancellationToken) => _timer.Dispatcher.InvokeAsync(Dispose, DispatcherPriority.Send, cancellationToken).Task;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _generation++; _timer.Stop(); _timer.Tick -= OnTick;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        power.StateChanged -= OnPowerChanged;
    }
}
