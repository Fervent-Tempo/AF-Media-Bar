namespace AFMediaBar.Models;

internal enum TaskbarForegroundMode
{
    Automatic = 0,
    LightText = 1,
    DarkText = 2
}

internal enum MenuThemeMode
{
    Automatic = 0,
    Light = 1,
    Dark = 2
}

internal readonly record struct ThemeSettings(
    TaskbarForegroundMode TaskbarForegroundMode,
    MenuThemeMode MenuThemeMode,
    bool EnhancedReadability)
{
    internal static ThemeSettings Default { get; } = new(
        TaskbarForegroundMode.Automatic,
        MenuThemeMode.Automatic,
        false);
}
