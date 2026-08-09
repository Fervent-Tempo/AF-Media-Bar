namespace TaskbarPlayer.Models;

internal readonly record struct PlacementSettings(
    bool AutomaticPlacement,
    bool PositionLocked,
    int ManualOffsetDip,
    int? CachedAutomaticOffsetDip,
    int? CachedTaskbarWidthDip,
    int? CachedPlayerWidthDip,
    TaskbarAlignment? CachedTaskbarAlignment)
{
    internal static PlacementSettings Default { get; } = new(
        false,
        false,
        8,
        null,
        null,
        null,
        null);
}
