namespace AFMediaBar.Classes.Services;

/// <summary>
/// 定义 Popup 菜单的命中测试安全外观策略。
/// Defines the hit-test-safe appearance policy for Popup menus.
/// </summary>
public static class PopupAppearancePolicy
{
    /// <summary>
    /// 指示 Popup 是否允许使用原生透明材质；菜单固定为 false 以避免空白区穿透点击。
    /// Indicates whether a Popup may use a native transparent material; menus always return false to prevent click-through in blank areas.
    /// </summary>
    public static bool AllowsNativeBackdrop => false;

    /// <summary>
    /// 返回菜单背景资源键。
    /// Returns the menu background resource key.
    /// </summary>
    public static string BackgroundResourceKey => "AppMenuBackgroundBrush";
}
