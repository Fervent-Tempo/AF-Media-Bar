using Microsoft.Win32;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 从注册表读取、迁移并保存任务栏组件的位置设置。
/// Reads, migrates, and saves taskbar component placement settings in the registry.
/// </summary>
public static class PlacementSettingsService
{
    private const string SettingsKeyPath = @"Software\AFMediaBar";
    private const int CurrentSettingsVersion = 2;

    public static PlacementSettings Load()
    {
        try
        {
            using var currentKey = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            if (currentKey is null)
            {
                return PlacementSettings.Default;
            }

            var settings = new PlacementSettings(
                ReadBoolean(
                    currentKey,
                    "AutomaticPlacement",
                    PlacementSettings.Default.AutomaticPlacement),
                ReadBoolean(
                    currentKey,
                    "PositionLocked",
                    PlacementSettings.Default.PositionLocked),
                ReadBoolean(
                    currentKey,
                    "VerticalPositionLocked",
                    PlacementSettings.Default.VerticalPositionLocked),
                ReadInteger(
                    currentKey,
                    "ManualOffsetDip",
                    PlacementSettings.Default.ManualOffsetDip),
                ReadInteger(
                    currentKey,
                    "ManualVerticalOffsetDip",
                    PlacementSettings.Default.ManualVerticalOffsetDip),
                Math.Clamp(
                    ReadInteger(
                        currentKey,
                        "TaskbarTopOffsetDip",
                        PlacementSettings.Default.TaskbarTopOffsetDip),
                    -20,
                    20),
                ReadNullableInteger(
                    currentKey,
                    "CachedAutomaticOffsetDip"),
                ReadNullableInteger(
                    currentKey,
                    "CachedTaskbarWidthDip"),
                ReadNullableInteger(
                    currentKey,
                    "CachedPlayerWidthDip"),
                ReadTaskbarAlignment(
                    currentKey,
                    "CachedTaskbarAlignment"));

            var settingsVersion = ReadInteger(
                currentKey,
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

            if (settingsVersion < CurrentSettingsVersion ||
                HasMissingValues(currentKey))
            {
                try
                {
                    Save(settings);
                }
                catch (Exception exception)
                {
                    DiagnosticsLogService.Write("placement-settings-migration", exception);
                    // 迁移写入失败时仍使用已读取的旧设置，避免阻断启动。
                    // Keep the loaded legacy settings when migration cannot write, so startup continues.
                }
            }

            return settings;
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("placement-settings-read", exception);
            return PlacementSettings.Default;
        }
    }

    public static void Save(PlacementSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue("AutomaticPlacement", settings.AutomaticPlacement ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("PositionLocked", settings.PositionLocked ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(
            "VerticalPositionLocked",
            settings.VerticalPositionLocked ? 1 : 0,
            RegistryValueKind.DWord);
        key.SetValue("ManualOffsetDip", settings.ManualOffsetDip, RegistryValueKind.DWord);
        key.SetValue(
            "ManualVerticalOffsetDip",
            settings.ManualVerticalOffsetDip,
            RegistryValueKind.DWord);
        key.SetValue(
            "TaskbarTopOffsetDip",
            settings.TaskbarTopOffsetDip,
            RegistryValueKind.DWord);
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
        string name,
        bool defaultValue)
    {
        return currentKey?.GetValue(name) is int value
            ? value != 0
            : defaultValue;
    }

    private static int ReadInteger(
        RegistryKey? currentKey,
        string name,
        int defaultValue)
    {
        return currentKey?.GetValue(name) is int value
            ? value
            : defaultValue;
    }

    private static int? ReadNullableInteger(
        RegistryKey? currentKey,
        string name)
    {
        return currentKey?.GetValue(name) is int value
            ? value
            : null;
    }

    private static TaskbarAlignment? ReadTaskbarAlignment(
        RegistryKey? currentKey,
        string name)
    {
        return ReadNullableInteger(currentKey, name) is int value &&
            Enum.IsDefined(typeof(TaskbarAlignment), value)
                ? (TaskbarAlignment)value
                : null;
    }

    private static bool HasMissingValues(RegistryKey key)
    {
        return key.GetValue("AutomaticPlacement") is not int ||
            key.GetValue("PositionLocked") is not int ||
            key.GetValue("ManualOffsetDip") is not int ||
            key.GetValue("TaskbarTopOffsetDip") is not int ||
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
