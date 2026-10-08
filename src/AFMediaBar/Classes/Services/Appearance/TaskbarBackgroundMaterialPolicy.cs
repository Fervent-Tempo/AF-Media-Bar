// Resolves taskbar material colours without owning settings or WPF controls.
using System.Windows.Media;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>任务栏背景材质的可测试呈现策略。 / Testable presentation policy for the taskbar background material.</summary>
public static class TaskbarBackgroundMaterialPolicy
{
    /// <summary>解析材质底色、描边与封面纹理透明度。 / Resolves the material tint, stroke, and artwork-texture opacity.</summary>
    public static TaskbarBackgroundMaterialPresentation Resolve(
        TaskbarBackgroundMaterial material,
        bool highContrast,
        bool usesLightText,
        int opacityPercent,
        TaskbarFrostedStyle style = TaskbarFrostedStyle.Neutral)
    {
        if (material != TaskbarBackgroundMaterial.Frosted)
            return new(Colors.Transparent, Colors.Transparent, 0);

        if (highContrast)
        {
            return usesLightText
                ? new(Color.FromArgb(0xFF, 0x00, 0x00, 0x00), Colors.White, 0)
                : new(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), Colors.Black, 0);
        }

        var opacity = Math.Clamp(
            opacityPercent,
            AppearanceSettings.MinimumTaskbarBackgroundOpacityPercent,
            AppearanceSettings.MaximumTaskbarBackgroundOpacityPercent);
        var alpha = (byte)Math.Round(byte.MaxValue * opacity / 100d, MidpointRounding.AwayFromZero);
        var resolvedStyle = Enum.IsDefined(style) ? style : TaskbarFrostedStyle.Neutral;
        var (red, green, blue, artworkOpacity) = ResolvePalette(resolvedStyle, usesLightText);
        return usesLightText
            ? new(Color.FromArgb(alpha, red, green, blue), Color.FromArgb(0x36, 0xFF, 0xFF, 0xFF), artworkOpacity)
            : new(Color.FromArgb(alpha, red, green, blue), Color.FromArgb(0x2E, 0x00, 0x00, 0x00), artworkOpacity);
    }

    private static (byte Red, byte Green, byte Blue, double ArtworkOpacity) ResolvePalette(
        TaskbarFrostedStyle style,
        bool usesLightText) => (style, usesLightText) switch
    {
        (TaskbarFrostedStyle.Cool, true) => (0x30, 0x39, 0x41, 0.10),
        (TaskbarFrostedStyle.Cool, false) => (0x81, 0x90, 0x9A, 0.08),
        (TaskbarFrostedStyle.Warm, true) => (0x40, 0x3A, 0x35, 0.10),
        (TaskbarFrostedStyle.Warm, false) => (0x95, 0x8A, 0x80, 0.08),
        (TaskbarFrostedStyle.Artwork, true) => (0x38, 0x38, 0x38, 0.24),
        (TaskbarFrostedStyle.Artwork, false) => (0x8E, 0x8E, 0x8E, 0.20),
        (_, true) => (0x38, 0x38, 0x38, 0.10),
        _ => (0x8E, 0x8E, 0x8E, 0.08)
    };
}

/// <summary>任务栏背景材质的呈现结果。 / Presentation result for the taskbar background material.</summary>
public readonly record struct TaskbarBackgroundMaterialPresentation(Color Tint, Color Stroke, double ArtworkOpacity);
