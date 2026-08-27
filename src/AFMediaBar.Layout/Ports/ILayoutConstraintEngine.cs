using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Ports;

/// <summary>
/// Layout mutation boundary used by the editor. The first adapter delegates to
/// the existing Core constraint service; the implementation can move here after
/// the model namespace migration is complete.
/// </summary>
public interface ILayoutConstraintEngine
{
    LayoutMutationResult TrySetBounds(
        LayoutProfile profile,
        string instanceId,
        LayoutGridRect bounds);

    LayoutMutationResult TryMove(
        LayoutProfile profile,
        string instanceId,
        int deltaX,
        int deltaY);

    LayoutMutationResult TryResize(
        LayoutProfile profile,
        string instanceId,
        LayoutEdge edge,
        int delta);
}

public sealed record LayoutMutationResult(
    LayoutProfile? Profile,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Profile is not null && Errors.Count == 0;

    public static LayoutMutationResult Rejected(params string[] errors) =>
        new(null, errors);
}
