// 首页协调只读资料和已有会话；离页取消请求，不拥有媒体或网络资源。
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Home;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Home;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Resources;
namespace AFMediaBar.ViewModels.Pages;

public sealed record HomeFeatureCard(string Id, string Badge, string Title, string Summary, string Url, string? ImageUrl)
{
    /// <summary>类型、版本与来源标签；Badge 保留原有拼接文案，旧页面继续绑定它。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record HomeThreadCard(string Title, string Detail, string Url, string Number = "", string DateText = "", bool IsDraft = false);

/// <summary>先显示本地内容，再刷新公开动态；不上传任何使用资料。</summary>
public partial class QuickStartViewModel : ObservableObject, IDisposable
{
    private readonly ReleaseHighlightsService _releases;
    private readonly HomeContentService _home;
    private readonly IHomeMediaState _media;
    private readonly LocalizationService _localization;
    private readonly AppLogService _log;
    private readonly System.Windows.Threading.Dispatcher _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
    private CancellationTokenSource? _request;
    private IReadOnlyList<ReleaseHighlights> _releaseData = [];
    private bool _releaseIsCached = true;
    private DateTimeOffset? _releaseFetchedAt;
    private HomeCatalogue _catalogue = new(1, []);
    private ProjectActivitySnapshot _project = HomeContentService.EmptyProject;
    private int _generation;
    private DateTimeOffset _lastManualRefresh, _lastReleaseAttempt;
    private bool _active, _disposed;
    private bool _hasShownLatest, _featureWasChosen;
    private string _playerFilter = string.Empty;
    private string _playerSignature = string.Empty;
    private readonly Dictionary<string, ImageSource> _images = new();
    [ObservableProperty] private HomeFeatureCard? _selectedFeature;
    [ObservableProperty] private ImageSource? _featureImage;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isRotationPaused;
    [ObservableProperty] private bool _showAllPlayers;
    public ObservableCollection<HomeFeatureCard> Features { get; } = [];
    public ObservableCollection<HomePlayerCard> Players { get; } = [];
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
    public bool HasNoIssues => Issues.Count == 0;
    public bool HasNoPullRequests => PullRequests.Count == 0;
    public bool HasMultipleFeatures => Features.Count > 1;
    public bool HasNoSupportedPlayers => SupportedPlayers.Count == 0;
    public int SupportedPlayerCount => PlayerSupportCatalog.All.Count;
    public string PlayerSupportCountText => Translations.Format("Home.Player.Count", SupportedPlayerCount);
    public string FeaturePosition => $"{Math.Max(1, Features.IndexOf(SelectedFeature!)+1)} / {Features.Count}";
    public string RotationText => Translations.Get(IsRotationPaused ? "Home.Rotation.Resume" : "Home.Rotation.Pause");
    public string ProjectStatus => _project.FetchedAt is { } time
        ? Translations.Get(_project.IsCached ? "Home.Cache" : "Home.Updated") + " · " + time.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : Translations.Get("Home.Unavailable");
    public string PlayerFilter { get => _playerFilter; set { if (SetProperty(ref _playerFilter, value)) ProjectSupport(); } }
    public QuickStartViewModel(ReleaseHighlightsService releases, HomeContentService home, IHomeMediaState media,
        LocalizationService localization, AppLogService log, SettingsPreviewViewModel preview)
    {
        _releases = releases; _home = home; _media = media; _localization = localization; _log = log; Preview = preview;
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
        if (DateTimeOffset.UtcNow - _lastManualRefresh < TimeSpan.FromMinutes(5)) return Task.CompletedTask;
        _lastManualRefresh = DateTimeOffset.UtcNow;
        return LoadAsync(true);
    }
    [RelayCommand] private void ClearPlayerFilter()
    {
        _playerFilter = string.Empty;
        OnPropertyChanged(nameof(PlayerFilter));
        ProjectSupport();
    }
    private async Task LoadAsync(bool force)
    {
        if (!_active) return;
        _request?.Cancel(); _request?.Dispose();
        var request = _request = new CancellationTokenSource();
        var token = request.Token;
        var generation = ++_generation;
        bool IsCurrent() => _active && !_disposed && !token.IsCancellationRequested && generation == _generation;
        IsLoading = true;
        try
        {
            var initialRelease = await _releases.ReadInitialAsync(token);
            _releaseData = initialRelease.Releases;
            _releaseIsCached = initialRelease.IsCached; _releaseFetchedAt = initialRelease.FetchedAt;
            if (!IsCurrent()) return;
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
            async Task ReleasesAsync()
            {
                if (DateTimeOffset.UtcNow - _lastReleaseAttempt < TimeSpan.FromMinutes(5)) return;
                _lastReleaseAttempt = DateTimeOffset.UtcNow;
                var expired = initialRelease.FetchedAt is not { } fetched || DateTimeOffset.UtcNow - fetched >= TimeSpan.FromHours(6);
                var result = await _releases.LoadAsync(force || expired, token);
                if (IsCurrent()) { _releaseData = result.Releases; _releaseIsCached = result.IsCached; _releaseFetchedAt = result.FetchedAt; ProjectFeatures(); }
            }
            await Task.WhenAll(CatalogueAsync(), ProjectAsync(), ReleasesAsync());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (IsCurrent()) _log.Warn("Home", "首页刷新失败，保留可用内容", exception); }
        finally { if (IsCurrent()) IsLoading = false; }
    }
    [RelayCommand] private void NextFeature() { _featureWasChosen = true; IsRotationPaused = true; Advance(1); }
    [RelayCommand] private void PreviousFeature() { _featureWasChosen = true; IsRotationPaused = true; Advance(-1); }
    [RelayCommand] private void ToggleRotation() => IsRotationPaused = !IsRotationPaused;
    public void AdvanceAutomatically() { if (!IsRotationPaused) Advance(1); }
    private void Advance(int direction)
    {
        if (Features.Count > 1) SelectedFeature = Features[(Features.IndexOf(SelectedFeature!) + direction + Features.Count) % Features.Count];
    }
    partial void OnIsRotationPausedChanged(bool value) => OnPropertyChanged(nameof(RotationText));
    partial void OnSelectedFeatureChanged(HomeFeatureCard? value)
    {
        OnPropertyChanged(nameof(FeaturePosition));
        FeatureImage = null;
        if (value?.ImageUrl is not { } url) return;
        if (_images.TryGetValue(url, out var image)) { FeatureImage = image; return; }
        if (url.StartsWith("pack://application:,,,/AFMediaBar;component/Assets/Home/", StringComparison.Ordinal))
        {
            try { var bitmap = new BitmapImage(new Uri(url)); bitmap.Freeze(); _images[url] = FeatureImage = bitmap; }
            catch (Exception exception) { _log.Warn("Home", "内置首页图片不可用", exception); }
        }
        else if (_request is { } request) _ = LoadImageAsync(value, request.Token);
    }
    private async Task LoadImageAsync(HomeFeatureCard card, CancellationToken token)
    {
        try
        {
            var image = await _home.ReadImageAsync(card.ImageUrl, token);
            if (!_active || _disposed || token.IsCancellationRequested || image is null) return;
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
        var items = new List<HomeFeatureCard>();
        if (_releaseData.FirstOrDefault() is { } latest)
        {
            var (copy, original) = ReleaseHighlightsPolicy.ResolveText(latest, Language);
            var source = _releaseIsCached
                ? Translations.Get(_releaseFetchedAt is null ? "Home.Bundled" : "Home.Cache")
                : original ? Translations.Get("Home.Original") : null;
            items.Add(new("latest", Translations.Get("Home.Kind.release") + " · v" + latest.Version + (original ? " · " + Translations.Get("Home.Original") : "") +
                (_releaseIsCached ? " · " + source : ""),
                copy.Title, string.Join(" ", copy.Highlights.Take(2)), latest.ReleaseNotesUrl ?? HomeContentPolicy.RepositoryUrl + "/releases", null)
                { Tags = FeatureTags("Home.Kind.release", latest.Version, source) });
        }
        foreach (var item in _catalogue.Items.Take(5))
        {
            var original = !item.Localizations.TryGetValue(Language, out var copy);
            copy ??= item.Localizations.GetValueOrDefault("zh-Hans") ?? item.Localizations.Values.First();
            items.Add(new(item.Id, Translations.Get("Home.Kind." + item.Kind) + (item.Version is null ? "" : " · v" + item.Version) +
                (original ? " · " + Translations.Get("Home.Original") : ""), copy.Title, copy.Summary, item.Url ?? HomeContentPolicy.RepositoryUrl, item.Image)
                { Tags = FeatureTags("Home.Kind." + item.Kind, item.Version, original ? Translations.Get("Home.Original") : null) });
        }
        items.Add(new("settings", Translations.Get("Home.Kind.development"), Translations.Get("Home.Feature.Settings"),
            Translations.Get("Home.Feature.Settings.Body"), HomeContentPolicy.RepositoryUrl, "pack://application:,,,/AFMediaBar;component/Assets/Home/settings.png")
            { Tags = FeatureTags("Home.Kind.development", null, null) });
        items.Add(new("panel", Translations.Get("Home.Kind.development"), Translations.Get("Home.Feature.Panel"),
            Translations.Get("Home.Feature.Panel.Body"), HomeContentPolicy.RepositoryUrl, "pack://application:,,,/AFMediaBar;component/Assets/Home/panel.png")
            { Tags = FeatureTags("Home.Kind.development", null, null) });
        items.Add(new("layers", Translations.Get("Home.Kind.feature"), Translations.Get("Start.Overview"),
            Translations.Get("Start.Intro.Description"), HomeContentPolicy.RepositoryUrl, null)
            { Tags = FeatureTags("Home.Kind.feature", null, null) });
        Features.Clear(); foreach (var item in items.DistinctBy(item => item.Id).Take(5)) Features.Add(item);
        SelectedFeature = !_hasShownLatest && !_featureWasChosen && _releaseData.Count > 0 ? Features.First() : Features.FirstOrDefault(item => item.Id == oldId) ?? Features.First();
        if (_releaseData.Count > 0) _hasShownLatest = true;
        OnPropertyChanged(nameof(HasMultipleFeatures)); OnPropertyChanged(nameof(FeaturePosition));
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
        OnPropertyChanged(nameof(HasPlayers));
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
        return new($"#{item.Number}  {item.Title}", (item.IsDraft ? Translations.Get("Home.Draft") + " · " : "") + date, item.Url,
            $"#{item.Number}", date, item.IsDraft);
    }
    private static string FormatCount(int? value) => value?.ToString("N0") ?? "—";
    private void OnMediaChanged(object? sender, EventArgs args)
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(() => { if (_active && !_disposed) ProjectPlayers(); }); return; }
        if (_active && !_disposed) ProjectPlayers();
    }
    private void OnLanguageChanged(object? sender, EventArgs args) { if (_active && !_disposed) { ProjectFeatures(); ProjectPlayers(); ProjectSupport(); ProjectActivity(); OnPropertyChanged(nameof(RotationText)); } }
    public void Dispose() { if (_disposed) return; _disposed = true; Deactivate(); _images.Clear(); }
}
