using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 程序自身图标该用哪一套图形：按"图形自身的颜色"命名，因此映射是反的。
///
/// 两套素材是 `Assets/icon_dark.ico`（深色图形）与 `Assets/icon_white.ico`（白色图形）。深色主题下应用图标坐在深色
/// 标题栏与深色任务栏上，必须用白色图形；浅色主题相反。Windows 不会替应用换色，这一步只能由程序自己做。
/// Which artwork the application's own icon uses. The two assets are named after their own colour, so the mapping is inverted:
/// `Assets/icon_dark.ico` (dark artwork) and `Assets/icon_white.ico` (white artwork). On the dark theme an application icon sits on
/// a dark title bar and a dark taskbar and has to be white; the light theme is the other way round. Windows never recolours an
/// application icon, so the decision belongs to the application.
/// </summary>
public static class AppIconPolicy
{
    /// <summary>深色图形（给浅色背景）。/ Dark artwork, for light backgrounds.</summary>
    public const string DarkArtworkUri = "pack://application:,,,/Assets/icon_dark.ico";

    /// <summary>白色图形（给深色背景）。/ White artwork, for dark backgrounds.</summary>
    public const string LightArtworkUri = "pack://application:,,,/Assets/icon_white.ico";

    /// <summary>
    /// 按生效主题与系统窗口底色选出当前的图形。
    ///
    /// 高对比度不按主题枚举判断：那套配色可能是"黑底白字"也可能是"白底黑字"，只有系统窗口底色能说明当前是哪一种，
    /// 因此这里额外读一个颜色参数，而不是把高对比度一律当成浅色。
    /// Picks the current artwork from the active theme and the system window colour. High contrast is not decided by the theme enum:
    /// that scheme may be white-on-black or black-on-white, and only the system window colour says which one is in effect, so the
    /// colour is passed in instead of treating high contrast as light by default.
    /// </summary>
    /// <param name="theme">当前生效主题。/ The active theme.</param>
    /// <param name="systemWindowColor">系统窗口底色（通常传 <c>SystemColors.WindowColor</c>）。/ System window colour, normally <c>SystemColors.WindowColor</c>.</param>
    /// <returns>图形资源的 pack URI。/ The pack URI of the artwork.</returns>
    public static string ResolveArtworkUri(ApplicationTheme theme, Color systemWindowColor)
    {
        var darkBackground = theme == ApplicationTheme.Dark ||
                             (theme == ApplicationTheme.HighContrast && IsDark(systemWindowColor));
        return darkBackground ? LightArtworkUri : DarkArtworkUri;
    }

    /// <summary>
    /// 颜色是否算深色：按人眼对三通道的敏感度加权后与中灰比较。
    /// Whether a colour counts as dark: the three channels are weighted by human sensitivity and compared against mid grey.
    /// </summary>
    /// <param name="color">待判定的颜色。/ Colour to test.</param>
    public static bool IsDark(Color color) =>
        ((299 * color.R) + (587 * color.G) + (114 * color.B)) / 1000 < 128;

    /// <summary>
    /// 托盘图标该用哪一套图形：托盘坐在**任务栏**上，因此跟的是 Windows 的「系统模式」（`SystemUsesLightTheme`），
    /// 只有读不到时才回退本程序主题。
    ///
    /// 两个表面刻意分开判定：Windows 允许"任务栏浅色 + 应用深色"这类组合，跟错一方就会出现深色图形贴在深色任务栏上
    /// （或白色图形贴在浅色任务栏上），而窗口图标跟的是本程序主题（见 <see cref="ResolveArtworkUri"/>）。
    /// Which artwork the tray icon uses. The tray sits on the **taskbar** and therefore follows the Windows "system mode"
    /// (`SystemUsesLightTheme`), falling back to this application's theme only when it cannot be read.
    ///
    /// The two surfaces are decided separately on purpose: Windows allows a light taskbar with dark applications, and following the
    /// wrong one leaves dark artwork on a dark taskbar (or white artwork on a light one), while window artwork follows this
    /// application's theme (see <see cref="ResolveArtworkUri"/>).
    /// </summary>
    /// <param name="systemTheme">Windows 的系统模式（任务栏）。/ The Windows system mode (taskbar).</param>
    /// <param name="applicationTheme">本程序生效的应用主题，仅在系统模式读不到时使用。/ The application theme in effect, used only when the system mode is unreadable.</param>
    /// <param name="systemWindowColor">系统窗口底色。/ The system window colour.</param>
    /// <returns>图形资源的 pack URI。/ The pack URI of the artwork.</returns>
    public static string ResolveTrayArtworkUri(
        WindowsThemeDetector.ThemeMode systemTheme,
        ApplicationTheme applicationTheme,
        Color systemWindowColor) => ResolveArtworkUri(
            systemTheme switch
            {
                WindowsThemeDetector.ThemeMode.Dark => ApplicationTheme.Dark,
                WindowsThemeDetector.ThemeMode.Light => ApplicationTheme.Light,
                _ => applicationTheme
            },
            systemWindowColor);
}
