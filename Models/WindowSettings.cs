namespace AFMediaBar.Models;

internal enum WindowHostMode
{
    Taskbar = 0,
    Floating = 1
}

internal readonly record struct WindowSettings(
    bool HideWhenNoMedia,
    bool AlwaysOnTop,
    WindowHostMode HostMode,
    bool AutoCollapse,
    bool EdgeAutoCollapse,
    int? FloatingLeft,
    int? FloatingTop)
{
    internal static WindowSettings Default { get; } = new(
        false,
        false,
        WindowHostMode.Taskbar,
        true,
        false,
        null,
        null);
}
