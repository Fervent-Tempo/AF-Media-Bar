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
            var enhancedReadability = key.GetValue("EnhancedTaskbarReadability") switch
            {
                int value => value != 0,
                long value => value != 0,
                _ => ThemeSettings.Default.EnhancedReadability
            };

            return new ThemeSettings(mode, enhancedReadability);
        }
        catch
        {
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
            "EnhancedTaskbarReadability",
            settings.EnhancedReadability ? 1 : 0,
            RegistryValueKind.DWord);
    }
}
