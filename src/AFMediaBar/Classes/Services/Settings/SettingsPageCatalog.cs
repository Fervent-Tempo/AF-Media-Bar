// Declares settings page ownership and mode availability without importing WPF views or changing runtime mode settings.
using AFMediaBar.Classes.Models.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>Semantic settings destination with ownership, label, icon name, and sidebar placement.</summary>
public sealed record SettingsPageDefinition(SettingsPageKey Key, string TitleKey, string IconName, bool IsGlobal, bool IsFooter = false);

/// <summary>One source of truth for current page composition and future display-family availability.</summary>
public static class SettingsPageCatalog
{
    private static readonly IReadOnlyList<SettingsPageDefinition> TaskbarPages = Array.AsReadOnly(new[]
    {
        new SettingsPageDefinition(SettingsPageKey.DisplayModes, "Common.Page.DisplayModes", "WindowConsole20", true),
        new SettingsPageDefinition(SettingsPageKey.ScreenAndPlacement, "Common.Page.ScreenAndPlacement", "Desktop24", false),
        new SettingsPageDefinition(SettingsPageKey.Appearance, "Common.Page.TaskbarAppearance", "PaintBrush24", false),
        new SettingsPageDefinition(SettingsPageKey.Interaction, "Common.Page.Interaction", "HandDraw24", false),
        new SettingsPageDefinition(SettingsPageKey.Lyrics, "Common.Page.Lyrics", "TextQuote24", false),
        new SettingsPageDefinition(SettingsPageKey.Components, "Common.Page.Components", "DataHistogram24", false),
        new SettingsPageDefinition(SettingsPageKey.MediaAndNotifications, "Common.Page.MediaAndNotifications", "PuzzlePiece24", true),
        new SettingsPageDefinition(SettingsPageKey.ApplicationAppearance, "Common.Page.ApplicationAppearance", "Color24", true),
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
}
