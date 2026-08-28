using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Media;

public sealed record ArtworkSettings(
    int CornerRadiusDip = 6,
    bool UseMediaPrimaryColor = false,
    bool OpenSourceOnClick = true) : IComponentSettings
{
    public string TypeId => ComponentTypeIds.Artwork;
}
