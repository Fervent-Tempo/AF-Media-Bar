using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Ports;

namespace AFMediaBar.Services;

/// <summary>
/// Transitional platform composition adapter. Layout owns the port; Core owns
/// the current implementation until constraint code is moved completely.
/// </summary>
public sealed class CoreLayoutConstraintAdapter : ILayoutConstraintEngine
{
    public LayoutMutationResult TrySetBounds(LayoutProfile profile, string instanceId, LayoutGridRect bounds) =>
        Convert(LayoutGridConstraintService.TrySetGridBounds(profile, instanceId, bounds));

    public LayoutMutationResult TryMove(LayoutProfile profile, string instanceId, int deltaX, int deltaY) =>
        Convert(LayoutGridConstraintService.TryMove(profile, instanceId, deltaX, deltaY));

    public LayoutMutationResult TryResize(LayoutProfile profile, string instanceId, LayoutEdge edge, int delta) =>
        Convert(LayoutGridConstraintService.TryResize(profile, instanceId, edge, delta));

    private static LayoutMutationResult Convert(LayoutGridEditResult result) =>
        result.Success
            ? new LayoutMutationResult(result.Updated, [])
            : LayoutMutationResult.Rejected(result.Failure.ToString());
}
