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
        new SettingsPageDefinition(SettingsPageKey.ScreenAndPlacement, "Common.Page.ScreenAndPlacement", "Desktop24", true, HasModeContent: true),
        new SettingsPageDefinition(SettingsPageKey.Components, "Common.Page.ContentLayout", "LayoutColumnTwo24", false),
        new SettingsPageDefinition(SettingsPageKey.Appearance, "Common.Page.Appearance", "PaintBrush24", true, HasModeContent: true),
        new SettingsPageDefinition(SettingsPageKey.Lyrics, "Common.Page.Lyrics", "TextQuote24", false),
        new SettingsPageDefinition(SettingsPageKey.Interaction, "Common.Page.Interaction", "HandDraw24", false),
        new SettingsPageDefinition(SettingsPageKey.MediaAndNotifications, "Common.Page.MediaAndNotifications", "PuzzlePiece24", true),
        new SettingsPageDefinition(SettingsPageKey.Application, "Common.Page.Application", "Settings24", true),
        new SettingsPageDefinition(SettingsPageKey.ReleaseHighlights, "Common.Page.ReleaseHighlights", "Sparkle24", true, true),
        new SettingsPageDefinition(SettingsPageKey.About, "Common.Page.About", "QuestionCircle24", true, true)
    });

    /// <summary>Only hosts implemented in this release can become active settings contexts.</summary>
    public static bool IsImplemented(SettingsMode mode) => mode == SettingsMode.Taskbar;
    /// <summary>Returns current-mode pages plus common application destinations; future hosts get no fake specialized controls.</summary>
    public static IReadOnlyList<SettingsPageDefinition> ForMode(SettingsMode mode) =>
        IsImplemented(mode) ? TaskbarPages : TaskbarPages.Where(page => page.IsGlobal).ToArray();
    /// <summary>Finds a semantic destination in the current composition.</summary>
    public static SettingsPageDefinition? Find(SettingsPageKey key, SettingsMode mode) => ForMode(mode).FirstOrDefault(page => page.Key == key);

    /// <summary>旧页面和分组入口仍定位到重组后的唯一编辑位置。</summary>
    public static (SettingsPageKey Page, string GroupId) ResolveDestination(SettingsPageKey page, string groupId) => (page, groupId) switch
    {
        (SettingsPageKey.DisplayModes, "Common.RestLayer" or "Common.HoverLayer" or "Common.FullLayer") => (SettingsPageKey.Components, groupId),
        (SettingsPageKey.DisplayModes, _) => (SettingsPageKey.ScreenAndPlacement, groupId),
        (SettingsPageKey.ApplicationAppearance, _) => (SettingsPageKey.Appearance, groupId),
        (SettingsPageKey.Appearance, "Common.Group.MediaBarWidth" or "Appearance.Group.RestLayout" or "Appearance.Group.InteractionButtons") => (SettingsPageKey.Components, groupId),
        (SettingsPageKey.Lyrics, "Common.Group.LyricsAlignment") => (SettingsPageKey.Components, "Appearance.Group.RestLayout"),
        (SettingsPageKey.Components, "Common.Group.RestLayerComponents") => (SettingsPageKey.Components, "Common.RestLayer"),
        (SettingsPageKey.Interaction, "Common.Group.SharedModifier") => (SettingsPageKey.Interaction, "Interaction.Group.Wheel"),
        _ => (page, groupId)
    };
}
