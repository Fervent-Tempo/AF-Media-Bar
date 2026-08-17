namespace AFMediaBar.Models;

internal enum WindowHostMode
{
    Taskbar = 0,
    Floating = 1
}

internal enum PlayerLayoutMode
{
    Automatic = 0,
    Horizontal = 1,
    Vertical = 2
}

internal readonly record struct WindowSettings(
    bool HideWhenNoMedia,
    bool AlwaysOnTop,
    WindowHostMode HostMode,
    PlayerLayoutMode LayoutMode,
    int DisplayScalePercent,
    bool AutoCollapse,
    bool EdgeAutoCollapse,
    int? FloatingLeft,
    int? FloatingTop,
    bool ShowArtwork,
    bool RoundedArtwork,
    bool ShowMediaInfo)
{
    internal static WindowSettings Default { get; } = new(
        false,
        false,
        WindowHostMode.Taskbar,
        PlayerLayoutMode.Automatic,
        100,
        true,
        false,
        null,
        null,
        true,
        true,
        true);
}
