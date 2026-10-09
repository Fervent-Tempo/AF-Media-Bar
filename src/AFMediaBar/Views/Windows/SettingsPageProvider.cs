// Resolves semantic destinations in the view layer; each cache entry owns a DI scope disposed with the settings window.
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Views.Pages;
using AFMediaBar.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Abstractions;

namespace AFMediaBar.Views.Windows;

/// <summary>Context-aware page resolver. View types stay out of the domain page catalog.</summary>
public sealed class SettingsPageProvider(SettingsPageScopeCache cache) : INavigationViewPageProvider, IDisposable
{
    /// <summary>View-layer mapping for semantic navigation and search destinations.</summary>
    public static IReadOnlyDictionary<SettingsPageKey, Type> PageTypes { get; } = new Dictionary<SettingsPageKey, Type>
    {
        [SettingsPageKey.ScreenAndPlacement] = typeof(ScreenAndPlacementPage),
        [SettingsPageKey.Appearance] = typeof(AppearancePage),
        [SettingsPageKey.Interaction] = typeof(InteractionPage),
        [SettingsPageKey.Lyrics] = typeof(LyricsPage),
        [SettingsPageKey.Components] = typeof(ComponentsSettingsPage),
        [SettingsPageKey.MediaAndNotifications] = typeof(ExtraFeaturesPage),
        [SettingsPageKey.Application] = typeof(ApplicationPage),
        [SettingsPageKey.ReleaseHighlights] = typeof(ReleaseHighlightsPage),
        [SettingsPageKey.About] = typeof(AboutPage)
    };
    private SettingsContext _context = SettingsContext.Initial;
    /// <summary>Cancels the departing editors before switching context.</summary>
    public void DeactivateContext() => cache.DeactivateContext();
    /// <summary>Activates the selected environment without rebuilding unrelated common pages.</summary>
    public void SetContext(SettingsContext context) { _context = context; cache.SetContext(context); }
    /// <inheritdoc />
    public object? GetPage(Type pageType)
    {
        pageType = ResolvePageType(pageType);
        var match = PageTypes.FirstOrDefault(pair => pair.Value == pageType);
        if (match.Value is null || SettingsPageCatalog.Find(match.Key, _context.Mode) is not { } definition) return null;
        if (pageType == typeof(AppearancePage))
        {
            var page = (AppearancePage)cache.Get(pageType, definition with { HasModeContent = false });
            var editor = SettingsPageCatalog.IsImplemented(_context.Mode)
                ? (TaskbarAppearanceViewModel)cache.Get(typeof(TaskbarAppearanceViewModel), definition with { IsGlobal = false })
                : null;
            page.SetTaskbarEditor(editor);
            return page;
        }
        return cache.Get(pageType, definition);
    }
    /// <summary>保留调用方的旧页面类型入口，不创建重复设置页。</summary>
    public static Type ResolvePageType(Type pageType) => pageType == typeof(DisplayModesPage) ? typeof(ScreenAndPlacementPage)
        : pageType == typeof(ApplicationAppearancePage) ? typeof(AppearancePage) : pageType;
    /// <summary>Releases cached objects when the settings window closes.</summary>
    public void Dispose() => cache.Dispose();
}
