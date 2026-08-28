using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Ports;
using AFMediaBar.Layout.Widgets;
using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Services;

/// <summary>
/// Transitional platform composition adapter. Layout owns the port; Core owns
/// the current implementation until constraint code is moved completely.
/// </summary>
public sealed class CoreLayoutConstraintAdapter(IComponentSettingsMapper? settingsMapper = null) : ILayoutConstraintEngine
{
    private readonly IComponentSettingsMapper? _settingsMapper = settingsMapper;

    public LayoutMutationResult TrySetBounds(LayoutProfile profile, string instanceId, LayoutGridRect bounds) =>
        Convert(LayoutGridConstraintService.TrySetGridBounds(profile, instanceId, bounds, _settingsMapper));

    public LayoutMutationResult TryMove(LayoutProfile profile, string instanceId, int deltaX, int deltaY) =>
        Convert(LayoutGridConstraintService.TryMove(profile, instanceId, deltaX, deltaY, _settingsMapper));

    public LayoutMutationResult TryResize(LayoutProfile profile, string instanceId, LayoutEdge edge, int delta) =>
        Convert(LayoutGridConstraintService.TryResize(profile, instanceId, edge, delta, _settingsMapper));

    private static LayoutMutationResult Convert(LayoutGridEditResult result) =>
        result.Success
            ? new LayoutMutationResult(result.Updated, [])
            : LayoutMutationResult.Rejected(result.Failure.ToString());
}
