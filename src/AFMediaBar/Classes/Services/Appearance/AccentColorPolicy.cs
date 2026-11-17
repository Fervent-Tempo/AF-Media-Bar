using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 应用统一的强调色调色板：一个原始强调色加上由它派生的悬停、按下、淡色与"强调色上的文字色"。
/// The application's unified accent palette: the raw accent plus derived hover, pressed, tint, and on-accent text colors.
/// </summary>
public readonly record struct AccentPalette(Color Accent, Color Hover, Color Pressed, Color Tint, Color OnAccent);

/// <summary>
/// 从系统强调色派生整套调色板，不执行资源写入或窗口重应用。
/// Derives the whole accent palette from the system accent color without touching resources or windows.
/// </summary>
public static class AccentColorPolicy
{
    /// <summary>淡色底的透明度（约 12%），用于选中卡片、芯片与图示底片。/ Alpha of the tinted surface (about 12%) used by selected cards, chips, and diagram plates.</summary>
    private const byte TintAlpha = 31;

    /// <summary>悬停与按下档相对原始强调色的混合比例。/ Mix ratio of the hover and pressed shades against the raw accent.</summary>
    private const double HoverMix = 0.12;
    private const double PressedMix = 0.16;

    /// <summary>
    /// 外观页上直接可点的强调色预设。取值覆盖色轮的主要区段而不是 Windows 个性化里的那一组，
    /// 因为系统调色板的颜色会随系统版本与主题变化，写死它反而会让"预设"与用户看到的不一致。
    /// Accent presets offered as swatches on the appearance page. They cover the main sectors of the colour wheel rather than the
    /// set Windows personalisation shows, because the system palette varies with the Windows build and theme, which would make
    /// written-down presets disagree with what the user sees there.
    /// </summary>
    public static IReadOnlyList<Color> Presets { get; } =
    [
        Color.FromRgb(0x00, 0x78, 0xD4),
        Color.FromRgb(0x00, 0x99, 0xBC),
        Color.FromRgb(0x10, 0x7C, 0x10),
        Color.FromRgb(0x87, 0x64, 0x00),
        Color.FromRgb(0xC4, 0x2B, 0x1C),
        Color.FromRgb(0xE3, 0x00, 0x8C),
        Color.FromRgb(0x88, 0x17, 0x98),
        Color.FromRgb(0x7C, 0x3A, 0xED),
        Color.FromRgb(0x4A, 0x54, 0x5E),
        Color.FromRgb(0x76, 0x76, 0x76)
    ];

    /// <summary>
    /// 解析本次要用的原始强调色：选"自定义"且文本可解析时用用户色，"从封面取色"且有封面主色时用封面色，其余情况一律跟随系统。
    /// 无法解析自选色、或当前没有封面时 MUST 回退系统色而不是让调色板变成空值——那会把每一个强调色画刷变成透明。
    /// Resolves the raw accent for this pass: the user's colour when "custom" is selected and its text parses, the cover colour when
    /// "from artwork" is selected and one was extracted, the system accent otherwise. An unparsable custom colour, or a missing cover,
    /// MUST fall back to the system one instead of feeding an empty value into the palette, which would turn every accent brush transparent.
    /// </summary>
    /// <param name="systemAccent">系统原始强调色。/ Raw system accent.</param>
    /// <param name="mode">设置里的强调色来源。/ Accent source from the settings.</param>
    /// <param name="customHex">设置里保存的自选色文本。/ Custom colour text stored in the settings.</param>
    /// <param name="coverAccent">当前封面提取出的强调色；没有封面时为 null。/ Accent extracted from the current artwork, null without one.</param>
    public static Color ResolveRequested(Color systemAccent, AccentColorMode mode, string? customHex, Color? coverAccent = null)
    {
        if (mode == AccentColorMode.Custom && ColorHex.TryParse(customHex, out var custom))
            return custom;

        if (mode == AccentColorMode.FromCover && coverAccent is { } fromCover)
            return fromCover;

        return systemAccent;
    }

    /// <summary>
    /// 由系统强调色、主题明暗与高对比度状态生成调色板。
    /// Builds the palette from the system accent color, theme brightness, and high-contrast state.
    /// </summary>
    /// <param name="systemAccent">系统原始强调色（可带 alpha，函数只取其 RGB）。/ Raw system accent; only its RGB channels are used.</param>
    /// <param name="darkTheme">当前是否为深色主题，决定悬停/按下往哪个方向混合。/ Whether the theme is dark, which decides the direction of the hover and pressed mixes.</param>
    /// <param name="highContrast">是否处于高对比度；此时整套颜色改用系统高亮色。/ Whether high contrast is active, in which case the system highlight colors are used.</param>
    public static AccentPalette Build(Color systemAccent, bool darkTheme, bool highContrast)
    {
        if (highContrast)
        {
            // 高对比度下强调色必须交出控制权：轨道、填充与文字色都取系统高亮色，避免与系统调色板冲突。
            // High contrast takes precedence: track, fill, and text colors come from the system highlight colors so the
            // application cannot fight the system palette.
            var highlight = Opaque(SystemColors.HighlightColor);
            var highlightText = Opaque(SystemColors.HighlightTextColor);
            return new AccentPalette(highlight, highlight, highlight, WithAlpha(highlight, TintAlpha), highlightText);
        }

        var accent = Opaque(systemAccent);
        var toward = darkTheme ? Colors.White : Colors.Black;
        return new AccentPalette(
            accent,
            Mix(accent, toward, HoverMix),
            Mix(accent, darkTheme ? Colors.Black : Colors.White, PressedMix),
            WithAlpha(accent, TintAlpha),
            ResolveOnAccent(accent));
    }

    /// <summary>
    /// 在强调色底上选择可读的文字色：按相对亮度在近黑与纯白之间取对比度更高的一侧。
    /// Picks a readable text color on an accent fill: the higher-contrast side between near-black and white.
    /// </summary>
    public static Color ResolveOnAccent(Color accent)
    {
        var luminance = RelativeLuminance(accent);
        var contrastWithWhite = 1.05 / (luminance + 0.05);
        var dark = Color.FromRgb(0x1C, 0x1C, 0x1C);
        var contrastWithDark = (luminance + 0.05) / (RelativeLuminance(dark) + 0.05);
        return contrastWithWhite >= contrastWithDark ? Colors.White : dark;
    }

    private static Color Opaque(Color color) => Color.FromRgb(color.R, color.G, color.B);

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Mix(Color left, Color right, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(left.R + (right.R - left.R) * ratio),
            (byte)Math.Round(left.G + (right.G - left.G) * ratio),
            (byte)Math.Round(left.B + (right.B - left.B) * ratio));
    }

    private static double RelativeLuminance(Color color) =>
        0.2126 * ToLinear(color.R) + 0.7152 * ToLinear(color.G) + 0.0722 * ToLinear(color.B);

    private static double ToLinear(byte value)
    {
        var channel = value / 255.0;
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
