using System.Collections.ObjectModel;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Updates;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>快速开始的离线版本资料；读取与取消由页面作用域拥有。</summary>
public sealed class QuickStartViewModel : IDisposable
{
    private readonly ReleaseHighlightsService _releases;
    private readonly LocalizationService _localization;
    private readonly AppLogService _log;
    private CancellationTokenSource? _request;
    private IReadOnlyList<ReleaseHighlights> _catalogue = [];
    private int _generation;
    private bool _isDisposed;

    /// <summary>本地版本资料，按目录顺序呈现。</summary>
    public ObservableCollection<ReleaseHighlightsItem> History { get; } = [];

    public QuickStartViewModel(ReleaseHighlightsService releases, LocalizationService localization, AppLogService log)
    {
        _releases = releases;
        _localization = localization;
        _log = log;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>只读取本地目录；离页或释放后不发布结果。</summary>
    public async Task ActivateAsync()
    {
        Deactivate();
        if (_isDisposed) return;
        var generation = ++_generation;
        var request = _request = new CancellationTokenSource();
        try
        {
            var result = await _releases.ReadInitialAsync(request.Token);
            if (_isDisposed || request.IsCancellationRequested || generation != _generation) return;
            _catalogue = result.Releases;
            Project();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_isDisposed) _log.Warn("Settings", "读取快速开始的本地版本资料失败", exception);
        }
    }

    /// <summary>取消当前读取，保留已经呈现的资料。</summary>
    public void Deactivate()
    {
        _generation++;
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
    }

    private void OnLanguageChanged(object? sender, EventArgs args) { if (!_isDisposed) Project(); }

    private void Project()
    {
        var language = _localization.CurrentLanguage switch
        {
            LocalizationLanguage.TraditionalChinese => "zh-Hant",
            LocalizationLanguage.English => "en",
            LocalizationLanguage.Vietnamese => "vi",
            _ => "zh-Hans"
        };
        History.Clear();
        foreach (var release in _catalogue)
        {
            // 本地明确核实的 1.6 摘要在页面上独立呈现，避免与缓存中的同版本重复。
            if (release.Version.TrimStart('v') == "1.6.0") continue;
            var (text, original) = ReleaseHighlightsPolicy.ResolveText(release, language);
            History.Add(new(release.Version, text.Title, release.ReleaseDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                text.Highlights, release.ReleaseNotesUrl, string.Empty, original));
        }
    }

    /// <summary>释放窗口作用域持有的订阅与读取。</summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _localization.LanguageChanged -= OnLanguageChanged;
        Deactivate();
    }
}
