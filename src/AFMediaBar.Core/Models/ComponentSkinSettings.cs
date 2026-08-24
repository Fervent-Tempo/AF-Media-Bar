namespace AFMediaBar.Models;

public static class GlobalThemeTokenIds
{
    public const string Surface = "surface";
    public const string Text = "text";
    public const string Muted = "muted";
    public const string Accent = "accent";
    public const string Stroke = "stroke";
    public const string Shadow = "shadow";
}

public sealed record GlobalTheme(
    string ThemeId,
    IReadOnlyList<string> SemanticTokens,
    string TextFontFamily,
    string DisplayFontFamily,
    int MotionDurationMilliseconds,
    bool ReduceMotion,
    bool HighContrast)
{
    public static GlobalTheme Default { get; } = new(
        "default",
        [
            GlobalThemeTokenIds.Surface,
            GlobalThemeTokenIds.Text,
            GlobalThemeTokenIds.Muted,
            GlobalThemeTokenIds.Accent,
            GlobalThemeTokenIds.Stroke,
            GlobalThemeTokenIds.Shadow
        ],
        "AppTextFontFamily",
        "AppDisplayFontFamily",
        180,
        false,
        false);
}

public sealed record ComponentSkinSize(
    int DefaultWidthDip,
    int DefaultHeightDip,
    int MinWidthDip,
    int MaxWidthDip,
    int MinHeightDip,
    int MaxHeightDip);

/// <summary>
/// Describes a visual skin without carrying UI framework types into Core.
/// </summary>
public sealed record ComponentSkinDefinition(
    string SkinId,
    string DisplayNameResourceKey,
    int Version,
    IReadOnlyList<string> SupportedComponentTypes,
    IReadOnlyList<string> RequiredSemanticTokens,
    ComponentSkinSize? Size,
    string? ResourceKey,
    bool SupportsHorizontal,
    bool SupportsVertical,
    bool SupportsCompact,
    bool SupportsHighContrast,
    bool SupportsReducedMotion)
{
    public bool Supports(string componentType) => SupportedComponentTypes.Any(type =>
        string.Equals(type, componentType, StringComparison.Ordinal));
}

/// <summary>
/// Persisted per-widget assignment. Null fields are intentionally omitted for legacy layouts.
/// </summary>
public sealed record ComponentSkinAssignment(
    string SkinId,
    int Version,
    IReadOnlyDictionary<string, string>? Settings = null);
