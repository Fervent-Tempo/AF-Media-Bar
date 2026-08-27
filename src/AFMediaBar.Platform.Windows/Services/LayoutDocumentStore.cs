using AFMediaBar.Layout.Ports;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// Platform adapter for layout persistence. The existing static service remains
/// the implementation source during migration; callers now depend on ILayoutStore.
/// </summary>
public sealed class LayoutDocumentStore : ILayoutStore
{
    private readonly Func<(WindowSettings Window, MetricSettings Metrics)> _legacyDefaults;

    public LayoutDocumentStore(Func<(WindowSettings Window, MetricSettings Metrics)> legacyDefaults)
    {
        _legacyDefaults = legacyDefaults ?? throw new ArgumentNullException(nameof(legacyDefaults));
    }

    public Task<LayoutDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var defaults = _legacyDefaults();
        return Task.FromResult(LayoutSettingsService.Load(defaults.Window, defaults.Metrics));
    }

    public Task SaveAsync(LayoutDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LayoutSettingsService.Save(document);
        return Task.CompletedTask;
    }
}
