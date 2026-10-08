// Describes settings presentation only. These identifiers and environment facts are not serialized application settings.
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Models.Settings;

/// <summary>Display families supported by the settings architecture; availability is declared separately.</summary>
public enum SettingsMode
{
    /// <summary>Media embedded in a system taskbar.</summary>
    Taskbar,
    /// <summary>A standalone island host.</summary>
    DynamicIsland,
    /// <summary>A desktop card host.</summary>
    DesktopCard,
    /// <summary>A floating orb host.</summary>
    FloatingBall
}

/// <summary>Immutable, non-persistent mode and monitor context for one settings page instance.</summary>
public sealed record SettingsContext(SettingsMode Mode, string? MonitorDeviceId, LayoutOrientation? Orientation,
    bool IsCompact, bool EnvironmentAvailable)
{
    /// <summary>Initial context before the background environment query completes.</summary>
    public static SettingsContext Initial { get; } = new(SettingsMode.Taskbar, null, null, false, false);
}

/// <summary>Display selection offered by the editor, with its actual taskbar environment.</summary>
public sealed record SettingsMonitorContext(string DeviceId, string DeviceName, bool IsPrimary,
    LayoutOrientation? Orientation, bool IsCompact, bool EnvironmentAvailable);

/// <summary>One complete environment query result; it contains no HWND or UI object.</summary>
public sealed record SettingsEnvironmentSnapshot(SettingsMode RunningMode, IReadOnlyList<SettingsMonitorContext> Monitors);
