using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Media;

public sealed record MediaSourceSettings(int FontSizeDip = 11, int MaxLines = 1) : IComponentSettings
{
    public string TypeId => ComponentTypeIds.MediaSource;
}
