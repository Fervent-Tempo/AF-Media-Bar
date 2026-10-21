// 小音符参数与列表编辑；不读取文件或图标，页面作用域结束时释放订阅。
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>全局小音符交互与启动项的作用域编辑器。</summary>
public sealed partial class QuickLaunchSettingsViewModel : ObservableObject, IDisposable
{
    private readonly QuickLaunchService _quickLaunch;
    private readonly ISettingsConfiguration _configuration;
    private readonly LocalizationService _localization;
    private bool _disposed;
    private bool _refreshing;
    private string? _statusKey;
    public ObservableCollection<QuickLaunchEntry> QuickLaunchEntries { get; } = [];
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
    private bool CanEdit => !_disposed && !_refreshing && _configuration.IsActive;
    public QuickLaunchSettingsViewModel(QuickLaunchService quickLaunch, ISettingsConfiguration configuration, LocalizationService localization)
    {
        _quickLaunch = quickLaunch; _configuration = configuration; _localization = localization;
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
    [RelayCommand] private void ReorderQuickLaunch(SettingsReorderRequest? request)
    {
        if (!CanEdit || request is null || !request.TryResolve(QuickLaunchEntries, entry => entry.Id, out var index, out var target)) return;
        var entries = QuickLaunchEntries.ToList();
        var entry = entries[index]; entries.RemoveAt(index); entries.Insert(target, entry);
        _quickLaunch.SetEntries(entries);
    }
    private void Changed(object? sender, QuickLaunchChangedEventArgs args) => Refresh();
    private void LanguageChanged(object? sender, EventArgs args) { if (!_disposed) OnPropertyChanged(string.Empty); }
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
    }
}
