namespace AFMediaBar.Models;

public enum TaskbarForegroundMode
{
    Automatic = 0,
    LightText = 1,
    DarkText = 2
}

public enum MenuThemeMode
{
    Automatic = 0,
    Light = 1,
    Dark = 2
}

public readonly record struct ThemeSettings(
    TaskbarForegroundMode TaskbarForegroundMode,
    MenuThemeMode MenuThemeMode,
    bool EnhancedReadability)
{
    public static ThemeSettings Default { get; } = new(
        TaskbarForegroundMode.Automatic,
        MenuThemeMode.Automatic,
        false);
}
