namespace AFMediaBar.Classes.Settings;

/// <summary>Horizontal placement of the media bar on the taskbar.</summary>
public enum TaskbarBarPosition
{
    Start = 0,
    Center = 1,
    End = 2
}

/// <summary>
/// App settings. In-memory only for now; persistence and settings-page wiring come later.
/// </summary>
public class AppSettings
{
    /// <summary>Whether the media bar is docked into the taskbar.</summary>
    public bool TaskbarBarEnabled { get; set; } = true;

    /// <summary>Index of the monitor whose taskbar hosts the bar (see <see cref="Utils.MonitorUtil.GetMonitors"/> order).</summary>
    public int TaskbarBarSelectedMonitor { get; set; }

    /// <summary>Where on the taskbar the bar is placed.</summary>
    public TaskbarBarPosition Position { get; set; } = TaskbarBarPosition.Start;

    /// <summary>Show the blurred album-cover background (like FluentFlyout's TaskbarWidgetBackgroundBlur, off by default).</summary>
    public bool TaskbarBarBackgroundBlur { get; set; }

    /// <summary>Extra manual offset (physical px) applied along the taskbar axis.</summary>
    public int TaskbarBarManualPadding { get; set; }
}

public static class SettingsManager
{
    public static AppSettings Current { get; set; } = new();
}
