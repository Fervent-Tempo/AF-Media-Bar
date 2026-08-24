using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// Core-only skin metadata and fallback rules. It never creates UI resources.
/// </summary>
public static class ComponentSkinCatalog
{
    public const string DefaultSkinId = "default";
    public const string ExampleSkinId = "example.play-pause";

    private static readonly IReadOnlyList<ComponentSkinDefinition> Definitions =
    [
        new(
            DefaultSkinId,
            "Settings.Skin.DefaultName",
            1,
            [
                BuiltInWidgetTypeIds.Artwork,
                BuiltInWidgetTypeIds.MediaText,
                BuiltInWidgetTypeIds.MediaSource,
                BuiltInWidgetTypeIds.Command,
                BuiltInWidgetTypeIds.Metrics,
                BuiltInWidgetTypeIds.Spectrum,
                BuiltInWidgetTypeIds.Separator
            ],
            [
                GlobalThemeTokenIds.Text,
                GlobalThemeTokenIds.Muted,
                GlobalThemeTokenIds.Accent
            ],
            null,
            null,
            true,
            true,
            true,
            true,
            true),
        new(
            ExampleSkinId,
            "Settings.Skin.ExamplePlayPauseName",
            1,
            [BuiltInWidgetTypeIds.Command],
            [
                GlobalThemeTokenIds.Surface,
                GlobalThemeTokenIds.Text,
                GlobalThemeTokenIds.Accent,
                GlobalThemeTokenIds.Stroke
            ],
            new ComponentSkinSize(36, 36, 20, 96, 20, 96),
            "PlayPauseExampleSkinStyle",
            true,
            true,
            true,
            true,
            true)
    ];

    public static IReadOnlyList<ComponentSkinDefinition> All => Definitions;

    public static IReadOnlyList<ComponentSkinDefinition> ForComponent(string componentType) =>
        Definitions.Where(definition => definition.Supports(componentType)).ToArray();

    public static bool TryGet(string skinId, out ComponentSkinDefinition definition)
    {
        definition = Definitions.FirstOrDefault(item =>
            string.Equals(item.SkinId, skinId, StringComparison.Ordinal))!;
        return definition is not null;
    }

    public static ComponentSkinAssignment? Normalize(
        string componentType,
        string? skinId,
        int? version,
        IReadOnlyDictionary<string, string>? settings)
    {
        if (string.IsNullOrWhiteSpace(skinId) ||
            !TryGet(skinId, out var definition) ||
            !definition.Supports(componentType))
        {
            return null;
        }

        var requestedVersion = version ?? definition.Version;
        if (requestedVersion != definition.Version)
        {
            return null;
        }

        return new ComponentSkinAssignment(
            definition.SkinId,
            definition.Version,
            settings is null
                ? null
                : settings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }
}
