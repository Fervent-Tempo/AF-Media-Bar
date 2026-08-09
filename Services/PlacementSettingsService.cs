using Microsoft.Win32;
using TaskbarPlayer.Models;

namespace TaskbarPlayer.Services;

internal static class PlacementSettingsService
{
    private const string SettingsKeyPath = @"Software\AFShell\MediaBar";
    private const string LegacySettingsKeyPath = @"Software\TaskbarPlayer";
    private const int CurrentSettingsVersion = 2;

    internal static PlacementSettings Load()
    {
        try
        {
            using var currentKey = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            using var legacyKey = Registry.CurrentUser.OpenSubKey(
                LegacySettingsKeyPath,
                writable: false);
            if (currentKey is null && legacyKey is null)
            {
                return PlacementSettings.Default;
            }

            var settings = new PlacementSettings(
                ReadBoolean(
                    currentKey,
                    legacyKey,
                    "AutomaticPlacement",
                    PlacementSettings.Default.AutomaticPlacement),
                ReadBoolean(
                    currentKey,
                    legacyKey,
                    "PositionLocked",
                    PlacementSettings.Default.PositionLocked),
                ReadInteger(
                    currentKey,
                    legacyKey,
                    "ManualOffsetDip",
                    PlacementSettings.Default.ManualOffsetDip),
                ReadNullableInteger(currentKey, legacyKey, "CachedAutomaticOffsetDip"),
                ReadNullableInteger(currentKey, legacyKey, "CachedTaskbarWidthDip"),
                ReadNullableInteger(currentKey, legacyKey, "CachedPlayerWidthDip"),
                ReadTaskbarAlignment(currentKey, legacyKey, "CachedTaskbarAlignment"));

            var settingsVersion = ReadInteger(
                currentKey,
                legacyKey,
                "PlacementSettingsVersion",
                0);
            if (settingsVersion < CurrentSettingsVersion && settings.AutomaticPlacement)
            {
                settings = settings with
                {
                    AutomaticPlacement = false,
                    PositionLocked = false,
                    ManualOffsetDip = settings.CachedAutomaticOffsetDip ??
                        settings.ManualOffsetDip
                };
            }

            if (currentKey is null ||
                settingsVersion < CurrentSettingsVersion ||
                HasMissingValues(currentKey))
            {
                try
                {
                    Save(settings);
                }
                catch
                {
                    // Loading legacy settings should still succeed if migration cannot write.
                }
            }

            return settings;
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
        key.SetValue("PlacementSettingsVersion", CurrentSettingsVersion, RegistryValueKind.DWord);
        WriteNullableInteger(key, "CachedAutomaticOffsetDip", settings.CachedAutomaticOffsetDip);
        WriteNullableInteger(key, "CachedTaskbarWidthDip", settings.CachedTaskbarWidthDip);
        WriteNullableInteger(key, "CachedPlayerWidthDip", settings.CachedPlayerWidthDip);
        WriteNullableInteger(
            key,
            "CachedTaskbarAlignment",
            settings.CachedTaskbarAlignment is TaskbarAlignment alignment ? (int)alignment : null);
    }

    private static bool ReadBoolean(
        RegistryKey? currentKey,
        RegistryKey? legacyKey,
        string name,
        bool defaultValue)
    {
        return ReadValue(currentKey, legacyKey, name) is int value
            ? value != 0
            : defaultValue;
    }

    private static int ReadInteger(
        RegistryKey? currentKey,
        RegistryKey? legacyKey,
        string name,
        int defaultValue)
    {
        return ReadValue(currentKey, legacyKey, name) is int value ? value : defaultValue;
    }

    private static int? ReadNullableInteger(
        RegistryKey? currentKey,
        RegistryKey? legacyKey,
        string name)
    {
        return ReadValue(currentKey, legacyKey, name) is int value ? value : null;
    }

    private static TaskbarAlignment? ReadTaskbarAlignment(
        RegistryKey? currentKey,
        RegistryKey? legacyKey,
        string name)
    {
        return ReadNullableInteger(currentKey, legacyKey, name) is int value &&
            Enum.IsDefined(typeof(TaskbarAlignment), value)
                ? (TaskbarAlignment)value
                : null;
    }

    private static object? ReadValue(
        RegistryKey? currentKey,
        RegistryKey? legacyKey,
        string name)
    {
        return currentKey?.GetValue(name) ?? legacyKey?.GetValue(name);
    }

    private static bool HasMissingValues(RegistryKey key)
    {
        return key.GetValue("AutomaticPlacement") is not int ||
            key.GetValue("PositionLocked") is not int ||
            key.GetValue("ManualOffsetDip") is not int ||
            key.GetValue("PlacementSettingsVersion") is not int;
    }

    private static void WriteNullableInteger(RegistryKey key, string name, int? value)
    {
        if (value.HasValue)
        {
            key.SetValue(name, value.Value, RegistryValueKind.DWord);
        }
        else
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}
