namespace TaskbarPlayer.Models;

internal readonly record struct PlacementSettings(
    bool AutomaticPlacement,
    bool PositionLocked,
    int ManualOffsetDip)
{
    internal static PlacementSettings Default { get; } = new(true, true, 8);
}
