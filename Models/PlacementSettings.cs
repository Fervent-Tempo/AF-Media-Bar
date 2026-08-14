namespace AFMediaBar.Models;

internal readonly record struct PlacementSettings(
    bool AutomaticPlacement,
    bool PositionLocked,
    bool VerticalPositionLocked,
    int ManualOffsetDip,
    int ManualVerticalOffsetDip,
    int? CachedAutomaticOffsetDip,
    int? CachedTaskbarWidthDip,
    int? CachedPlayerWidthDip,
    TaskbarAlignment? CachedTaskbarAlignment)
{
    internal static PlacementSettings Default { get; } = new(
        false,
        false,
        false,
        8,
        8,
        null,
        null,
        null,
        null);
}
