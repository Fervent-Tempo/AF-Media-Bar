namespace AFMediaBar.Models;

internal enum TaskbarForegroundMode
{
    Automatic = 0,
    LightText = 1,
    DarkText = 2
}

internal readonly record struct ThemeSettings(
    TaskbarForegroundMode TaskbarForegroundMode,
    bool EnhancedReadability)
{
    internal static ThemeSettings Default { get; } = new(
        TaskbarForegroundMode.Automatic,
        false);
}
