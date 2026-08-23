using AFMediaBar.Models;
using Microsoft.Win32;

namespace AFMediaBar.Services;

/// <summary>
/// 负责窗口模式、布局缩放和浮动坐标的读取与保存，并在读取时消费旧组件设置完成迁移。
/// Owns window mode, layout scale, and floating-coordinate persistence while consuming legacy component settings during migration.
/// </summary>
public static class WindowSettingsService
{
    private const string SettingsKeyPath = @"Software\AFMediaBar";
    private static readonly string[] LegacyValueNames =
    [
        "AutoCollapse",
        "EdgeAutoCollapse",
        "ShowArtwork",
        "RoundedArtwork",
        "ArtworkCornerRadius",
        "ShowMediaInfo",
        "TaskbarLayout",
        "TaskbarScalePercent",
        "DisplayScalePercent"
    ];

    public static WindowSettings Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            if (key is null)
            {
                return WindowSettings.Default;
            }

            var legacyScale = ReadDisplayScalePercent(key);
            var settings = new WindowSettings(
                ReadBoolean(key, "HideWhenNoMedia", WindowSettings.Default.HideWhenNoMedia),
                ReadBoolean(key, "AlwaysOnTop", WindowSettings.Default.AlwaysOnTop),
                ReadHostMode(key),
                ReadPlayerLayoutMode(key),
                ReadScalePercent(key, "LengthScalePercent", legacyScale),
                ReadScalePercent(key, "ThicknessScalePercent", legacyScale),
                ReadBoolean(key, "AutoCollapse", WindowSettings.Default.AutoCollapse),
                // schema 3 将边缘折叠拆分为独立布局容器；忽略旧的整窗开关，避免整条窗口与边缘容器同时折叠。
                // Schema 3 models edge collapse as independent layout containers; ignore the legacy whole-window switch so both collapse systems cannot run together.
                false,
                ReadNullableInt(key, "FloatingLeft"),
                ReadNullableInt(key, "FloatingTop"),
                ReadBoolean(key, "ShowArtwork", WindowSettings.Default.ShowArtwork),
                ReadArtworkCornerRadius(key),
                ReadBoolean(key, "ShowMediaInfo", WindowSettings.Default.ShowMediaInfo));

            try
            {
                // 旧组件与旧整窗折叠值只参与本次布局迁移；读取后清理，避免 schema 3 继续出现双重配置来源。
                // Legacy component and whole-window-collapse values are used only for this layout migration, then removed so schema 3 has one source of truth.
                Save(settings);
            }
            catch (Exception exception)
            {
                DiagnosticsLogService.Write("window-settings-migration", exception);
                // 注册表规范化失败不应阻断启动；布局迁移仍可使用内存中的旧值。
                // A registry normalization failure must not block startup; layout migration can still use the in-memory legacy values.
            }

            return settings;
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("window-settings-read", exception);
            return WindowSettings.Default;
        }
    }

    public static void Save(WindowSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue("HideWhenNoMedia", settings.HideWhenNoMedia ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("AlwaysOnTop", settings.AlwaysOnTop ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("HostMode", (int)settings.HostMode, RegistryValueKind.DWord);
        key.SetValue("LayoutMode", (int)settings.LayoutMode, RegistryValueKind.DWord);
        key.SetValue("LengthScalePercent", settings.LengthScalePercent, RegistryValueKind.DWord);
        key.SetValue("ThicknessScalePercent", settings.ThicknessScalePercent, RegistryValueKind.DWord);
        if (settings.FloatingLeft is int left)
        {
            key.SetValue("FloatingLeft", left, RegistryValueKind.DWord);
        }
        else
        {
            key.DeleteValue("FloatingLeft", throwOnMissingValue: false);
        }
        if (settings.FloatingTop is int top)
        {
            key.SetValue("FloatingTop", top, RegistryValueKind.DWord);
        }
        else
        {
            key.DeleteValue("FloatingTop", throwOnMissingValue: false);
        }

        foreach (var legacyName in LegacyValueNames)
        {
            key.DeleteValue(legacyName, throwOnMissingValue: false);
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
