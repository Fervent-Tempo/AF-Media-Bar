// 页面拥有订阅与筛选状态；后台下载由应用服务持有，离页只停止 UI 更新。
using System.Collections.ObjectModel;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Plugins;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Plugins;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>插件中心的可绑定目录及语言投影，不执行网络或文件操作。</summary>
public partial class PluginCenterViewModel : ObservableObject, IDisposable
{
    private readonly PluginDownloadService _downloads;
    private readonly LocalizationService _localization;
    private readonly Dispatcher _dispatcher;
    private readonly PluginCardViewModel[] _cards;
    private bool _active;
    private bool _disposed;
    private int _generation;

    [ObservableProperty] private string _searchText = string.Empty;

    public ObservableCollection<PluginCardViewModel> Plugins { get; } = [];
    public bool IsEmpty => Plugins.Count == 0;

    public PluginCenterViewModel(PluginDownloadService downloads, LocalizationService localization, Dispatcher dispatcher)
    {
        _downloads = downloads;
        _localization = localization;
        _dispatcher = dispatcher;
        _cards = PluginCatalog.Entries.Select(plugin => new PluginCardViewModel(plugin, downloads)).ToArray();
        Refresh();
    }

    public void Activate()
    {
        if (_active || _disposed) return;
        _active = true;
        _generation++;
        _downloads.StateChanged += OnStateChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        foreach (var card in _cards) card.SetActive(true);
        Refresh();
    }

    public void Deactivate()
    {
        _active = false;
        _generation++;
        _downloads.StateChanged -= OnStateChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
        foreach (var card in _cards) card.SetActive(false);
    }

    partial void OnSearchTextChanged(string value) => Filter();

    private void OnLanguageChanged(object? sender, EventArgs e) => OnStateChanged();

    private void OnStateChanged()
    {
        if (!_active || _disposed || _dispatcher.HasShutdownStarted) return;
        var generation = _generation;
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (!_active || _disposed || generation != _generation || _dispatcher.HasShutdownStarted) return;
            Refresh();
        });
    }

    private void Refresh()
    {
        foreach (var card in _cards) card.Refresh();
        Filter();
    }

    private void Filter()
    {
        var query = SearchText.Trim();
        var visible = _cards.Where(card => query.Length == 0 ||
            $"{card.Target} {card.Descriptor.Name} {card.Descriptor.TargetApplication} {card.Descriptor.Author}"
                .Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        // 下载进度不重建目录，保留展开状态、键盘焦点和架构选择。
        if (!Plugins.SequenceEqual(visible))
        {
            Plugins.Clear();
            foreach (var card in visible) Plugins.Add(card);
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Deactivate();
    }
}

/// <summary>单个插件的语言、附件选择与下载命令；没有独立事件订阅。</summary>
public partial class PluginCardViewModel : ObservableObject
{
    private readonly PluginDownloadService _downloads;
    private PluginDownloadState _state = new();
    private string? _actionFailureKey;
    private bool _active;
    private int _generation;

    [ObservableProperty] private PluginAsset? _selectedAsset;

    public PluginDescriptor Descriptor { get; }
    public string Target => Text("Target");
    public string Summary => Text("Summary");
    public string Compatibility => Text("Compatibility");
    public string Requirements => Text("Requirements");
    public string Install => Text("Install");
    public string Upgrade => Text("Upgrade");
    public string Restore => Text("Restore");
    public string Attribution => HasDownload ? Translations.Format("Plugins.Attribution", Descriptor.Author, Descriptor.Version)
        : Translations.Format("Plugins.Author", Descriptor.Author);
    public bool HasDownload => Descriptor.Assets.Count > 0;
    public bool CanSelectAsset => _active && !_downloads.IsBusy;
    public bool CanDownload => _active && SelectedAsset is not null && !_downloads.IsBusy;
    public bool CanCancel => _active && _downloads.IsBusy && _state.Phase is PluginDownloadPhase.Downloading or PluginDownloadPhase.Verifying;
    public bool CanReveal => _active && _state.Phase == PluginDownloadPhase.Completed;
    public bool HasStatus => _state.Phase != PluginDownloadPhase.Available || _actionFailureKey is not null;
    public bool HasProgress => _state.Phase is PluginDownloadPhase.Downloading or PluginDownloadPhase.Verifying;
    public double Progress => _state.Progress;
    public string StatusText => _actionFailureKey is { } actionFailure ? Translations.Get(actionFailure)
        : !HasDownload ? Translations.Get("Plugins.Native")
        : SelectedAsset is null ? Translations.Get("Plugins.ChooseArchitecture")
        : _state.FailureKey is { } failure ? Translations.Get(failure)
        : Translations.Get($"Plugins.Status.{_state.Phase}");
    public string SourceText => _state.SourceHost is { } host ? Translations.Format("Plugins.Source", host) : string.Empty;
    public string DownloadLabel => Translations.Get(_state.Phase switch
    {
        PluginDownloadPhase.Failed or PluginDownloadPhase.Canceled => "Plugins.Retry",
        PluginDownloadPhase.Completed => "Plugins.VerifyOrDownload",
        _ => "Plugins.Download"
    });

    public PluginCardViewModel(PluginDescriptor descriptor, PluginDownloadService downloads)
    {
        Descriptor = descriptor;
        _downloads = downloads;
        // 新窗口恢复应用仍持有的下载，避免多架构项因未选择附件而隐藏进度和取消入口。
        _selectedAsset = descriptor.Assets.FirstOrDefault(asset => downloads.GetState(descriptor, asset).Phase is
                PluginDownloadPhase.Downloading or PluginDownloadPhase.Verifying)
            ?? (descriptor.Assets.Count == 1 ? descriptor.Assets[0] : descriptor.Assets.FirstOrDefault(asset =>
                downloads.GetState(descriptor, asset).Phase != PluginDownloadPhase.Available))
            ?? descriptor.Assets.FirstOrDefault(asset => asset.Architecture == "x64")
            ?? descriptor.Assets.FirstOrDefault();
    }

    partial void OnSelectedAssetChanged(PluginAsset? value)
    {
        _actionFailureKey = null;
        Refresh();
    }

    internal void SetActive(bool active)
    {
        _active = active;
        _generation++;
        Refresh();
    }

    public void Refresh()
    {
        _state = SelectedAsset is { } asset ? _downloads.GetState(Descriptor, asset) : new();
        OnPropertyChanged(string.Empty);
        DownloadCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RevealCommand.NotifyCanExecuteChanged();
    }

    private string Text(string suffix) => Translations.Get($"{Descriptor.ResourcePrefix}.{suffix}");

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private void Download()
    {
        if (SelectedAsset is not { } asset) return;
        _actionFailureKey = null;
        _downloads.TryStartDownload(Descriptor, asset);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _downloads.Cancel();

    [RelayCommand(CanExecute = nameof(CanReveal))]
    private async Task RevealAsync()
    {
        if (SelectedAsset is not { } asset) return;
        var generation = _generation;
        var revealed = await _downloads.RevealAsync(Descriptor, asset);
        if (!_active || generation != _generation) return;
        _actionFailureKey = revealed ? null : "Plugins.Error.Open";
        Refresh();
    }

    [RelayCommand]
    private void OpenProject()
    {
        _actionFailureKey = ExternalLinkLauncher.TryOpen(Descriptor.ProjectUrl) ? null : "Plugins.Error.Open";
        Refresh();
    }
}
