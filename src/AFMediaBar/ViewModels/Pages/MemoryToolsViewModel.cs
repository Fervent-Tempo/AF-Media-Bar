// 应用页可见时才拥有采样循环；关闭页面取消呈现，不中断已经开始的回收。
using System.Globalization;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>内存诊断与手动操作的页面状态。</summary>
public sealed partial class MemoryToolsViewModel : ObservableObject, IDisposable
{
    private readonly IMemoryAuthorizationPrompt _authorizationPrompt;
    private readonly SystemMemoryAutomationService _automation;
    private bool _configuringAutomation;
    private readonly MemoryToolsService _metrics;
    public string AuthorizationText => Translations.Get("Memory.Authorization." + _systemCleanup.Authorization);
    public string AutomaticStatusText => Translations.Get(_automation.StatusKey);
    public bool TimerEnabled { get => SettingsManager.Current.SystemMemoryOptimization.TimerEnabled; set => _ = SetAutomaticAsync(true, value); }
    public bool ThresholdEnabled { get => SettingsManager.Current.SystemMemoryOptimization.ThresholdEnabled; set => _ = SetAutomaticAsync(false, value); }
    public int IntervalMinutes { get => SettingsManager.Current.SystemMemoryOptimization.IntervalMinutes; set => UpdateAutomatic(SettingsManager.Current.SystemMemoryOptimization with { IntervalMinutes = value }); }
    public int ThresholdPercent { get => SettingsManager.Current.SystemMemoryOptimization.ThresholdPercent; set => UpdateAutomatic(SettingsManager.Current.SystemMemoryOptimization with { ThresholdPercent = value }); }
    public SystemMemoryOptimizationLevel AutomaticLevel { get => SettingsManager.Current.SystemMemoryOptimization.Level; set => UpdateAutomatic(SettingsManager.Current.SystemMemoryOptimization with { Level = value }); }
    private void UpdateAutomatic(SystemMemoryOptimizationSettings value)
    { if (!_disposed) SettingsManager.Current.SystemMemoryOptimization = value.Normalize(); }
    private async Task SetAutomaticAsync(bool timer, bool enabled)
    {
        if (_disposed || _configuringAutomation) { OnPropertyChanged(string.Empty); return; }
        _configuringAutomation = true;
        try
        {
            if (enabled && await _systemCleanup.RefreshAuthorizationAsync() != MemoryCleanupAuthorization.Ready)
            {
                if (await _authorizationPrompt.ChooseAsync(true) != MemoryAuthorizationChoice.Remember || _disposed) return;
                var result = await _systemCleanup.ManageAuthorizationAsync(true);
                if (_disposed || result.Status != SystemMemoryCleanupStatus.Completed || _systemCleanup.Authorization != MemoryCleanupAuthorization.Ready) return;
            }
            var settings = SettingsManager.Current.SystemMemoryOptimization;
            if (!_disposed) UpdateAutomatic(timer ? settings with { TimerEnabled = enabled } : settings with { ThresholdEnabled = enabled });
        }
        catch (Exception) { }
        finally { _configuringAutomation = false; if (!_disposed) OnPropertyChanged(string.Empty); }
    }
    [RelayCommand] private async Task AuthorizeAsync()
    {
        if (_disposed || !CanRun) return;
        _systemResult = await _systemCleanup.ManageAuthorizationAsync(true);
        if (!_disposed) OnPropertyChanged(string.Empty);
    }
    [RelayCommand] private async Task RevokeAuthorizationAsync()
    {
        if (_disposed || !CanRun) return;
        UpdateAutomatic(SettingsManager.Current.SystemMemoryOptimization with { TimerEnabled = false, ThresholdEnabled = false });
        _systemResult = await _systemCleanup.ManageAuthorizationAsync(false);
        if (!_disposed) OnPropertyChanged(string.Empty);
    }
    [RelayCommand] private void ResumeAutomatic() => _automation.Resume();
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
        Translations.Get("Memory.System." + result.Status) +
        (result.Details is { } details ? "\n" + string.Join("\n", details.Stages.Select(stage => Translations.Get("Memory.Stage." + stage.Operation) + ": " + Translations.Get("Memory.System." + stage.Status))) + " · " + details.ElapsedMilliseconds + " ms" : string.Empty) + (result.Before is not null && result.After is not null ? "\n" +
        Translations.Format("Memory.System.Readings", FormatBytes((double?)result.Before), FormatBytes((double?)result.After)) : string.Empty);
    public bool IsLegacyTrimVisible => !IsEnabled;
    public bool HasResult => _lastOutcome is not null;
    public bool HasSystemResult => _systemResult is not null;
    public string WorkingSetText => FormatBytes(_snapshot?.Process?.WorkingSetBytes);
    public string PrivateCommitText => FormatBytes(_snapshot?.Process?.PrivateBytes);
    public string ManagedHeapText => FormatBytes(_snapshot?.Process?.ManagedHeapBytes);
    public string HandlesText => _snapshot?.Process?.HandleCount.ToString() ?? Translations.Get("Memory.Unavailable");
    public string ThreadsText => _snapshot?.Process?.ThreadCount.ToString() ?? Translations.Get("Memory.Unavailable");
    public string AvailableMemoryText => FormatBytes((double?)_snapshot?.AvailableBytes);
    public string SampleTimeText => _snapshot?.At.ToLocalTime().ToString("T", CultureInfo.CurrentCulture) ?? Translations.Get("Memory.Unavailable");
    public bool IsEnabled => SettingsManager.Current.Experiments.MemoryTools;
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanRun)); } }
    public bool CanRun => !IsBusy && !_systemCleanup.IsRunning;
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

    public MemoryToolsViewModel(MemoryToolsService metrics, MemoryPruneCoordinator prune, SystemMemoryCleanupService systemCleanup, SystemMemoryAutomationService automation, IMemoryAuthorizationPrompt authorizationPrompt)
    {
        _authorizationPrompt = authorizationPrompt;
        _automation = automation;
        automation.Changed += OnCleanupChanged;
        _systemCleanup = systemCleanup;
        systemCleanup.Changed += OnCleanupChanged;
        _metrics = metrics;
        _prune = prune;
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    public void SetVisible(bool visible)
    {
        if (_disposed) return;
        _visible = visible;
        RestartSampling();
        if (visible) _ = RefreshAuthorizationAsync();
    }

    private async Task RefreshAuthorizationAsync()
    { try { await _systemCleanup.RefreshAuthorizationAsync(); } catch (Exception) { } }

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
                RefreshMetrics();
                OnPropertyChanged(nameof(ResultText));
                await Task.Delay(TimeSpan.FromSeconds(2), source.Token);
            } while (!source.IsCancellationRequested);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_disposed && ReferenceEquals(_sampling, source)) { _snapshot = null; RefreshMetrics(); } }
        finally { if (ReferenceEquals(_sampling, source)) _sampling = null; source.Dispose(); }
    }

    private void RefreshMetrics()
    {
        OnPropertyChanged(nameof(MetricsText));
        OnPropertyChanged(nameof(WorkingSetText)); OnPropertyChanged(nameof(PrivateCommitText));
        OnPropertyChanged(nameof(ManagedHeapText)); OnPropertyChanged(nameof(HandlesText));
        OnPropertyChanged(nameof(ThreadsText)); OnPropertyChanged(nameof(AvailableMemoryText));
        OnPropertyChanged(nameof(SampleTimeText));
    }

    [RelayCommand]
    private async Task CleanSystemAsync() => await CleanSystemCoreAsync(SystemMemoryOptimizationLevel.Standard);

    [RelayCommand]
    private async Task CleanSystemDeepAsync() => await CleanSystemCoreAsync(SystemMemoryOptimizationLevel.Deep);

    private async Task CleanSystemCoreAsync(SystemMemoryOptimizationLevel level)
    {
        if (_disposed || IsBusy || !IsSystemCleanupEnabled) return;
        IsBusy = true;
        var source = _sampling;
        try
        {
            if (await _systemCleanup.RefreshAuthorizationAsync() != MemoryCleanupAuthorization.Ready)
            {
                var choice = await _authorizationPrompt.ChooseAsync(false);
                if (_disposed || choice == MemoryAuthorizationChoice.Cancel) return;
                if (choice == MemoryAuthorizationChoice.Remember)
                {
                    var authorization = await _systemCleanup.ManageAuthorizationAsync(true);
                    if (_disposed || authorization.Status != SystemMemoryCleanupStatus.Completed) { _systemResult = authorization; OnPropertyChanged(string.Empty); return; }
                }
            }
            var result = await _systemCleanup.RunAsync(level);
            if (_disposed || !_visible || source != _sampling || !IsSystemCleanupEnabled) return;
            _systemResult = result;
            OnPropertyChanged(nameof(SystemResultText));
            OnPropertyChanged(nameof(HasSystemResult));
        }
        finally { if (!_disposed) IsBusy = false; }
    }

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
            OnPropertyChanged(nameof(HasResult));
        }
        finally { if (!_disposed) IsBusy = false; }
    }

    private void OnCleanupChanged(object? sender, EventArgs args)
    {
        if (_disposed) return;
        _systemResult = _systemCleanup.LastResult;
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(AuthorizationText));
        OnPropertyChanged(nameof(AutomaticStatusText));
        OnPropertyChanged(nameof(SystemResultText));
        OnPropertyChanged(nameof(HasSystemResult));
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_disposed || args.PropertyName is not (null or nameof(AppSettings.Experiments) or nameof(AppSettings.SystemMemoryOptimization))) return;
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsLegacyTrimVisible));
        OnPropertyChanged(nameof(IsSystemCleanupEnabled));
        OnPropertyChanged(string.Empty);
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
        _systemCleanup.Changed -= OnCleanupChanged;
        _automation.Changed -= OnCleanupChanged;
    }
}
