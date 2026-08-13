using AFMediaBar.Models;
using Microsoft.Win32;

namespace AFMediaBar.Services;

internal static class WindowSettingsService
{
    private const string SettingsKeyPath = @"Software\AFMediaBar";

    internal static WindowSettings Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            return new WindowSettings(
                ReadBoolean(key, "HideWhenNoMedia", WindowSettings.Default.HideWhenNoMedia),
                ReadBoolean(key, "AlwaysOnTop", WindowSettings.Default.AlwaysOnTop),
                ReadHostMode(key),
                ReadBoolean(key, "AutoCollapse", WindowSettings.Default.AutoCollapse),
                ReadBoolean(key, "EdgeAutoCollapse", WindowSettings.Default.EdgeAutoCollapse),
                ReadNullableInt(key, "FloatingLeft"),
                ReadNullableInt(key, "FloatingTop"));
        }
        catch
        {
            return WindowSettings.Default;
        }
    }

    internal static void Save(WindowSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue("HideWhenNoMedia", settings.HideWhenNoMedia ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("AlwaysOnTop", settings.AlwaysOnTop ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("HostMode", (int)settings.HostMode, RegistryValueKind.DWord);
        key.SetValue("AutoCollapse", settings.AutoCollapse ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("EdgeAutoCollapse", settings.EdgeAutoCollapse ? 1 : 0, RegistryValueKind.DWord);
        if (settings.FloatingLeft is int left)
        {
            key.SetValue("FloatingLeft", left, RegistryValueKind.DWord);
        }
        if (settings.FloatingTop is int top)
        {
            key.SetValue("FloatingTop", top, RegistryValueKind.DWord);
        }
    }

    private static bool ReadBoolean(RegistryKey? key, string name, bool defaultValue)
    {
        return key?.GetValue(name) switch
        {
            int value => value != 0,
            long value => value != 0,
            _ => defaultValue
        };
    }

    private static WindowHostMode ReadHostMode(RegistryKey? key)
    {
        var value = key?.GetValue("HostMode") switch
        {
            int number => number,
            long number => (int)number,
            _ => (int)WindowSettings.Default.HostMode
        };
        return Enum.IsDefined(typeof(WindowHostMode), value)
            ? (WindowHostMode)value
            : WindowSettings.Default.HostMode;
    }

    private static int? ReadNullableInt(RegistryKey? key, string name)
    {
        return key?.GetValue(name) switch
        {
            int value => value,
            long value => (int)value,
            _ => null
        };
    }
}
