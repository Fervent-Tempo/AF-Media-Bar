// Declares settings page ownership and mode availability without importing WPF views or changing runtime mode settings.
using AFMediaBar.Classes.Models.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>Semantic settings destination with ownership, label, icon name, and sidebar placement.</summary>
public sealed record SettingsPageDefinition(SettingsPageKey Key, string TitleKey, string IconName, bool IsGlobal, bool IsFooter = false, bool HasModeContent = false);

/// <summary>One source of truth for current page composition and future display-family availability.</summary>
public static class SettingsPageCatalog
{
    private static readonly IReadOnlyList<SettingsPageDefinition> TaskbarPages = Array.AsReadOnly(new[]
    {
        new SettingsPageDefinition(SettingsPageKey.QuickStart, "Settings.Section.Start", "Sparkle24", true),
        new SettingsPageDefinition(SettingsPageKey.ScreenAndPlacement, "Common.Page.ScreenAndPlacement", "Desktop24", true, HasModeContent: true),
        new SettingsPageDefinition(SettingsPageKey.RestState, "Common.Page.RestState", "Layer24", false),
        new SettingsPageDefinition(SettingsPageKey.Appearance, "Settings.Page.BarAppearance", "PaintBrush24", false),
        new SettingsPageDefinition(SettingsPageKey.Components, "Settings.Page.QuickControls", "HandDraw24", false),
        new SettingsPageDefinition(SettingsPageKey.FullPanel, "Settings.Page.FullPanel", "WindowConsole20", false),
        new SettingsPageDefinition(SettingsPageKey.Widgets, "Settings.Page.NoteAndLaunch", "MusicNote220", true),
        new SettingsPageDefinition(SettingsPageKey.Spectrum, "Settings.Page.Spectrum", "DataBarVertical24", true),
        new SettingsPageDefinition(SettingsPageKey.Performance, "Settings.Page.Performance", "Gauge24", true),
        new SettingsPageDefinition(SettingsPageKey.BluetoothBattery, "Settings.Page.BluetoothBattery", "Bluetooth24", true),
        new SettingsPageDefinition(SettingsPageKey.ApplicationAppearance, "Common.Page.ApplicationAppearance", "Color24", true),
        new SettingsPageDefinition(SettingsPageKey.Lyrics, "Settings.Page.TextAndLyrics", "TextQuote24", false),
        new SettingsPageDefinition(SettingsPageKey.Interaction, "Settings.Page.InteractionAndHotkeys", "HandDraw24", false),
        new SettingsPageDefinition(SettingsPageKey.DeviceManagement, "Settings.Page.AudioDevices", "Headphones24", true),
        new SettingsPageDefinition(SettingsPageKey.MediaAndNotifications, "Settings.Page.MediaSource", "MusicNote220", true),
        new SettingsPageDefinition(SettingsPageKey.TrackNotifications, "Settings.Page.TrackNotifications", "Alert24", true),
        new SettingsPageDefinition(SettingsPageKey.Application, "Settings.Page.GeneralAndUpdates", "Settings24", true),
        new SettingsPageDefinition(SettingsPageKey.Configuration, "Settings.Page.Configuration", "Save24", true),
        new SettingsPageDefinition(SettingsPageKey.Diagnostics, "Settings.Page.Diagnostics", "Bug24", true),
        new SettingsPageDefinition(SettingsPageKey.MemoryTools, "Settings.Page.MemoryTools", "ArrowMinimize24", true),
        new SettingsPageDefinition(SettingsPageKey.Experimental, "Common.Page.Experimental", "Beaker24", true),
        new SettingsPageDefinition(SettingsPageKey.ReleaseHighlights, "Common.Page.ReleaseHighlights", "Sparkle24", true, true),
        new SettingsPageDefinition(SettingsPageKey.About, "Common.Page.About", "QuestionCircle24", true, true)
    });

    /// <summary>Only hosts implemented in this release can become active settings contexts.</summary>
    public static bool IsImplemented(SettingsMode mode) => mode is SettingsMode.Taskbar or SettingsMode.FloatingBar;
    /// <summary>设置目标可编辑不表示该宿主已接入运行时。</summary>
    public static bool CanEditProfile(SettingsMode mode) => mode == SettingsMode.Taskbar ||
        mode == SettingsMode.FloatingBar;
    /// <summary>Returns current-mode pages plus common application destinations; future hosts get no fake specialized controls.</summary>
    public static IReadOnlyList<SettingsPageDefinition> ForMode(SettingsMode mode) =>
        CanEditProfile(mode) ? TaskbarPages : TaskbarPages.Where(page => page.IsGlobal).ToArray();
    /// <summary>Finds a semantic destination in the current composition.</summary>
    public static SettingsPageDefinition? Find(SettingsPageKey key, SettingsMode mode) => ForMode(mode).FirstOrDefault(page => page.Key == key);

