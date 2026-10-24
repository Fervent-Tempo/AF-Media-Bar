// 页面仅投影本地教程和播放器资料；版本资料由应用服务读取，离页取消并拒绝迟到结果。
using System.Collections.ObjectModel;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Plugins;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Plugins;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>播放器兼容性参考的当前语言投影，不检测或修改播放器。</summary>
public sealed partial class PlayerSupportItem(PlayerSupportProfile profile) : ObservableObject
{
    public PlayerSupportProfile Profile { get; } = profile;
    public string Name => Translations.Get($"QuickStart.Player.{Profile.Id}.Name");
    public string Capabilities => Translations.Format("QuickStart.Capabilities", Capability(Profile.Information),
        Capability(Profile.Timeline), Capability(Profile.Controls));
    public string Note => Translations.Get("QuickStart.Note." + Profile.NoteKey);
    public string AfNote => Translations.Get(Profile.AfNoteKey);
    public bool HasPlugin => Profile.PluginId is not null;
    public string Recommendation => Translations.Get(HasPlugin ? "QuickStart.PluginAvailable"
        : Profile.Information == PlayerCapability.Implemented && Profile.Timeline == PlayerCapability.Implemented &&
            Profile.Controls == PlayerCapability.Implemented ? "QuickStart.UseBuiltIn" : "QuickStart.NoRecommendation");
    private static string Capability(PlayerCapability capability) => Translations.Get("QuickStart.Capability." + capability);

    [RelayCommand]
    private void OpenSource() => ExternalLinkLauncher.TryOpen(Profile.SourceUrl);
}

/// <summary>快速开始的筛选、语言与可见期；进入首页不发起联网请求。</summary>
public sealed partial class QuickStartViewModel : ObservableObject, IDisposable
{
    private readonly ReleaseHighlightsService _releases;
    private readonly LocalizationService _localization;
    private readonly Dispatcher _dispatcher;
    private ReleaseHighlightsResult? _result;
    private CancellationTokenSource? _request;
    private bool _active;
    private bool _disposed;
    private int _generation;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _latestVersion = string.Empty;
    [ObservableProperty] private string _latestTitle = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _highlights = [];
    public ObservableCollection<PlayerSupportItem> Players { get; } = [];
    public bool IsEmpty => Players.Count == 0;

    public QuickStartViewModel(ReleaseHighlightsService releases, LocalizationService localization, Dispatcher dispatcher)
    {
        _releases = releases;
        _localization = localization;
        _dispatcher = dispatcher;
        Filter();
    }

    public async Task ActivateAsync()
    {
        if (_active || _disposed) return;
        _active = true;
        _localization.LanguageChanged += OnLanguageChanged;
        using var request = new CancellationTokenSource();
        _request = request;
        var generation = ++_generation;
        Project();
        try
        {
            var result = await _releases.ReadInitialAsync(request.Token);
            if (!_active || _disposed || request.IsCancellationRequested || generation != _generation) return;
            _result = result;
            Project();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            if (_active && !_disposed && generation == _generation)
                LatestTitle = Translations.Get("QuickStart.Latest.Unavailable");
        }
        finally { if (ReferenceEquals(_request, request)) _request = null; }
    }

    public void Deactivate()
    {
        _active = false;
        _generation++;
        _request?.Cancel();
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    partial void OnSearchTextChanged(string value) => Filter();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        var generation = _generation;
        if (!_active || _disposed || _dispatcher.HasShutdownStarted) return;
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (_active && !_disposed && generation == _generation) Project();
        });
    }

    private void Project()
    {
        var language = _localization.CurrentLanguage switch
        {
            LocalizationLanguage.TraditionalChinese => "zh-Hant",
            LocalizationLanguage.English => "en",
            LocalizationLanguage.Vietnamese => "vi",
            _ => "zh-Hans"
        };
        if (_result?.Releases.FirstOrDefault() is { } latest)
        {
            var (text, _) = ReleaseHighlightsPolicy.ResolveText(latest, language);
            LatestVersion = "v" + latest.Version;
            LatestTitle = text.Title;
            Highlights = text.Highlights;
        }
        Filter();
    }

    private void Filter()
    {
        var query = SearchText.Trim();
        Players.Clear();
        foreach (var profile in PlayerSupportCatalog.Entries)
        {
            var item = new PlayerSupportItem(profile);
            if (query.Length == 0 || $"{item.Name} {profile.Name} {profile.Id}".Contains(query, StringComparison.OrdinalIgnoreCase))
                Players.Add(item);
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
