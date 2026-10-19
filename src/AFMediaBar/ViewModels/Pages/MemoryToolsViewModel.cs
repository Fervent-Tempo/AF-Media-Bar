// 应用页可见时才拥有采样循环；关闭页面取消呈现，不中断已经开始的回收。
using System.Globalization;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>内存诊断与手动操作的页面状态。</summary>
public sealed partial class MemoryToolsViewModel : ObservableObject, IDisposable
{
    private readonly MemoryToolsService _metrics;
    private readonly MemoryPruneCoordinator _prune;
    private readonly SystemMemoryCleanupService _systemCleanup;
    private SystemMemoryCleanupResult? _systemResult;
    private CancellationTokenSource? _sampling;
    private bool _visible;
    private bool _disposed;
    private MemoryToolsSnapshot? _snapshot;
    private MemoryTrimOutcome? _lastOutcome;
    private DateTimeOffset? _lastTrimAt;
    private bool _isBusy;
    public bool IsSystemCleanupEnabled => IsEnabled && SettingsManager.Current.Experiments.SystemMemoryCleanup;
    public string SystemResultText => _systemResult is not { } result ? string.Empty :
        Translations.Get("Memory.System." + result.Status) + (result.Before is not null && result.After is not null ? "\n" +
        Translations.Format("Memory.System.Readings", FormatBytes((double?)result.Before), FormatBytes((double?)result.After)) : string.Empty);
    public bool IsEnabled => SettingsManager.Current.Experiments.MemoryTools;
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanRun)); } }
    public bool CanRun => !IsBusy;
    public string MetricsText => _snapshot is not { } snapshot ? Translations.Get("Memory.Unavailable") :
        Translations.Format("Memory.Metrics", FormatBytes(snapshot.Process?.WorkingSetBytes), FormatBytes(snapshot.Process?.PrivateBytes),
            FormatBytes(snapshot.Process?.ManagedHeapBytes), snapshot.Process?.HandleCount.ToString() ?? Translations.Get("Memory.Unavailable"),
            snapshot.Process?.ThreadCount.ToString() ?? Translations.Get("Memory.Unavailable"), FormatBytes((double?)snapshot.AvailableBytes),
            snapshot.At.ToLocalTime().ToString("T", CultureInfo.CurrentCulture));
    public string ResultText => _lastOutcome is not { } outcome ? string.Empty :
        outcome.Counters is { } counters ? Translations.Format("Memory.Result", _lastTrimAt?.ToString("T"),
            Translations.Get(outcome.Strength == MemoryTrimStrength.Deep ? "Memory.Deep" : "Memory.Gentle"),
            FormatBytes(counters.Before.WorkingSetBytes), FormatBytes(counters.After.WorkingSetBytes),
            FormatBytes(counters.Before.PrivateBytes), FormatBytes(counters.After.PrivateBytes),
            FormatBytes(counters.Before.ManagedHeapBytes), FormatBytes(counters.After.ManagedHeapBytes),
            outcome.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.CurrentCulture),
            Translations.Get(counters.ReturnedWorkingSet ? "Memory.WorkingSetReturned" : "Memory.WorkingSetNotReturned")) :
        Translations.Get("Memory.Status." + outcome.Status);

    public MemoryToolsViewModel(MemoryToolsService metrics, MemoryPruneCoordinator prune, SystemMemoryCleanupService systemCleanup)
    {
        _systemCleanup = systemCleanup;
        _metrics = metrics;
        _prune = prune;
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    public void SetVisible(bool visible)
    {
        if (_disposed) return;
        _visible = visible;
        RestartSampling();
    }

    private void RestartSampling()
    {
        _sampling?.Cancel();
        _sampling = null;
        if (!_visible || !IsEnabled || _disposed) return;
        var source = new CancellationTokenSource();
        _sampling = source;
        _ = SampleAsync(source);
    }

    private async Task SampleAsync(CancellationTokenSource source)
    {
        try
        {
            do
            {
                var snapshot = await Task.Run(_metrics.Capture, source.Token);
                if (source.IsCancellationRequested || _disposed || !ReferenceEquals(_sampling, source)) return;
                _snapshot = snapshot;
                OnPropertyChanged(nameof(MetricsText));
                OnPropertyChanged(nameof(ResultText));
                await Task.Delay(TimeSpan.FromSeconds(2), source.Token);
            } while (!source.IsCancellationRequested);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_disposed && ReferenceEquals(_sampling, source)) { _snapshot = null; OnPropertyChanged(nameof(MetricsText)); } }
        finally { if (ReferenceEquals(_sampling, source)) _sampling = null; source.Dispose(); }
    }

    [RelayCommand]
    private async Task CleanSystemAsync()
    {
        if (_disposed || IsBusy || !IsSystemCleanupEnabled) return;
        IsBusy = true;
        var source = _sampling;
        try
        {
            var result = await _systemCleanup.RunAsync();
            if (_disposed || !_visible || source != _sampling || !IsSystemCleanupEnabled) return;
            _systemResult = result;
            OnPropertyChanged(nameof(SystemResultText));
        }
        finally { if (!_disposed) IsBusy = false; }
    }

    [RelayCommand]
    private Task GentleAsync() => TrimAsync(MemoryTrimStrength.Gentle);
    [RelayCommand]
    private Task DeepAsync() => TrimAsync(MemoryTrimStrength.Deep);

    private async Task TrimAsync(MemoryTrimStrength strength)
    {
        if (_disposed || IsBusy) return;
        IsBusy = true;
        var source = _sampling;
        try
        {
            var outcome = await _prune.RequestTrimAsync(strength);
            if (_disposed || !_visible || source != _sampling) return;
            _lastOutcome = outcome;
            _lastTrimAt = DateTimeOffset.Now;
            OnPropertyChanged(nameof(ResultText));
        }
        finally { if (!_disposed) IsBusy = false; }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_disposed || args.PropertyName is not (null or nameof(AppSettings.Experiments))) return;
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsSystemCleanupEnabled));
        RestartSampling();
    }

    private static string FormatBytes(double? bytes) => bytes is { } value ?
        (value / 1048576).ToString("0.0", CultureInfo.CurrentCulture) + " MB" : Translations.Get("Memory.Unavailable");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sampling?.Cancel();
        _sampling = null;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
    }
}
