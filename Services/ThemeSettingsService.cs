using Microsoft.Win32;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

internal static class ThemeSettingsService
{
    private const string SettingsKeyPath = @"Software\AFMediaBar";

    internal static ThemeSettings Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            if (key is null)
            {
                return ThemeSettings.Default;
            }

            var mode = key.GetValue("TaskbarForegroundMode") is int modeValue &&
                Enum.IsDefined(typeof(TaskbarForegroundMode), modeValue)
                    ? (TaskbarForegroundMode)modeValue
                    : ThemeSettings.Default.TaskbarForegroundMode;
            var menuThemeMode = key.GetValue("MenuThemeMode") is int menuThemeModeValue &&
                Enum.IsDefined(typeof(MenuThemeMode), menuThemeModeValue)
                    ? (MenuThemeMode)menuThemeModeValue
                    : ThemeSettings.Default.MenuThemeMode;
            var enhancedReadability = key.GetValue("EnhancedTaskbarReadability") switch
            {
                int value => value != 0,
                long value => value != 0,
                _ => ThemeSettings.Default.EnhancedReadability
            };

            return new ThemeSettings(mode, menuThemeMode, enhancedReadability);
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("theme-settings-read", exception);
            return ThemeSettings.Default;
        }
    }

    internal static void Save(ThemeSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(
            "TaskbarForegroundMode",
            (int)settings.TaskbarForegroundMode,
            RegistryValueKind.DWord);
        key.SetValue(
            "MenuThemeMode",
            (int)settings.MenuThemeMode,
            RegistryValueKind.DWord);
        key.SetValue(
            "EnhancedTaskbarReadability",
            settings.EnhancedReadability ? 1 : 0,
            RegistryValueKind.DWord);
    }
}
