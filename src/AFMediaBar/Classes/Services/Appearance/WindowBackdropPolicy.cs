using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 计算窗口请求材质在当前系统环境下的有效材质，以及 Accent 模糊路径要用的底色。
/// Resolves the effective window backdrop for the requested mode and current system environment, and the tint the Accent blur path
/// has to use.
/// </summary>
public static class WindowBackdropPolicy
{
    /// <summary>
    /// 按系统能力回退：高对比度一律纯色；云母 Alt 在拿不到它时退到云母，两种系统材质都拿不到时退到 Acrylic。
    ///
    /// 回退链而不是"不支持就纯色"是刻意的：云母 Alt 只在 Windows 11 22621 之后存在，而 22000 那一档已经有云母，
    /// 让用户为了一个新材质在选择与纯色之间二选一是没必要的；Windows 10 上没有系统材质（云母不存在，Acrylic 走 Accent 模糊路径，
    /// 那条路径由窗口自己绘制、在 Windows 10 上可用），因此退到 Acrylic 仍然给到半透明表面，而不是把默认材质变成一块纯色。
    /// 若那条路径在个别系统上失败，适配器会返回"没应用上"，调用方随即退回纯色（`WindowAppearanceService.ApplyWindow`）。
    /// Falls back by system capability: high contrast is always solid; Mica Alt falls back to Mica when it is unavailable, and to
    /// Acrylic when neither system material is available.
    ///
    /// A chain rather than "unsupported means solid" is deliberate: Mica Alt exists only from Windows 11 22621 on, while the 22000
    /// tier already has Mica, so forcing the user to choose between a new material and a solid surface would be pointless. Windows 10
    /// has no system material at all (no Mica, and Acrylic goes through the window-painted Accent blur path, which does work there),
    /// so falling back to Acrylic still yields a translucent surface instead of turning the default material into a slab of colour. If
    /// that path fails on some system the adapter reports "not applied" and the caller falls back to solid
    /// (`WindowAppearanceService.ApplyWindow`).
    /// </summary>
    /// <param name="requested">设置里请求的材质。/ Requested backdrop from the settings.</param>
    /// <param name="highContrast">是否处于高对比度。/ Whether high contrast is active.</param>
    /// <param name="supportsMica">系统是否支持云母（Windows 11 22000+）。/ Whether the system supports Mica (Windows 11 22000+).</param>
    /// <param name="supportsMicaAlt">系统是否支持云母 Alt（Windows 11 22621+）。/ Whether the system supports Mica Alt (Windows 11 22621+).</param>
    public static ApplicationBackdropMode Resolve(
        ApplicationBackdropMode requested,
        bool highContrast,
        bool supportsMica,
        bool supportsMicaAlt)
    {
        if (highContrast)
            return ApplicationBackdropMode.FluentSolid;

        if (requested == ApplicationBackdropMode.MicaAlt && !supportsMicaAlt)
            requested = supportsMica ? ApplicationBackdropMode.Mica : ApplicationBackdropMode.Acrylic;

        if (requested == ApplicationBackdropMode.Mica && !supportsMica)
            return ApplicationBackdropMode.Acrylic;

        return requested;
    }

    /// <summary>
    /// Accent 模糊路径的底色：材质浓度按比例映射成 alpha，颜色跟随主题明暗。
    ///
    /// 这条路径由窗口自己绘制，因此浓度就是它的不透明度；Windows 的系统材质由 DWM 绘制，原生 tint 对它无效。系统材质窗口
    /// 由 `ApplicationAppearanceResources` 发布的同一浓度中性遮罩统一着色，所以用户看到的浓度效果仍然一致。
    /// The tint of the Accent blur path: the material concentration maps onto alpha, and the colour follows the theme brightness.
    ///
    /// This path is painted by the window itself, so the concentration is literally its opacity; the Windows system materials are
    /// painted by DWM and their native tint is unaffected. System-material windows are coloured by the neutral overlay published
    /// from the same concentration in `ApplicationAppearanceResources`, so the user-visible concentration stays consistent.
    /// </summary>
    /// <param name="dark">是否深色主题，决定底色是深灰还是浅白。/ Whether the theme is dark, which picks a dark or a light base colour.</param>
    /// <param name="opacityPercent">材质浓度（0–100）。/ Material concentration (0-100).</param>
    /// <returns>ARGB 底色，直接交给 <c>AccentPolicy.GradientColor</c>。/ ARGB tint handed straight to <c>AccentPolicy.GradientColor</c>.</returns>
    public static int ResolveLegacyTint(bool dark, int opacityPercent)
    {
        var percent = Math.Clamp(
            opacityPercent,
            AppearanceSettings.MinimumBackdropTintOpacityPercent,
            AppearanceSettings.MaximumBackdropTintOpacityPercent);
        var alpha = (int)Math.Round(percent / 100.0 * 255, MidpointRounding.AwayFromZero);
        var rgb = dark ? 0x00202020 : 0x00F9F9F9;
        return (alpha << 24) | rgb;
    }

    /// <summary>
    /// 非激活表面（通知、菜单子项）的近似材质底色。
    ///
    /// 这三种系统材质无法在非激活窗口上由 DWM 绘制，只能用 Acrylic 模糊近似。为保留可辨识的材质差异：
    /// Acrylic 保持基准浓度和较中性的底色；Mica 提高不透明度并轻微提亮/压暗，接近壁纸着色的平面观感；
    /// MicaAlt 再提高不透明度并加深深色/压低浅色底色，接近更重的 Alt 色调。三者都受用户浓度约束，
    /// 100% 时都落到不透明底色，30% 时仍按同一顺序递减。
    /// Approximate material tints for non-activating surfaces (notifications and menu submenus).
    ///
    /// DWM cannot paint these three system materials on a non-activating window, so Acrylic blur is the only available path.
    /// To keep the modes recognisable: Acrylic keeps the base concentration and a neutral tint; Mica raises the opacity and
    /// shifts the colour slightly to read like a flatter wallpaper tint; MicaAlt raises it further and deepens darks / lowers
    /// lights for the heavier Alt tone. All three remain bounded by the user concentration: 100% is always opaque, and at 30%
    /// they still differ in the same order.
    /// </summary>
    public static int ResolveNonActivatingTint(ApplicationBackdropMode mode, bool dark, int opacityPercent)
    {
        var baseTint = ResolveLegacyTint(dark, opacityPercent);
        var baseAlpha = (baseTint >> 24) & 0xFF;
        var extraAlpha = mode switch
        {
            ApplicationBackdropMode.Mica => 40,
            ApplicationBackdropMode.MicaAlt => 70,
            _ => 0
        };
        var alpha = Math.Min(255, baseAlpha + extraAlpha);
        var rgb = mode switch
        {
            ApplicationBackdropMode.Mica => dark ? 0x00181818 : 0x00F3F3F3,
            ApplicationBackdropMode.MicaAlt => dark ? 0x00101010 : 0x00ECECEC,
            _ => baseTint & 0x00FFFFFF
        };
        return (alpha << 24) | rgb;
    }
}
