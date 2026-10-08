// Owns visible release-page state and cancellation; catalogue I/O belongs to the injected reader.
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>A release projected into the active interface language.</summary>
public sealed record ReleaseHighlightsItem(string Version, string Title, string DateText, IReadOnlyList<string> Highlights,
    string? ReleaseNotesUrl, string Badge, bool IsOriginal)
{
    /// <summary>Version label shown independently of the release title.</summary>
    public string VersionLabel => $"v{Version}";
    /// <summary>Whether an official release page is available.</summary>
    public bool HasReleaseNotes => ReleaseNotesUrl is not null;
    /// <summary>Original-text notice for a missing translation.</summary>
    public string OriginalNotice => IsOriginal ? Translations.Get("ReleaseHighlights.OriginalText") : string.Empty;
}

/// <summary>Coordinates a read-only release page without owning controls or installer state.</summary>
public partial class ReleaseHighlightsViewModel : ObservableObject
{
    private readonly ReleaseHighlightsService _reader;
    private readonly UpdateService _updates;
    private readonly LocalizationService _localization;
    private CancellationTokenSource? _request;
    private int _generation;
    private bool _active;
    private ReleaseHighlightsResult? _result;

    [ObservableProperty] private ReleaseHighlightsItem? _latest;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>Historical releases in descending numeric version order.</summary>
    public ObservableCollection<ReleaseHighlightsItem> History { get; } = [];

    /// <summary>Creates page state with application-owned readers and language state.</summary>
    public ReleaseHighlightsViewModel(ReleaseHighlightsService reader, UpdateService updates, LocalizationService localization)
    {
        _reader = reader;
        _updates = updates;
        _localization = localization;
    }

    /// <summary>Activates subscriptions and shows local content before refreshing on demand.</summary>
    public async Task ActivateAsync()
    {
        if (_active) return;
        _active = true;
        _updates.UpdateStateChanged += OnUpdateState;
        _localization.LanguageChanged += OnLanguageChanged;
        await LoadAsync(false);
    }

    /// <summary>Stops page work and subscriptions; repeated navigation cannot accumulate handlers.</summary>
    public void Deactivate()
    {
        _active = false;
        _generation++;
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
        _updates.UpdateStateChanged -= OnUpdateState;
        _localization.LanguageChanged -= OnLanguageChanged;
        IsLoading = false;
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(true);

    private async Task LoadAsync(bool force)
    {
        if (!_active) return;
        _request?.Cancel();
        _request?.Dispose();
        using var request = new CancellationTokenSource();
        _request = request;
        var generation = ++_generation;
        IsLoading = true;
        try
        {
            var initial = await _reader.ReadInitialAsync(request.Token);
            if (!IsCurrent(generation, request)) return;
            _result = initial;
            Project();
            var result = await _reader.LoadAsync(force, request.Token);
            if (!IsCurrent(generation, request)) return;
            _result = result;
            Project();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            if (IsCurrent(generation, request)) StatusText = Translations.Get("ReleaseHighlights.Status.RefreshFailed");
        }
        finally
        {
            if (IsCurrent(generation, request)) IsLoading = false;
            if (ReferenceEquals(_request, request)) _request = null;
        }
    }

    private bool IsCurrent(int generation, CancellationTokenSource request) =>
        _active && generation == _generation && !request.IsCancellationRequested;

    private void OnUpdateState(UpdateState state) { if (_active) Project(); }
    private void OnLanguageChanged(object? sender, EventArgs e) { if (_active) Project(); }

    private void Project()
    {
        var language = _localization.CurrentLanguage switch
        {
            LocalizationLanguage.TraditionalChinese => "zh-Hant",
            LocalizationLanguage.English => "en",
            _ => "zh-Hans"
        };
        var releases = ReleaseHighlightsPolicy.Merge(_result?.Releases ?? [], _updates.CurrentState.Manifest);
        var items = releases.Select(release =>
        {
            var (text, original) = ReleaseHighlightsPolicy.ResolveText(release, language);
            var current = UpdateVersionPolicy.TryParse(release.Version, out var version) &&
                UpdateVersionPolicy.TryParse(_updates.CurrentVersion, out var running) && version == running;
            return new ReleaseHighlightsItem(release.Version, text.Title, release.ReleaseDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                text.Highlights, release.ReleaseNotesUrl, current ? Translations.Get("ReleaseHighlights.CurrentVersion") : string.Empty, original);
        }).ToArray();
        Latest = items.FirstOrDefault();
        History.Clear();
        foreach (var item in items.Skip(1)) History.Add(item);
        StatusText = _result?.FailureKey is { } failure ? Translations.Get(failure)
            : _result?.FetchedAt is { } fetched ? Translations.Format(_result.IsCached ? "ReleaseHighlights.Status.Cached" : "ReleaseHighlights.Status.Updated", fetched.ToLocalTime().ToString("g"))
            : Translations.Get("ReleaseHighlights.Status.Bundled");
    }
}
