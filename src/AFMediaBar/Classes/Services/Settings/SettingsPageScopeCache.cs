// Owns context-isolated page DI scopes; the settings-window scope disposes all cached objects and cancellation registrations.
using AFMediaBar.Classes.Models.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace AFMediaBar.Classes.Services.Settings;
/// <summary>Separates page state by environment while retaining common application pages.</summary>
public sealed class SettingsPageScopeCache(IServiceScopeFactory scopeFactory) : IDisposable
{
    private readonly Dictionary<(SettingsPageKey Page, SettingsContext? Context), Entry> _cache = new();
    private SettingsContext _context = SettingsContext.Initial;
    private bool _disposed;

    /// <summary>Cancels work and rejects edits in the departing context before navigation changes.</summary>
    public void DeactivateContext()
    {
        foreach (var pair in _cache.Where(pair => pair.Key.Context is not null)) pair.Value.Context.Deactivate();
    }

    /// <summary>Changes only the editor context, keeping common pages and reactivating valid cached editors.</summary>
    public void SetContext(SettingsContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_context == context) return;
        DeactivateContext();
        _context = context;
        foreach (var pair in _cache.Where(pair => pair.Key.Context == context)) pair.Value.Context.Activate();
    }

    /// <summary>Resolves a DI-owned object in the semantic page's cache scope.</summary>
    public object Get(Type pageType, SettingsPageDefinition definition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SettingsPageCatalog.Find(definition.Key, _context.Mode) is null) throw new InvalidOperationException("Page unavailable in this runtime family.");
        // 混合页可分别缓存全局页面和上下文编辑器；离开环境只取消后者。
        var key = (definition.Key, definition.IsGlobal && !definition.HasModeContent ? null : (SettingsContext?)_context);
        if (_cache.TryGetValue(key, out var cached)) return cached.Page;
        var scope = scopeFactory.CreateScope();
        try
        {
            var context = scope.ServiceProvider.GetRequiredService<SettingsPageContext>();
            context.Initialize(_context);
            var page = scope.ServiceProvider.GetRequiredService(pageType);
            _cache.Add(key, new(scope, context, page));
            return page;
        }
        catch { scope.Dispose(); throw; }
    }

    /// <summary>Disposes every cached page and its subscriptions, after canceling context work.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _cache.Values) entry.Context.Deactivate();
        foreach (var entry in _cache.Values) entry.Scope.Dispose();
        _cache.Clear();
    }
    private sealed record Entry(IServiceScope Scope, SettingsPageContext Context, object Page);
}
