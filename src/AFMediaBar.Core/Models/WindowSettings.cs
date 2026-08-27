using AFMediaBar.Layout.Models;

namespace AFMediaBar.Models;

public enum WindowHostMode
{
    Taskbar = 0,
    Floating = 1
}

public readonly record struct WindowSettings(
    bool HideWhenNoMedia,
    bool AlwaysOnTop,
    WindowHostMode HostMode,
    PlayerLayoutMode LayoutMode,
    int LengthScalePercent,
    int ThicknessScalePercent,
    bool AutoCollapse,
    bool EdgeAutoCollapse,
    int? FloatingLeft,
    int? FloatingTop,
    bool ShowArtwork,
    int ArtworkCornerRadius,
    bool ShowMediaInfo)
{
    public static WindowSettings Default { get; } = new(
        false,
        false,
        WindowHostMode.Taskbar,
        PlayerLayoutMode.Automatic,
        100,
        100,
        true,
        false,
        null,
        null,
        true,
        6,
        true);
}
