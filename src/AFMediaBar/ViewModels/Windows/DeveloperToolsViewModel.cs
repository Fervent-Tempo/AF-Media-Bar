// 协调工具窗口的固定命令、忙碌与有界结果；关闭时取消请求并解除订阅。
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Diagnostics;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>可用条件与说明均来自同一动作目录的绑定项。</summary>
public sealed partial class DeveloperActionItem(DeveloperAction action) : ObservableObject
{
    public DeveloperAction Action { get; } = action;
    public string Title => Action.Title;
    public string Description => Action.Description;
    public string Command => Action.Command;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private string _unavailableReason = string.Empty;
    internal void RefreshLanguage() => OnPropertyChanged(string.Empty);
}

/// <summary>开发者窗口的一次会话；服务由 App 拥有，视图模型只取消自身请求。</summary>
public sealed partial class DeveloperToolsViewModel : ObservableObject, IDisposable
{
    private readonly IDeveloperModeService _mode;
    private readonly IDeveloperScenarioService _scenarios;
    private readonly LocalizationService _localization;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Queue<string> _records = new();
    private bool _disposed;
    private int _requestGeneration;

    [ObservableProperty] private string _commandText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private DeveloperLyricsHostState? _selectedHost;
    [ObservableProperty] private string _output = string.Empty;
    public ObservableCollection<DeveloperLyricsHostState> Hosts { get; } = [];
    public ObservableCollection<DeveloperActionItem> Actions { get; } = new(DeveloperActionCatalog.Actions.Select(action => new DeveloperActionItem(action)));
    public ObservableCollection<DeveloperActionItem> Suggestions { get; } = [];
    public IEnumerable<DeveloperActionItem> PreviewActions => Actions.Where(item => item.Action.Impact == DeveloperActionImpact.Preview);
    public IEnumerable<DeveloperActionItem> SessionActions => Actions.Where(item => item.Action.Impact == DeveloperActionImpact.Session);
    public IEnumerable<DeveloperActionItem> DiagnosticActions => Actions.Where(item => item.Action.Impact == DeveloperActionImpact.Diagnostic);
    public bool CanRunCommands => !_disposed && _mode.IsEnabled && !IsBusy;
    public event Action<string>? CopyRequested;

    public DeveloperToolsViewModel(IDeveloperModeService mode, IDeveloperScenarioService scenarios, LocalizationService localization)
    {
        _mode = mode; _scenarios = scenarios; _localization = localization;
        _mode.EnabledChanged += OnModeChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        _scenarios.ResultObserved += OnResultObserved;
        try { RefreshHosts(); RefreshSuggestions(); }
        catch { Dispose(); throw; }
    }

    partial void OnCommandTextChanged(string value) => RefreshSuggestions();
    partial void OnSelectedHostChanged(DeveloperLyricsHostState? value) => RefreshAvailability();
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunCommands));
        RefreshAvailability();
    }

    private void RefreshSuggestions()
    {
        if (_disposed) return;
        Suggestions.Clear();
        foreach (var action in DeveloperActionCatalog.Suggest(CommandText))
            Suggestions.Add(Actions.First(item => ReferenceEquals(item.Action, action)));
    }

    [RelayCommand]
    private void SelectSuggestion(DeveloperActionItem? item)
    {
        if (!_disposed && item is not null) CommandText = item.Command;
    }

    [RelayCommand]
    private Task ExecuteCommandAsync() => ExecuteAsync(DeveloperActionCatalog.Find(CommandText));

    [RelayCommand]
    private Task ExecuteActionAsync(DeveloperActionItem? item) => ExecuteAsync(item?.Action);

    private async Task ExecuteAsync(DeveloperAction? action)
    {
        if (_disposed || !_mode.IsEnabled || IsBusy) return;
        if (action is null) { AddResult(Translations.Get("Developer.UnknownCommand"), new(DeveloperActionStatus.Unavailable)); return; }
        var generation = ++_requestGeneration;
        var modeGeneration = _mode.Generation;
        var host = SelectedHost?.HostId;
        IsBusy = true;
        try
        {
            var result = await _scenarios.ExecuteAsync(action.Command, host, _lifetime.Token);
            if (_disposed || !_mode.IsEnabled || generation != _requestGeneration || modeGeneration != _mode.Generation) return;
            AddResult(action.Command, result);
            RefreshHosts();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_disposed && _mode.IsEnabled && modeGeneration == _mode.Generation)
                AddResult(action.Command, new(DeveloperActionStatus.Failed));
        }
        finally { if (!_disposed) IsBusy = false; }
    }

    [RelayCommand]
    private void RefreshHosts()
    {
        if (_disposed || !_mode.IsEnabled) return;
        var selected = SelectedHost?.HostId;
        var hosts = _scenarios.GetLyricsHosts();
        Hosts.Clear();
        foreach (var host in hosts) Hosts.Add(host);
        SelectedHost = Hosts.FirstOrDefault(host => host.HostId == selected) ?? Hosts.FirstOrDefault(host => host.HasRenderer && !host.Disabled) ?? Hosts.FirstOrDefault();
        RefreshAvailability();
    }

    private void RefreshAvailability()
    {
        foreach (var item in Actions)
        {
            item.IsAvailable = !_disposed && _mode.IsEnabled && !IsBusy &&
                (!item.Action.RequiresRenderer || SelectedHost is { Loaded: true, HasRenderer: true, Disabled: false });
            item.UnavailableReason = item.IsAvailable ? string.Empty : Translations.Get(IsBusy ? "Developer.Result.Busy" : "Developer.NoRenderer");
        }
    }

    private void AddResult(string command, DeveloperActionResult result)
    {
        _records.Enqueue($"{DateTime.Now:HH:mm:ss} [{result.StatusText}] {command}" +
            (string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : Environment.NewLine + result.Detail));
        while (_records.Count > 100) _records.Dequeue();
        Output = string.Join(Environment.NewLine + Environment.NewLine, _records);
    }

    private void OnResultObserved(string command, DeveloperActionResult result)
    {
        if (!_disposed && _mode.IsEnabled) AddResult(command, result);
    }

    [RelayCommand]
    private void CopyOutput()
    {
        if (!_disposed) CopyRequested?.Invoke(Output);
    }

    public void ReportClipboardFailure() => AddResult("copy", new(DeveloperActionStatus.Failed));

    private void OnModeChanged(object? sender, EventArgs e)
    {
        if (!_mode.IsEnabled) _lifetime.Cancel();
        OnPropertyChanged(nameof(CanRunCommands));
        RefreshAvailability();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in Actions) item.RefreshLanguage();
        RefreshAvailability(); RefreshSuggestions();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _requestGeneration++;
        _lifetime.Cancel();
        try { _scenarios.ClosePreviews(); }
        finally
        {
            _mode.EnabledChanged -= OnModeChanged;
            _localization.LanguageChanged -= OnLanguageChanged;
            _scenarios.ResultObserved -= OnResultObserved;
            _lifetime.Dispose();
        }
    }
}
