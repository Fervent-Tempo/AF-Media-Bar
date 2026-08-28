using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Media;

public enum MediaTextContentKind { Title = 0, Artist = 1, TitleAndArtist = 2 }

public sealed record MediaTextSettings(
    MediaTextContentKind TextKind = MediaTextContentKind.Title,
    bool EnableMarquee = true,
    int FontSizeDip = 14,
    int MaxLines = 1) : IComponentSettings
{
    public string TypeId => ComponentTypeIds.MediaText;
}
