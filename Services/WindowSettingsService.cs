using AFMediaBar.Models;
using Microsoft.Win32;

namespace AFMediaBar.Services;

/// <summary>
/// 负责窗口模式、布局、缩放、封面和浮动坐标设置的读取、迁移与保存。
/// Owns loading, migration, and persistence for window mode, layout, scale, artwork, and floating coordinates.
/// </summary>
internal static class WindowSettingsService
{
    private const string SettingsKeyPath = @"Software\AFMediaBar";

    internal static WindowSettings Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            var legacyScale = ReadDisplayScalePercent(key);
            return new WindowSettings(
                ReadBoolean(key, "HideWhenNoMedia", WindowSettings.Default.HideWhenNoMedia),
                ReadBoolean(key, "AlwaysOnTop", WindowSettings.Default.AlwaysOnTop),
                ReadHostMode(key),
                ReadPlayerLayoutMode(key),
                ReadScalePercent(key, "LengthScalePercent", legacyScale),
                ReadScalePercent(key, "ThicknessScalePercent", legacyScale),
                ReadBoolean(key, "AutoCollapse", WindowSettings.Default.AutoCollapse),
                ReadBoolean(key, "EdgeAutoCollapse", WindowSettings.Default.EdgeAutoCollapse),
                ReadNullableInt(key, "FloatingLeft"),
                ReadNullableInt(key, "FloatingTop"),
                ReadBoolean(key, "ShowArtwork", WindowSettings.Default.ShowArtwork),
                ReadArtworkCornerRadius(key),
                ReadBoolean(key, "ShowMediaInfo", WindowSettings.Default.ShowMediaInfo));
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("window-settings-read", exception);
            return WindowSettings.Default;
        }
    }

    internal static void Save(WindowSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue("HideWhenNoMedia", settings.HideWhenNoMedia ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("AlwaysOnTop", settings.AlwaysOnTop ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("HostMode", (int)settings.HostMode, RegistryValueKind.DWord);
        key.SetValue("LayoutMode", (int)settings.LayoutMode, RegistryValueKind.DWord);
        key.SetValue("LengthScalePercent", settings.LengthScalePercent, RegistryValueKind.DWord);
        key.SetValue("ThicknessScalePercent", settings.ThicknessScalePercent, RegistryValueKind.DWord);
        key.SetValue("AutoCollapse", settings.AutoCollapse ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("EdgeAutoCollapse", settings.EdgeAutoCollapse ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("ShowArtwork", settings.ShowArtwork ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(
            "ArtworkCornerRadius",
            Math.Clamp(settings.ArtworkCornerRadius, 0, 20),
            RegistryValueKind.DWord);
        key.DeleteValue("RoundedArtwork", throwOnMissingValue: false);
        key.SetValue("ShowMediaInfo", settings.ShowMediaInfo ? 1 : 0, RegistryValueKind.DWord);
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

    private static PlayerLayoutMode ReadPlayerLayoutMode(RegistryKey? key)
    {
        var value = ReadInteger(
            key,
            "LayoutMode",
            ReadInteger(
                key,
                "TaskbarLayout",
                (int)WindowSettings.Default.LayoutMode));
        return Enum.IsDefined(typeof(PlayerLayoutMode), value)
            ? (PlayerLayoutMode)value
            : WindowSettings.Default.LayoutMode;
    }

    private static int ReadDisplayScalePercent(RegistryKey? key)
    {
        var value = ReadInteger(
            key,
            "DisplayScalePercent",
            ReadInteger(
                key,
                "TaskbarScalePercent",
                WindowSettings.Default.LengthScalePercent));
        return Math.Clamp(
            value,
            70,
            125);
    }

    private static int ReadScalePercent(RegistryKey? key, string name, int fallback)
    {
        return Math.Clamp(ReadInteger(key, name, fallback), 70, 125);
    }

    private static int ReadArtworkCornerRadius(RegistryKey? key)
    {
        var value = key?.GetValue("ArtworkCornerRadius") switch
        {
            int number => number,
            long number => (int)number,
            _ => int.MinValue
        };
        if (value != int.MinValue)
        {
            return Math.Clamp(value, 0, 20);
        }

        // Migrate the previous on/off option to the former default radius.
        return ReadBoolean(
            key,
            "RoundedArtwork",
            WindowSettings.Default.ArtworkCornerRadius > 0)
            ? WindowSettings.Default.ArtworkCornerRadius
            : 0;
    }

    private static int ReadInteger(RegistryKey? key, string name, int defaultValue)
    {
        return key?.GetValue(name) switch
        {
            int value => value,
            long value => (int)value,
            _ => defaultValue
        };
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
