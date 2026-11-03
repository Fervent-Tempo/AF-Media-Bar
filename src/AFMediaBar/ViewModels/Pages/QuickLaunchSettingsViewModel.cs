// 小音符参数与列表编辑；不读取文件或图标，页面作用域结束时释放订阅。
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using AFMediaBar.Classes.Utils;
using System.Windows.Threading;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>可加入快速启动的来源快照，不改变媒体允许列表。</summary>
public sealed record QuickLaunchSourceItem(MediaSourceDescriptor Descriptor, bool IsAdded)
{
    public bool CanAdd => Descriptor.CanQuickLaunch && !IsAdded;
    public string ActionText => Translations.Get(IsAdded ? "Media.QuickLaunch.Added" : "Media.SourceList.AddToQuickLaunch");
}

/// <summary>全局小音符交互与启动项的作用域编辑器。</summary>
public sealed partial class QuickLaunchSettingsViewModel : ObservableObject, IDisposable
{
    private readonly QuickLaunchService _quickLaunch;
    private readonly ISettingsConfiguration _configuration;
    private readonly LocalizationService _localization;
    private readonly IMediaSourceCatalog _mediaSessions;
    private readonly Dispatcher _dispatcher = DispatcherHelper.Current;
    private bool _disposed;
    private bool _refreshing;
    private string? _statusKey;
    public ObservableCollection<QuickLaunchEntry> QuickLaunchEntries { get; } = [];
    public ObservableCollection<QuickLaunchSourceItem> AvailableSources { get; } = [];
    public string QuickLaunchEntryCountText => Translations.Format("Media.QuickLaunch.EntryCount", QuickLaunchEntries.Count);
    public string StatusText => _statusKey is null ? string.Empty : Translations.Get(_statusKey);
    public bool IsSelectThenLaunch => InteractionMode == QuickLaunchInteractionMode.SelectThenLaunch;
    public QuickLaunchInteractionMode InteractionMode
    {
        get => _quickLaunch.Settings.InteractionMode;
        set { if (CanEdit) SettingsManager.SetQuickLaunchSettings(_quickLaunch.Settings with { InteractionMode = value }); }
    }
    public bool ShowApplicationIconOnHover
    {
        get => _quickLaunch.Settings.ShowApplicationIconOnHover;
        set { if (CanEdit) SettingsManager.SetQuickLaunchSettings(_quickLaunch.Settings with { ShowApplicationIconOnHover = value }); }
    }
    public bool ShowTooltips
    {
        get => _quickLaunch.Settings.ShowTooltips;
        set { if (CanEdit) SettingsManager.SetQuickLaunchSettings(_quickLaunch.Settings with { ShowTooltips = value }); }
    }
    private bool CanEdit => !_disposed && !_refreshing && _configuration.IsActive;
    public QuickLaunchSettingsViewModel(QuickLaunchService quickLaunch, ISettingsConfiguration configuration, LocalizationService localization,
        IMediaSourceCatalog mediaSessions)
    {
        _quickLaunch = quickLaunch; _configuration = configuration; _localization = localization;
        _mediaSessions = mediaSessions;
        _mediaSessions.DiscoveredSourcesChanged += SourcesChanged;
        quickLaunch.Changed += Changed;
        localization.LanguageChanged += LanguageChanged;
        Refresh();
    }
    public async Task AddFileAsync(string path)
    {
        if (!CanEdit) return;
        var token = _configuration.CancellationToken;
        try
        {
            var status = await _quickLaunch.AddFileAsync(path, null, token);
            if (_disposed || token.IsCancellationRequested || !_configuration.IsActive) return;
            _statusKey = status;
            OnPropertyChanged(nameof(StatusText));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            if (_disposed || token.IsCancellationRequested || !_configuration.IsActive) return;
            _statusKey = "Media.Status.OnlyExistingFiles";
            OnPropertyChanged(nameof(StatusText));
        }
    }
    [RelayCommand] private void RemoveQuickLaunch(QuickLaunchEntry? entry)
    {
        if (CanEdit && entry is not null) _quickLaunch.SetEntries(QuickLaunchEntries.Where(item => item.Id != entry.Id));
    }
    [RelayCommand]
    private void AddSource(QuickLaunchSourceItem? item)
    {
        if (!CanEdit || item is not { CanAdd: true } || item.Descriptor.LaunchKind is not { } kind || item.Descriptor.LaunchTarget is not { } target) return;
        _statusKey = _quickLaunch.Add(new(Guid.NewGuid().ToString("N"), item.Descriptor.DisplayName, kind, target, item.Descriptor.SourceId))
            ? "Media.Status.AddedToQuickLaunch" : "Media.Status.AlreadyInQuickLaunch";
        OnPropertyChanged(nameof(StatusText));
    }
    [RelayCommand] private void ReorderQuickLaunch(SettingsReorderRequest? request)
    {
        if (!CanEdit || request is null || !request.TryResolve(QuickLaunchEntries, entry => entry.Id, out var index, out var target)) return;
        var entries = QuickLaunchEntries.ToList();
        var entry = entries[index]; entries.RemoveAt(index); entries.Insert(target, entry);
        _quickLaunch.SetEntries(entries);
    }
    private void Changed(object? sender, QuickLaunchChangedEventArgs args) => Refresh();
    private void LanguageChanged(object? sender, EventArgs args) { if (!_disposed) { RefreshSources(); OnPropertyChanged(string.Empty); } }
    private void SourcesChanged(IReadOnlyList<MediaSourceDescriptor> sources) => DispatcherHelper.Run(_dispatcher, () =>
    {
        if (!_disposed) RefreshSources();
    });
    private void RefreshSources()
    {
        AvailableSources.Clear();
        foreach (var source in _mediaSessions.CurrentDiscoveredSources)
        {
            var added = QuickLaunchEntries.Any(entry => string.Equals(entry.SourceId, source.SourceId, StringComparison.OrdinalIgnoreCase) ||
                entry.Kind == source.LaunchKind && string.Equals(entry.Target, source.LaunchTarget, StringComparison.OrdinalIgnoreCase));
            AvailableSources.Add(new(source, added));
        }
    }
    private void Refresh()
    {
        if (_disposed) return;
        _refreshing = true;
        try
        {
            if (!QuickLaunchEntries.SequenceEqual(_quickLaunch.Settings.Entries ?? []))
            {
                QuickLaunchEntries.Clear();
                foreach (var entry in _quickLaunch.Settings.Entries ?? []) QuickLaunchEntries.Add(entry);
            }
            RefreshSources();
            OnPropertyChanged(string.Empty);
        }
        finally { _refreshing = false; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _quickLaunch.Changed -= Changed;
        _localization.LanguageChanged -= LanguageChanged;
        _mediaSessions.DiscoveredSourcesChanged -= SourcesChanged;
    }
}
