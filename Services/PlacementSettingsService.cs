using Microsoft.Win32;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

internal static class PlacementSettingsService
{
    private const string SettingsKeyPath = @"Software\AFMediaBar";
    private const string AFShellSettingsKeyPath = @"Software\AFShell\MediaBar";
    private const string TaskbarPlayerSettingsKeyPath = @"Software\TaskbarPlayer";
    private const int CurrentSettingsVersion = 2;

    internal static PlacementSettings Load()
    {
        try
        {
            using var currentKey = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            using var afShellKey = Registry.CurrentUser.OpenSubKey(
                AFShellSettingsKeyPath,
                writable: false);
            using var taskbarPlayerKey = Registry.CurrentUser.OpenSubKey(
                TaskbarPlayerSettingsKeyPath,
                writable: false);
            if (currentKey is null && afShellKey is null && taskbarPlayerKey is null)
            {
                return PlacementSettings.Default;
            }

            var settings = new PlacementSettings(
                ReadBoolean(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "AutomaticPlacement",
                    PlacementSettings.Default.AutomaticPlacement),
                ReadBoolean(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "PositionLocked",
                    PlacementSettings.Default.PositionLocked),
                ReadInteger(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "ManualOffsetDip",
                    PlacementSettings.Default.ManualOffsetDip),
                ReadNullableInteger(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "CachedAutomaticOffsetDip"),
                ReadNullableInteger(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "CachedTaskbarWidthDip"),
                ReadNullableInteger(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "CachedPlayerWidthDip"),
                ReadTaskbarAlignment(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "CachedTaskbarAlignment"));

            var settingsVersion = ReadInteger(
                currentKey,
                afShellKey,
                taskbarPlayerKey,
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
        RegistryKey? afShellKey,
        RegistryKey? taskbarPlayerKey,
        string name,
        bool defaultValue)
    {
        return ReadValue(currentKey, afShellKey, taskbarPlayerKey, name) is int value
            ? value != 0
            : defaultValue;
    }

    private static int ReadInteger(
        RegistryKey? currentKey,
        RegistryKey? afShellKey,
        RegistryKey? taskbarPlayerKey,
        string name,
        int defaultValue)
    {
        return ReadValue(currentKey, afShellKey, taskbarPlayerKey, name) is int value
            ? value
            : defaultValue;
    }

    private static int? ReadNullableInteger(
        RegistryKey? currentKey,
        RegistryKey? afShellKey,
        RegistryKey? taskbarPlayerKey,
        string name)
    {
        return ReadValue(currentKey, afShellKey, taskbarPlayerKey, name) is int value
            ? value
            : null;
    }

    private static TaskbarAlignment? ReadTaskbarAlignment(
        RegistryKey? currentKey,
        RegistryKey? afShellKey,
        RegistryKey? taskbarPlayerKey,
        string name)
    {
        return ReadNullableInteger(currentKey, afShellKey, taskbarPlayerKey, name) is int value &&
            Enum.IsDefined(typeof(TaskbarAlignment), value)
                ? (TaskbarAlignment)value
                : null;
    }

    private static object? ReadValue(
        RegistryKey? currentKey,
        RegistryKey? afShellKey,
        RegistryKey? taskbarPlayerKey,
        string name)
    {
        return currentKey?.GetValue(name) ??
            afShellKey?.GetValue(name) ??
            taskbarPlayerKey?.GetValue(name);
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
