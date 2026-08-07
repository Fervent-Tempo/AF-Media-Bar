using Microsoft.Win32;
using TaskbarPlayer.Models;

namespace TaskbarPlayer.Services;

internal static class PlacementSettingsService
{
    private const string SettingsKeyPath = @"Software\TaskbarPlayer";

    internal static PlacementSettings Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            if (key is null)
            {
                return PlacementSettings.Default;
            }

            return new PlacementSettings(
                ReadBoolean(key, "AutomaticPlacement", PlacementSettings.Default.AutomaticPlacement),
                ReadBoolean(key, "PositionLocked", PlacementSettings.Default.PositionLocked),
                ReadInteger(key, "ManualOffsetDip", PlacementSettings.Default.ManualOffsetDip));
        }
        catch
        {
            return PlacementSettings.Default;
        }
    }

    internal static void Save(PlacementSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue("AutomaticPlacement", settings.AutomaticPlacement ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("PositionLocked", settings.PositionLocked ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("ManualOffsetDip", settings.ManualOffsetDip, RegistryValueKind.DWord);
    }

    private static bool ReadBoolean(RegistryKey key, string name, bool defaultValue)
    {
        return key.GetValue(name) is int value ? value != 0 : defaultValue;
    }

    private static int ReadInteger(RegistryKey key, string name, int defaultValue)
    {
        return key.GetValue(name) is int value ? value : defaultValue;
    }
}
