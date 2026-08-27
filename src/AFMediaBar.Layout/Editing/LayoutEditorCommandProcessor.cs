using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Ports;

namespace AFMediaBar.Layout.Editing;

/// <summary>
/// Applies editor geometry commands to a session through the constraint port.
/// WPF input decides when a command starts; this class decides how it commits.
/// </summary>
public sealed class LayoutEditorCommandProcessor(ILayoutConstraintEngine constraints)
{
    public bool TrySetBounds(LayoutEditorSession session, string instanceId, LayoutGridRect bounds) =>
        Apply(session, profile => constraints.TrySetBounds(profile, instanceId, bounds));

    public bool TryMove(LayoutEditorSession session, string instanceId, int deltaX, int deltaY) =>
        Apply(session, profile => constraints.TryMove(profile, instanceId, deltaX, deltaY));

    public bool TryResize(LayoutEditorSession session, string instanceId, LayoutEdge edge, int delta) =>
        Apply(session, profile => constraints.TryResize(profile, instanceId, edge, delta));

    private bool Apply(LayoutEditorSession session, Func<LayoutProfile, LayoutMutationResult> mutation)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(mutation);
        return session.TryApply(document =>
        {
            var result = mutation(document.Get(session.ProfileKey));
            return result.Succeeded ? document.WithProfile(result.Profile!) : null;
        });
    }
}
