using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services;

/// <summary>滚轮手势的两个槽位：普通滚轮与按住共用按键后的组合滚轮。 / The two wheel slots: the plain wheel and the chord wheel held together with the shared modifier.</summary>
public enum WheelGestureSlot
{
    /// <summary>不按键的普通滚轮。 / The plain wheel with nothing held.</summary>
    Primary,

    /// <summary>按住共用按键的组合滚轮。 / The chord wheel with the shared modifier held.</summary>
    Chord
}

/// <summary>
/// 滚轮提示文案：悬停时说明"滚轮现在会做什么"，滚动后换成刚刚发生的结果。
///
/// 提示是纯策略拼出来的（拿不到依赖注入），因此按当前界面语言取值：语言是进程级状态，唯一写入方是本地化服务。
/// Wheel tooltip text: while hovering it says what the wheel will do, and after scrolling it becomes the result that just happened.
///
/// The text is composed by a pure policy that cannot receive dependency injection and therefore reads the active interface
/// language: the language is process-wide state whose only writer is the localization service.
/// </summary>
public static class WheelTooltipPolicy
{
    /// <summary>
    /// 悬停时的提示。普通滚轮与组合滚轮各说各的，因此用户不需要记住当前按键绑定。
    /// The hint shown while hovering. Plain and chord wheels each state their own action, so the user does not have to remember the
    /// current modifier binding.
    /// </summary>
    /// <param name="slot">要说明的槽位。/ Slot being described.</param>
    /// <param name="modifier">共用的组合键。/ The shared modifier.</param>
    /// <param name="actionName">该槽位当前绑定的动作名。/ Name of the action currently bound to that slot.</param>
    public static string BuildHint(WheelGestureSlot slot, InteractionModifier modifier, string actionName) =>
        slot == WheelGestureSlot.Chord
            ? Translations.Format("Wheel.Hint.Chord", BuildModifierName(modifier), actionName)
            : Translations.Format("Wheel.Hint.Plain", actionName);

    /// <summary>共用按键的显示名；与交互页的下拉框取自同一组共用键，因此设置里选的名字与提示里读到的名字一致。/ Display name of the shared modifier, read from the same shared keys as the interaction page's drop-down so the name picked in the settings matches the one in the tooltip.</summary>
    /// <param name="modifier">共用的组合键。/ The shared modifier.</param>
    public static string BuildModifierName(InteractionModifier modifier) => modifier switch
    {
        InteractionModifier.LeftMouseButton => Translations.Get("Common.Modifier.LeftMouseButton"),
        InteractionModifier.RightMouseButton => Translations.Get("Common.Modifier.RightMouseButton"),
        _ => Translations.Get("Common.Modifier.Shift")
    };

    /// <summary>媒体栏滚轮动作的显示名。/ Display name of a media-bar wheel action.</summary>
    /// <param name="action">滚轮动作。/ Wheel action.</param>
    public static string BuildActionName(WheelAction action) => action switch
    {
        WheelAction.PreviousNext => Translations.Get("Common.PreviousNextTrack"),
        WheelAction.SwitchMediaSource => Translations.Get("Common.SwitchPlayer"),
        WheelAction.InputDevice => Translations.Get("Common.SwitchInputDevice"),
        WheelAction.OutputDevice => Translations.Get("Common.SwitchOutputDevice"),
        WheelAction.CurrentApplicationVolume => Translations.Get("Common.AdjustCurrentMediaVolume"),
        _ => Translations.Get("Common.NotBound")
    };

    /// <summary>托盘滚轮动作的显示名。/ Display name of a tray wheel action.</summary>
    /// <param name="behavior">托盘滚轮行为。/ Tray wheel behavior.</param>
    public static string BuildActionName(TrayWheelBehavior behavior) => behavior switch
    {
        TrayWheelBehavior.SwitchInputDevice => Translations.Get("Common.SwitchInputDevice"),
        TrayWheelBehavior.SwitchOutputDevice => Translations.Get("Common.SwitchOutputDevice"),
        TrayWheelBehavior.AdjustVolume => Translations.Get("Common.AdjustCurrentMediaVolume"),
        _ => Translations.Get("Common.NotBound")
    };

    /// <summary>
    /// 滚动之后的提示：动作名加上它的结果。没有结果（例如切歌时读不到曲名）时只显示动作名，
    /// 而不是留一条空白的"：后面什么都没有"。
    /// The tooltip after scrolling: the action name plus its result. With no result (the title is unreadable while skipping, for
    /// example) only the action name is shown instead of a dangling colon with nothing behind it.
    /// </summary>
    /// <param name="actionName">刚执行的动作名。/ Name of the action just executed.</param>
    /// <param name="detail">结果细节（曲名、媒体名、设备名或音量）。/ Result detail: track title, media name, device name, or volume.</param>
    public static string BuildResult(string actionName, string? detail) =>
        string.IsNullOrWhiteSpace(detail)
            ? actionName
            : Translations.Format("Wheel.Line.WithValue", actionName, detail);

    /// <summary>切歌动作的方向名：向上滚是上一首，向下滚是下一首。/ Direction name for the skip action: scrolling up is previous, scrolling down is next.</summary>
    /// <param name="delta">滚轮增量，正数表示向上滚。/ Wheel delta, where a positive value means scroll up.</param>
    public static string BuildSkipActionName(int delta) =>
        Translations.Get(delta > 0 ? "Common.PreviousTrack" : "Common.NextTrack");

    /// <summary>
    /// 无媒体时在音符上滚动快速启动列表的提示：显示当前候选，与切换输出设备的提示同一种形式。
    ///
    /// 与设备切换一样，用户需要的是"滚一下就看到现在是哪一项"，而不是滚完之后去菜单里核对；候选名字由宿主给出，
    /// 因此这里只负责拼文案，不参与列表状态。
    /// The tooltip for wheeling the quick-launch list over the note while there is no media: it shows the current candidate, in the same shape
    /// as the output-device tooltip.
    ///
    /// As with device switching, what the user needs is "scroll once and see which entry is selected now" rather than checking the menu
    /// afterwards; the host supplies the candidate name, so this only composes the text and holds no list state.
    /// </summary>
    /// <param name="displayName">当前候选的显示名。/ Display name of the current candidate.</param>
    public static string BuildQuickLaunchPreview(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName)
            ? Translations.Get("Panel.QuickLaunch.Title")
            : Translations.Format("Panel.QuickLaunch.Preview", displayName.Trim());

    /// <summary>
    /// 在提示后面附上当前值。悬停时用户既要知道手势会做什么，也要知道它此刻的值，
    /// 例如「普通滚轮：切换输出设备（当前：扬声器）」；没有可读值时只留提示。
    /// Appends the current value to a hint. While hovering the user needs both what the gesture does and where it currently stands,
    /// as in "plain wheel: switch output device (now: Speakers)"; with no readable value only the hint remains.
    /// </summary>
    /// <param name="hint">已经拼好的提示文本。/ Already composed hint text.</param>
    /// <param name="currentValue">当前值（设备名或应用与音量）；不可读时为 null。/ Current value (device name, or application and volume), or null when unreadable.</param>
    public static string BuildHintWithValue(string hint, string? currentValue) =>
        string.IsNullOrWhiteSpace(currentValue)
            ? hint
            : Translations.Format("Wheel.Hint.WithValue", hint, currentValue);
}
