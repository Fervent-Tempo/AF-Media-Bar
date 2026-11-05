namespace AFMediaBar.Classes.Services;

/// <summary>
/// 定义 Popup 菜单子项的外观策略：允许程序绘制的 Accent 模糊，不允许 DWM 系统材质，且必须覆盖命中安全表面。
/// Defines the appearance policy for popup menu submenus: the app-painted Accent blur is allowed, DWM system materials are not,
/// and the surface must stay hit-test safe.
/// </summary>
public static class PopupAppearancePolicy
{
    /// <summary>
    /// 指示 Popup 是否允许使用 DWM 系统材质；子菜单固定为 false，因为它们不会激活，DWM 只会给出单色填充。
    /// Indicates whether a popup may use DWM system material; submenus always return false because they do not activate and DWM
    /// would only paint a flat fill.
    /// </summary>
    public static bool AllowsSystemBackdrop => false;

    /// <summary>
    /// 返回菜单不透明回退背景资源键。
    /// Returns the opaque fallback background resource key for menus.
    /// </summary>
    public static string BackgroundResourceKey => "AppMenuBackgroundBrush";
}
