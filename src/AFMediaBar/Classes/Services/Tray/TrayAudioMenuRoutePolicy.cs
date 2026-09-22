namespace AFMediaBar.Classes.Services;

/// <summary>托盘左键音频菜单的宿主。/ Host that serves the tray left-click audio menu.</summary>
public enum TrayAudioMenuRoute
{
    /// <summary>任务栏媒体栏宿主。/ The taskbar media-bar host.</summary>
    TaskbarHost,

    /// <summary>灵动岛宿主。/ The dynamic-island (capsule) host.</summary>
    IslandHost,

    /// <summary>托盘音频浮窗：两个媒体栏宿主都不存在时的兜底入口。/ The tray audio flyout: the fallback when neither media-bar host exists.</summary>
    TrayAudioFlyout
}

/// <summary>
/// 托盘左键的「输出设备菜单 / 当前应用音量菜单」该由谁打开。
/// Decides who opens the tray left-click "output device" and "current application volume" menus.
///
/// 这条判定独立成纯逻辑，是因为它曾经是一处静默失效：宿主在任务栏媒体栏列表为空时直接返回，
/// 于是灵动岛模式下点击托盘左键完全没有反应，而且不留下任何痕迹。判定的唯一契约是"永远有一个真实落点"。
/// This decision is pure logic of its own because it used to be a silent failure: the host returned as soon as the taskbar
/// media-bar list was empty, so in dynamic-island mode a tray left-click did nothing at all and left no trace behind. The
/// single contract of the decision is "there is always a real destination".
/// </summary>
public static class TrayAudioMenuRoutePolicy
{
    /// <summary>
    /// 解析音频菜单的宿主：任务栏宿主优先（与既有行为一致），其次是灵动岛宿主，都没有时落到托盘音频浮窗。
    /// Resolves the audio menu's host: the taskbar host wins (matching the existing behaviour), the island host is next,
    /// and the tray audio flyout is the destination when neither exists.
    /// </summary>
    /// <param name="hasTaskbarHost">当前是否存在任务栏媒体栏宿主。/ Whether a taskbar media-bar host currently exists.</param>
    /// <param name="hasIslandHost">当前是否存在灵动岛宿主。/ Whether the dynamic-island host currently exists.</param>
    public static TrayAudioMenuRoute Resolve(bool hasTaskbarHost, bool hasIslandHost)
    {
        if (hasTaskbarHost)
            return TrayAudioMenuRoute.TaskbarHost;

        return hasIslandHost ? TrayAudioMenuRoute.IslandHost : TrayAudioMenuRoute.TrayAudioFlyout;
    }
}
