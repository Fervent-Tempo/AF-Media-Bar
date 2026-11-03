// 首页协调只读资料和已有会话；离页取消请求，不拥有媒体或网络资源。
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Home;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Home;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Resources;
namespace AFMediaBar.ViewModels.Pages;

public sealed record HomeFeatureCard(string Id, string Badge, string Title, string Summary, string Url, string? ImageUrl)
{
    /// <summary>类型、版本与来源标签；Badge 保留原有拼接文案，旧页面继续绑定它。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record HomeThreadCard(string Title, string Detail, string Url, string Number = "", string DateText = "", bool IsDraft = false);

/// <summary>轮播圆点的一项：当前项显示为强调色胶囊，点击后切到该张。</summary>
public sealed record HomeFeatureTab(HomeFeatureCard Feature, bool IsCurrent, string AutomationName);

/// <summary>来源卡片的图标呈现状态，不改变会话与支持能力。</summary>
public sealed partial class HomePlayerTile(HomePlayerCard player) : ObservableObject
{
    public HomePlayerCard Player { get; } = player;
    public string Name => Player.Name;
    public string Status => Player.Status;
    public bool IsCurrent => Player.IsCurrent;
    [ObservableProperty] private ImageSource? _icon;
}

/// <summary>先显示本地内容，再刷新公开动态；不上传任何使用资料。</summary>
public partial class QuickStartViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshCooldown = TimeSpan.FromMinutes(5);
    private readonly HomeContentService _home;
    private readonly TaskbarCustomizationService _customization;
    private CustomizationDetectionResult? _customizationResult;
    private readonly IHomeMediaState _media;
    private readonly LocalizationService _localization;
    private readonly AppLogService _log;
    private readonly QuickLaunchIconService _icons;
    private readonly MediaSourceActivationService _sources;
    private readonly DispatcherTimer _refreshCooldownTimer = new(DispatcherPriority.Background) { Interval = RefreshCooldown };
    private readonly System.Windows.Threading.Dispatcher _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
    private CancellationTokenSource? _request;
    private HomeCatalogue _catalogue = new(1, []);
    private ProjectActivitySnapshot _project = HomeContentService.EmptyProject;
    private int _generation;
    private DateTimeOffset _lastManualRefresh;
    private bool _active, _disposed;
    private string _playerFilter = string.Empty;
    private string _playerSignature = string.Empty;
    private string _additionalPlayersText = string.Empty;
    private readonly Dictionary<string, ImageSource> _images = new();
    [ObservableProperty] private HomeFeatureCard? _selectedFeature;
    [ObservableProperty] private ImageSource? _featureImage;
    [ObservableProperty] private bool _isLoading;
    public ObservableCollection<HomeFeatureCard> Features { get; } = [];
    public ObservableCollection<HomeFeatureTab> FeatureTabs { get; } = [];
    public ObservableCollection<HomePlayerCard> Players { get; } = [];
    public ObservableCollection<HomePlayerTile> VisiblePlayers { get; } = [];
    public ObservableCollection<PlayerSupportProfile> SupportedPlayers { get; } = [];
    public ObservableCollection<HomeThreadCard> Issues { get; } = [];
    public ObservableCollection<HomeThreadCard> PullRequests { get; } = [];
    public SettingsPreviewViewModel Preview { get; }
    public string InstalledVersion => "v" + (typeof(QuickStartViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.6.0");
    public string Stars => FormatCount(_project.Stars);
    public string OpenIssues => FormatCount(_project.OpenIssues);
    public string RecentIssues => FormatCount(_project.RecentIssues);
    public string OpenPullRequests => FormatCount(_project.OpenPullRequests);
    public bool HasPlayers => Players.Count != 0;
    public bool HasCustomizationTools => _customizationResult?.Tools.Count > 0;
    public string CustomizationStatus => HasCustomizationTools
        ? Translations.Format("Home.Customization.Detected", string.Join("、", _customizationResult!.Tools.Select(tool => tool.Name))) : string.Empty;
    public string CustomizationCoverage => Translations.Get("Home.Customization.Coverage") +
        (_customizationResult?.IsComplete == false && HasCustomizationTools ? "\n" + Translations.Get("Home.Customization.Incomplete") : "");
    public bool HasNoIssues => Issues.Count == 0;
    public bool HasNoPullRequests => PullRequests.Count == 0;
    public bool HasMultipleFeatures => Features.Count > 1;
    public bool HasFeatureImage => FeatureImage is not null;
    public bool HasAdditionalPlayers => AdditionalPlayersText.Length > 0;
    public bool HasNoSupportedPlayers => SupportedPlayers.Count == 0;
    public int SupportedPlayerCount => PlayerSupportCatalog.All.Count;
    public string PlayerSupportCountText => Translations.Format("Home.Player.Count", SupportedPlayerCount);
    public string AdditionalPlayersText => _additionalPlayersText;
    public string FeaturePosition => $"{Math.Max(1, Features.IndexOf(SelectedFeature!)+1)} / {Features.Count}";
    public bool CanRefresh => !IsLoading && DateTimeOffset.UtcNow - _lastManualRefresh >= RefreshCooldown;
    public string RefreshHint => IsLoading ? Translations.Get("Home.Refreshing")
        : CanRefresh ? Translations.Get("Home.Refresh") : Translations.Get("Home.RefreshCooldown");
    public string ProjectStatus => _project.FetchedAt is { } time
        ? Translations.Get(_project.IsCached ? "Home.Cache" : "Home.Updated") + " · " + time.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : Translations.Get("Home.Unavailable");
    public string PlayerFilter { get => _playerFilter; set { if (SetProperty(ref _playerFilter, value)) ProjectSupport(); } }
    public QuickStartViewModel(HomeContentService home, IHomeMediaState media,
        LocalizationService localization, AppLogService log, SettingsPreviewViewModel preview,
        QuickLaunchIconService icons, MediaSourceActivationService sources, TaskbarCustomizationService customization)
    {
        _home = home; _media = media; _localization = localization; _log = log; Preview = preview;
        _icons = icons; _sources = sources;
        _customization = customization;
        _refreshCooldownTimer.Tick += OnRefreshCooldownElapsed;
        ProjectFeatures(); ProjectPlayers(); ProjectSupport();
    }
    public async Task ActivateAsync()
    {
        Deactivate();
        if (_disposed) return;
        _active = true;
        _media.Changed += OnMediaChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        ProjectPlayers();
        await LoadAsync(false);
    }
    public void Deactivate()
    {
        _active = false; _generation++;
        _media.Changed -= OnMediaChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
        _request?.Cancel(); _request?.Dispose(); _request = null;
        IsLoading = false;
    }
    [RelayCommand] private Task RefreshAsync()
    {
        if (!CanRefresh) return Task.CompletedTask;
        _lastManualRefresh = DateTimeOffset.UtcNow;
        _refreshCooldownTimer.Stop();
        _refreshCooldownTimer.Start();
        NotifyRefreshState();
        return LoadAsync(true);
    }
    [RelayCommand] private void ClearPlayerFilter()
    {
        _playerFilter = string.Empty;
        OnPropertyChanged(nameof(PlayerFilter));
        ProjectSupport();
    }
    /// <summary>参数来自 <see cref="FeatureTabs"/> 的当前快照；计时与交互暂停由页面负责。</summary>
    [RelayCommand] private void SelectFeature(HomeFeatureTab? tab)
    {
        if (tab is null || !Features.Contains(tab.Feature)) return;
        SelectedFeature = tab.Feature;
    }
    private async Task LoadAsync(bool force)
    {
        if (!_active) return;
        _request?.Cancel(); _request?.Dispose();
        var request = _request = new CancellationTokenSource();
        var token = request.Token;
        var generation = ++_generation;
        var detection = DetectCustomizationAsync(token, generation);
        _playerSignature = string.Empty;
        ProjectPlayers();
        if (SelectedFeature is { } selected && !_images.ContainsKey(selected.ImageUrl ?? string.Empty))
            _ = LoadImageAsync(selected, token);
        bool IsCurrent() => _active && !_disposed && !token.IsCancellationRequested && generation == _generation;
        IsLoading = true;
        try
        {
            _catalogue = (await _home.ReadCatalogueAsync(token)).Catalogue;
            if (!IsCurrent()) return;
            _project = await _home.ReadProjectAsync(token);
            if (!IsCurrent()) return;
            ProjectFeatures(); ProjectActivity();
            async Task CatalogueAsync()
            {
                var result = await _home.RefreshCatalogueAsync(force, token);
                if (IsCurrent()) { _catalogue = result.Catalogue; ProjectFeatures(); ProjectSupport(); ProjectPlayers(); }
            }
            async Task ProjectAsync()
            {
                var result = await _home.RefreshProjectAsync(force, token);
                if (IsCurrent()) { _project = result; ProjectActivity(); }
            }
            await Task.WhenAll(CatalogueAsync(), ProjectAsync(), detection);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (IsCurrent()) _log.Warn("Home", "首页刷新失败，保留可用内容", exception); }
        finally
        {
            // 即使远程资料先失败，也要观察独立的本机检测任务。
            await detection;
            if (IsCurrent()) IsLoading = false;
        }
    }
    private async Task DetectCustomizationAsync(CancellationToken token, int generation)
    {
        try
        {
            var result = await _customization.DetectAsync(token);
            if (!_active || _disposed || token.IsCancellationRequested || generation != _generation) return;
            _customizationResult = result;
            NotifyCustomization();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _log.Warn("Home", "美化工具检测未完成", exception);
            if (_active && !_disposed && !token.IsCancellationRequested && generation == _generation)
            {
                _customizationResult = new([], false);
                NotifyCustomization();
            }
        }
    }
    private void NotifyCustomization()
    {
        OnPropertyChanged(nameof(HasCustomizationTools));
        OnPropertyChanged(nameof(CustomizationStatus));
        OnPropertyChanged(nameof(CustomizationCoverage));
    }
    [RelayCommand]
    private void NextFeature() => Advance(1);
    [RelayCommand]
    private void PreviousFeature() => Advance(-1);
    public void AdvanceAutomatically() => Advance(1);
    private void Advance(int direction)
    {
        if (Features.Count > 1) SelectedFeature = Features[(Features.IndexOf(SelectedFeature!) + direction + Features.Count) % Features.Count];
    }
    partial void OnIsLoadingChanged(bool value) => NotifyRefreshState();
    partial void OnFeatureImageChanged(ImageSource? value) => OnPropertyChanged(nameof(HasFeatureImage));
    partial void OnSelectedFeatureChanged(HomeFeatureCard? value)
    {
        OnPropertyChanged(nameof(FeaturePosition));
        ProjectFeatureTabs();
        // 切页保留上一张图，直到新图就绪；无图条目立即清空，由页面显示静态演示场景。
        if (value?.ImageUrl is not { } url) { FeatureImage = null; return; }
        if (_images.TryGetValue(url, out var image)) { FeatureImage = image; return; }
        if (_request is { } request) _ = LoadImageAsync(value, request.Token);
    }
    private async Task LoadImageAsync(HomeFeatureCard card, CancellationToken token)
    {
        try
        {
            var image = await _home.ReadImageAsync(card.ImageUrl, token);
            if (!_active || _disposed || token.IsCancellationRequested) return;
            if (image is null) { if (SelectedFeature == card) FeatureImage = null; return; }
            if (_images.Count >= 8) _images.Clear();
            _images[card.ImageUrl!] = image;
            if (SelectedFeature == card) FeatureImage = image;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { _log.Warn("Home", "读取首页示例图片失败", exception); }
    }
    private void ProjectFeatures()
    {
        var oldId = SelectedFeature?.Id;
        var version = InstalledVersion.TrimStart('v');
        HomeFeatureCard Card(string id, string kind, string titleKey, string summaryKey, string image)
        {
            // 只接收当前版本的两项已定稿文案，旧缓存或开发中条目不能覆盖内置介绍。
            var remote = _catalogue.Items.FirstOrDefault(item => item.Id == id && item.Kind == kind && item.Version == version);
            var copy = remote?.Localizations.GetValueOrDefault(Language);
            return new(id, Translations.Get("Home.Kind." + kind), copy?.Title ?? Translations.Get(titleKey),
                copy?.Summary ?? Translations.Get(summaryKey), remote?.Url ?? HomeContentPolicy.RepositoryUrl, image)
            { Tags = FeatureTags("Home.Kind." + kind, kind == "release" ? version : null, null) };
        }
        HomeFeatureCard[] items =
        [
            Card("release", "release", "Home.Feature.Release", "Home.Feature.Release.Body",
                "pack://application:,,,/AFMediaBar;component/Assets/Home/hero-" + (Language is "zh-Hans" or "zh-Hant" ? "zh" : "en") + ".png"),
            Card("settings", "feature", "Home.Feature.Settings", "Home.Feature.Settings.Body",
                "pack://application:,,,/AFMediaBar;component/Assets/Home/settings.png")
        ];
        Features.Clear(); foreach (var item in items) Features.Add(item);
        SelectedFeature = Features.FirstOrDefault(item => item.Id == oldId) ?? Features.First();
        ProjectFeatureTabs();
        OnPropertyChanged(nameof(HasMultipleFeatures)); OnPropertyChanged(nameof(FeaturePosition));
    }
    /// <summary>按当前选中项重投影轮播圆点；标题变化或语言切换后都要重建一次。</summary>
    private void ProjectFeatureTabs()
    {
        FeatureTabs.Clear();
        for (var index = 0; index < Features.Count; index++)
        {
            var feature = Features[index];
            FeatureTabs.Add(new(feature, feature == SelectedFeature,
                Translations.Format("Home.Rotation.Slide", index + 1, feature.Title)));
        }
        OnPropertyChanged(nameof(FeaturePosition));
    }
    private string Language => _localization.CurrentLanguage switch
    {
        LocalizationLanguage.TraditionalChinese => "zh-Hant", LocalizationLanguage.English => "en", LocalizationLanguage.Vietnamese => "vi", _ => "zh-Hans"
    };
    private void ProjectPlayers()
    {
        var snapshot = _media.Snapshot;
        var signature = Language + string.Join("|", _media.Sessions.Select(item => $"{item.Key}:{item.SourceId}:{item.IsSelected}:{item.IsPlaying}")) +
            $"{snapshot?.SourceId}:{snapshot?.IsConnected}:{snapshot?.IsStale}:{snapshot?.CanPlayPause}:{snapshot?.CanSeek}:{snapshot?.Lyrics is not null}";
        if (_playerSignature == signature) return;
        _playerSignature = signature;
        Players.Clear(); foreach (var item in PlayerSupportCatalog.Describe(_media.Sessions, _media.Snapshot)) Players.Add(item);
        VisiblePlayers.Clear(); foreach (var player in Players.Take(2)) VisiblePlayers.Add(new(player));
        if (_active && _request is { } request)
            foreach (var tile in VisiblePlayers) _ = LoadPlayerIconAsync(tile, request.Token, _generation);
        var additional = Players.Count - VisiblePlayers.Count;
        _additionalPlayersText = additional > 0 ? Translations.Format("Home.Player.More", additional) : string.Empty;
        OnPropertyChanged(nameof(AdditionalPlayersText));
        OnPropertyChanged(nameof(HasAdditionalPlayers));
        OnPropertyChanged(nameof(HasPlayers));
    }
    private async Task LoadPlayerIconAsync(HomePlayerTile tile, CancellationToken token, int generation)
    {
        try
        {
            // 进程路径和 Shell 图标均在后台读取，不激活或启动来源应用。
            var source = await Task.Run(() => _sources.Describe(tile.Player.SourceId), token);
            if (!source.CanQuickLaunch) return;
            var icon = await _icons.GetAsync(new(source.SourceId, source.DisplayName, source.LaunchKind!.Value, source.LaunchTarget!), token);
            if (_active && !_disposed && !token.IsCancellationRequested && generation == _generation && VisiblePlayers.Contains(tile))
                tile.Icon = icon;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { _log.Warn("Home", "读取来源应用图标失败，保留默认图标", exception); }
    }
    private static IReadOnlyList<string> FeatureTags(string kindKey, string? version, string? source)
    {
        var tags = new List<string> { Translations.Get(kindKey) };
        if (!string.IsNullOrEmpty(version)) tags.Add("v" + version);
        if (!string.IsNullOrEmpty(source)) tags.Add(source);
        return tags;
    }
    private void ProjectSupport()
    {
        SupportedPlayers.Clear();
        foreach (var profile in PlayerSupportCatalog.Resolve(_catalogue, Language).Where(profile =>
            profile.Name.Contains(PlayerFilter, StringComparison.CurrentCultureIgnoreCase) ||
            profile.Notes.Contains(PlayerFilter, StringComparison.CurrentCultureIgnoreCase) ||
            profile.PluginCondition.Contains(PlayerFilter, StringComparison.CurrentCultureIgnoreCase)))
            SupportedPlayers.Add(profile);
        OnPropertyChanged(nameof(HasNoSupportedPlayers));
        OnPropertyChanged(nameof(SupportedPlayerCount));
        OnPropertyChanged(nameof(PlayerSupportCountText));
    }
    private void ProjectActivity()
    {
        Issues.Clear(); PullRequests.Clear();
        foreach (var issue in _project.Issues) Issues.Add(Thread(issue));
        foreach (var pr in _project.PullRequests) PullRequests.Add(Thread(pr));
        foreach (var property in new[] { nameof(Stars), nameof(OpenIssues), nameof(RecentIssues), nameof(OpenPullRequests), nameof(ProjectStatus), nameof(HasNoIssues), nameof(HasNoPullRequests) }) OnPropertyChanged(property);
    }
    private static HomeThreadCard Thread(ProjectThread item)
    {
        var date = item.UpdatedAt == DateTimeOffset.MinValue ? "" : item.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd");
        // 页面把编号作为独立列呈现，标题只保留正文，避免 "#261  #261" 重复。
        return new(item.Title, (item.IsDraft ? Translations.Get("Home.Draft") + " · " : "") + date, item.Url,
            $"#{item.Number}", date, item.IsDraft);
    }
    private static string FormatCount(int? value) => value?.ToString("N0") ?? "—";
    private void OnRefreshCooldownElapsed(object? sender, EventArgs args)
    {
        _refreshCooldownTimer.Stop();
        NotifyRefreshState();
    }
    private void NotifyRefreshState()
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(RefreshHint));
    }
    private void OnMediaChanged(object? sender, EventArgs args)
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(() => { if (_active && !_disposed) ProjectPlayers(); }); return; }
        if (_active && !_disposed) ProjectPlayers();
    }
    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        if (!_active || _disposed) return;
        ProjectFeatures(); ProjectPlayers(); ProjectSupport(); ProjectActivity();
        ProjectFeatureTabs(); NotifyRefreshState(); NotifyCustomization();
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Deactivate(); _refreshCooldownTimer.Stop(); _images.Clear(); }
}