    /// <summary>旧页面和分组入口仍定位到重组后的唯一编辑位置。</summary>
    public static (SettingsPageKey Page, string GroupId) ResolveDestination(SettingsPageKey page, string groupId) => (page, groupId) switch
    {
        (SettingsPageKey.DisplayModes, "Common.RestLayer") => (SettingsPageKey.RestState, groupId),
        (SettingsPageKey.DisplayModes, "Common.HoverLayer") => (SettingsPageKey.Components, groupId),
        (SettingsPageKey.DisplayModes, "Common.FullLayer") => (SettingsPageKey.FullPanel, groupId),
        (SettingsPageKey.DisplayModes, _) => (SettingsPageKey.ScreenAndPlacement, groupId),
        (SettingsPageKey.Components, "Common.RestLayer" or "Common.Group.ComponentOrder" or "Settings.RestState.Idle") => (SettingsPageKey.RestState, groupId),
        (SettingsPageKey.Appearance, "Common.Group.ThemeAndBackdrop" or "Common.Group.Fonts" or "Settings.Personalization.Accent") => (SettingsPageKey.ApplicationAppearance, groupId),
        (SettingsPageKey.Appearance, "Common.Group.MediaBarText") => (SettingsPageKey.Lyrics, groupId),
        (SettingsPageKey.Appearance, "Appearance.IdleNote.Title") => (SettingsPageKey.Widgets, groupId),
        (SettingsPageKey.Appearance, "Appearance.Group.RestLayout") => (SettingsPageKey.Lyrics, "Common.Group.MediaBarText"),
        (SettingsPageKey.Appearance, "Appearance.Group.InteractionButtons") => (SettingsPageKey.Components, groupId),
        (SettingsPageKey.Components, "Common.Group.MediaBarWidth") => (SettingsPageKey.Appearance, groupId),
        (SettingsPageKey.Components, "Appearance.Group.RestLayout") => (SettingsPageKey.Lyrics, "Common.Group.MediaBarText"),
        (SettingsPageKey.Components, "Common.FullLayer") => (SettingsPageKey.FullPanel, groupId),
        (SettingsPageKey.Lyrics, "Common.Group.LyricsAlignment") => (SettingsPageKey.Lyrics, "Common.Group.MediaBarText"),
        (SettingsPageKey.MediaAndNotifications, "Common.Group.QuickLaunch") => (SettingsPageKey.Widgets, "Widgets.Note"),
        (SettingsPageKey.Components, "Common.Group.RestLayerComponents" or "Common.Group.Widgets") => (SettingsPageKey.Spectrum, "Widgets.Spectrum"),
        (SettingsPageKey.Widgets, "Widgets.Spectrum") => (SettingsPageKey.Spectrum, groupId),
        (SettingsPageKey.Widgets, "Widgets.Performance") => (SettingsPageKey.Performance, groupId),
        (SettingsPageKey.Widgets or SettingsPageKey.DeviceManagement, "Battery.Title") => (SettingsPageKey.BluetoothBattery, groupId),
        (SettingsPageKey.MediaAndNotifications, "Common.Group.TrackChangeNotification") => (SettingsPageKey.TrackNotifications, groupId),
        (SettingsPageKey.Application, "Common.Group.SettingsFile") => (SettingsPageKey.Configuration, groupId),
        (SettingsPageKey.Application, "Common.Group.Diagnostics") => (SettingsPageKey.Diagnostics, groupId),
        (SettingsPageKey.Application, "Memory.Title") => (SettingsPageKey.MemoryTools, groupId),
        (SettingsPageKey.Interaction, "Common.Group.SharedModifier") => (SettingsPageKey.Interaction, "Interaction.Group.Wheel"),
        (SettingsPageKey.Interaction, "Interaction.AudioGestures") => (SettingsPageKey.Interaction, "Audio.Settings.Advanced"),
        (SettingsPageKey.Experimental, "Experimental.Floating.Title") => (SettingsPageKey.ScreenAndPlacement, "Common.Group.ChooseDisplayMode"),
        (SettingsPageKey.Experimental, "Experimental.Vertical.Title") => (SettingsPageKey.ScreenAndPlacement, "Common.Group.ScreenAndPlacement"),
        (SettingsPageKey.Experimental, "Experimental.FullPanel.Title") => (SettingsPageKey.FullPanel, "Common.FullLayer"),
        (SettingsPageKey.Experimental, "Experimental.Audio.Title") => (SettingsPageKey.DeviceManagement, "Common.OutputDevice"),
        (SettingsPageKey.DeviceManagement, "Device.Hotkeys") => (SettingsPageKey.Interaction, "Device.Hotkeys"),
        (SettingsPageKey.Interaction, "Audio.Settings.Title") => (SettingsPageKey.DeviceManagement, "Common.OutputDevice"),
        _ => (page, groupId)
    };
}
