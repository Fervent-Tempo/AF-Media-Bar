using System.Windows.Media;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Appearance;

/// <summary>
/// 灵动岛表面外观策略：把设置里的表面方案换算成画刷颜色与形态圆角。纯静态、无窗口依赖。
/// Capsule-island surface appearance policy: turns the configured surface style into a brush colour and a shape corner radius.
/// Pure static, with no window dependency.
/// </summary>
public static class CapsuleIslandSurfacePolicy
{
    /// <summary>
    /// 深色主题的表面色：iOS 灵动岛的纯黑底（#FF000000）。
    ///
    /// 浅色主题仍取 <see cref="LightNeutral"/>：纯黑配浅色主题的前景（近黑字）会变成"黑字黑底"完全看不见，所以这处
    /// 只在深色底上与任务栏模式主窗口同源，浅色底保留它自己的中性色。
    /// Dark-theme surface colour: the iOS dynamic island's pure black (#FF000000).
    ///
    /// A light theme keeps <see cref="LightNeutral"/>: pure black against the light theme's foreground (near-black text)
    /// would be black on black, so only the dark surface stays in sync with the taskbar-mode host and the light surface
    /// keeps its own neutral.
    /// </summary>
    private static readonly Color DarkNeutral = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);

    /// <summary>浅色主题的表面色：同上，与任务栏模式主窗口一致。/ Light-theme surface colour, likewise identical to the taskbar-mode host's.</summary>
    private static readonly Color LightNeutral = Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3);

    /// <summary>主题色方案里强调色的混合权重。/ Weight of the accent colour in the theme-tint style.</summary>
    private const double AccentWeight = 0.35;

    /// <summary>
    /// 外观设置 → 表面画刷颜色（返回值 alpha 已包含不透明度）。
    /// Automatic：主题表面色（深色纯黑 #FF000000 / 浅色 #FFF3F3F3），只有 Solid 不随主题变；
    /// Solid    ：恒定深色底 #FF000000；
    /// ThemeTint：表面色与强调色按 35% 强调色 + 65% 表面色混合。
    /// 三者的最终 alpha 均 = round(255 * clamp(opacityPercent, 0, 100) / 100)。
    /// mainTextContrast 之类不做：调用方负责描边。
    /// Appearance settings to the surface brush colour, with the opacity already folded into the returned alpha.
    /// Automatic follows the theme's surface colour (dark pure black #FF000000, light #FFF3F3F3); only Solid is
    /// theme-independent; ThemeTint mixes that surface colour with the accent at 35%.
    /// All three end with alpha = round(255 * clamp(opacityPercent, 0, 100) / 100).
    /// Main-text contrast is deliberately out of scope: the caller owns the outline.
    /// </summary>
    /// <param name="style">设置里的表面方案。/ Surface style from the settings.</param>
    /// <param name="opacityPercent">背景不透明度（百分比，越界会被夹到 0–100）。/ Background opacity in percent, clamped into 0–100.</param>
    /// <param name="isDarkTheme">当前是否为深色主题。/ Whether the current theme is dark.</param>
    /// <param name="accentColor">当前强调色（只取 RGB）。/ Current accent colour; only its RGB channels are used.</param>
    /// <returns>带不透明度的表面颜色。/ The surface colour carrying the requested opacity.</returns>
    public static Color ResolveSurfaceColor(
        PlayerSurfaceStyle style,
        int opacityPercent,
        bool isDarkTheme,
        Color accentColor)
    {
        // 只有 Solid 不跟随主题：它表达的是"恒定深色底"，与 Automatic 的区别必须能在一处看出来。
        // Only Solid ignores the theme: it means "a constant dark surface", which is the one visible difference from Automatic.
        var neutral = style == PlayerSurfaceStyle.Solid || isDarkTheme ? DarkNeutral : LightNeutral;
        var rgb = style == PlayerSurfaceStyle.ThemeTint ? Mix(neutral, accentColor, AccentWeight) : neutral;

        var opacity = Math.Clamp(opacityPercent, 0, 100);
        // 用 AwayFromZero 而不是默认的银行家舍入：50% 必须落到 128，界面上才读得出"一半"。
        // AwayFromZero rather than the default banker's rounding: 50% has to land on 128 so the interface reads as exactly half.
        var alpha = (byte)Math.Round(255.0 * opacity / 100.0, MidpointRounding.AwayFromZero);
        return Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B);
    }

    /// <summary>
    /// 设置里的圆角 DIP → 当前形态实际圆角：clamp(configuredDip, 0, min(width, height) / 2)；
    /// configuredDip 非有限数（NaN/Infinity）时按 0 处理；width/height ≤ 0 时返回 0。
    /// Configured corner radius in DIP to the shape's actual radius: clamp(configuredDip, 0, min(width, height) / 2).
    /// A non-finite configuredDip (NaN/Infinity) counts as 0, and a width or height of zero or less returns 0.
    /// </summary>
    /// <param name="configuredDip">设置里的圆角（DIP）。/ Corner radius from the settings, in DIP.</param>
    /// <param name="width">当前形态宽度（DIP）。/ Width of the current shape, in DIP.</param>
    /// <param name="height">当前形态高度（DIP）。/ Height of the current shape, in DIP.</param>
    /// <returns>可安全用于圆角矩形的半径。/ A radius safe to hand to a rounded rectangle.</returns>
    public static double ResolveCornerRadius(double configuredDip, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        // 半径超过短边的一半就不再是圆角而是畸变，因此上界是短边半长（胶囊态 44 高时恰好是 22）。
        // Past half the shorter side a radius stops being a corner and becomes a distortion, so the bound is half the shorter side
        // (exactly 22 for the 44-DIP capsule).
        var maximum = Math.Min(width, height) / 2;
        return double.IsFinite(configuredDip) ? Math.Clamp(configuredDip, 0, maximum) : 0;
    }

    /// <summary>
    /// 按比例把强调色混进中性底，alpha 丢弃（表面色的不透明度由调用方统一决定）。
    /// Mixes the accent into the neutral surface by ratio, dropping alpha because the caller decides the surface opacity.
    /// </summary>
    private static Color Mix(Color neutral, Color accent, double ratio) => Color.FromRgb(
        (byte)Math.Round(neutral.R + (accent.R - neutral.R) * ratio),
        (byte)Math.Round(neutral.G + (accent.G - neutral.G) * ratio),
        (byte)Math.Round(neutral.B + (accent.B - neutral.B) * ratio));
}
