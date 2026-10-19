// 按功能归组的设置契约；保持既有类型全名、枚举值及默认语义。
using AFMediaBar.Classes.Models;
using System.IO;

namespace AFMediaBar.Classes.Settings;


/// <summary>播放器表面滚轮可绑定的媒体或音频动作。 / Media or audio action bindable to player-surface wheel input.</summary>
public enum WheelAction
{
    PreviousNext = 0,
    CurrentApplicationVolume = 1,
    OutputDevice = 2,
    SwitchMediaSource = 3,

    /// <summary>不绑定：滚轮在媒体栏上不做任何事（提示仍会说明"已禁用"）。成员值参与序列化，因此只能追加。 / Not bound: the wheel does nothing over the media bar, while the tooltip still states that it is disabled. Member values take part in serialization, so this may only be appended.</summary>
    Disabled = 4,
    InputDevice = 5
}

/// <summary>单击通知区域图标时执行的动作。 / Action performed when the notification-area icon is clicked.</summary>
public enum TrayClickAction
{
    /// <summary>禁用托盘左键单击动作。/ Disables the tray icon's left-click action.</summary>
    None = 0,
    OpenSettings = 1,
    OpenAudioControl = 2,
    OpenContextMenu = 3,

    /// <summary>打开输出设备菜单。它与音频控制浮窗是两条不同路径：前者只换设备，后者带音量与设备列表。 / Opens the output-device menu. It is a different path from the audio flyout: this one only switches devices, the flyout also carries volume.</summary>
    OpenOutputDeviceMenu = 4,

    /// <summary>打开当前应用音量菜单。 / Opens the current application's volume menu.</summary>
    OpenCurrentAppVolumeMenu = 5,
    OpenInputDeviceMenu = 6
}

/// <summary>任务栏静置内容的点击结果。 / Result of clicking taskbar rest-layer content.</summary>
public enum PlayerClickAction
{
    TogglePlayPause = 0,
    ActivateSource = 1,

    /// <summary>打开完整层。 / Opens the full layer.</summary>
    OpenFullPanel = 2,

    /// <summary>不绑定：点击该区域不做任何事。成员值参与序列化，因此只能追加。 / Not bound: clicking that region does nothing. Member values take part in serialization, so this may only be appended.</summary>
    Disabled = 3
}

/// <summary>普通滚轮映射切换到组合映射时使用的共享修饰键。 / Shared modifier that switches plain wheel input to its chord mapping.</summary>
public enum InteractionModifier
{
    Shift = 0,
    LeftMouseButton = 1,
    RightMouseButton = 2
}

/// <summary>任务栏播放器和托盘图标共用的点击与滚轮绑定。 / Click and wheel bindings shared by the taskbar player and tray icon.</summary>
public readonly record struct GlobalInteractionSettings(
    PlayerClickAction ArtworkClickAction,
    PlayerClickAction TextClickAction,
    WheelAction PrimaryWheelAction,
    InteractionModifier Modifier,
    WheelAction ChordWheelAction,
    TrayClickAction TrayClickAction,
    TrayWheelBehavior TrayPrimaryWheelAction,
    TrayWheelBehavior TrayChordWheelAction)
{
    /// <summary>是否显示媒体栏与托盘的滚轮操作提示；托盘音频状态和滚动结果仍可见。/ Whether to show wheel action hints on the bar and tray; tray audio status and wheel results remain visible.</summary>
    public bool ShowWheelTooltips { get; init; } = true;
    public AudioDeviceSwitchTarget OutputDeviceClickTarget { get; init; }
    public AudioDeviceSwitchTarget OutputDeviceWheelTarget { get; init; }
    public AudioDeviceSwitchTarget InputDeviceClickTarget { get; init; }
    public AudioDeviceSwitchTarget InputDeviceWheelTarget { get; init; }


    /// <summary>
    /// 悬停封面时的放大方式。旧设置文件没有该字段时读作 <see cref="ArtworkHoverMode.Off"/>（保持紧凑 1.1 倍放大）。
    /// How the artwork behaves on hover. Settings files that predate the field read as <see cref="ArtworkHoverMode.Off"/>
    /// (the compact 1.1× zoom stays).
    /// </summary>
    public ArtworkHoverMode ArtworkHoverMode { get => _artworkHoverMode ?? ArtworkHoverMode.Off; init => _artworkHoverMode = value; }

    private readonly ArtworkHoverMode? _artworkHoverMode;

    public static GlobalInteractionSettings Default { get; } = new(
        PlayerClickAction.TogglePlayPause,
        PlayerClickAction.ActivateSource,
        WheelAction.PreviousNext,
        InteractionModifier.Shift,
        WheelAction.SwitchMediaSource,
        TrayClickAction.OpenAudioControl,
        TrayWheelBehavior.SwitchOutputDevice,
        TrayWheelBehavior.AdjustVolume);

    public GlobalInteractionSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            ArtworkClickAction = Enum.IsDefined(ArtworkClickAction) ? ArtworkClickAction : defaults.ArtworkClickAction,
            TextClickAction = Enum.IsDefined(TextClickAction) ? TextClickAction : defaults.TextClickAction,
            PrimaryWheelAction = Enum.IsDefined(PrimaryWheelAction) ? PrimaryWheelAction : defaults.PrimaryWheelAction,
            Modifier = Enum.IsDefined(Modifier) ? Modifier : defaults.Modifier,
            ChordWheelAction = Enum.IsDefined(ChordWheelAction) ? ChordWheelAction : defaults.ChordWheelAction,
            TrayClickAction = TrayClickAction is TrayClickAction.OpenAudioControl
                or TrayClickAction.None
                or TrayClickAction.OpenSettings
                or TrayClickAction.OpenContextMenu
                or TrayClickAction.OpenOutputDeviceMenu
                or TrayClickAction.OpenCurrentAppVolumeMenu
                or TrayClickAction.OpenInputDeviceMenu
                ? TrayClickAction
                : defaults.TrayClickAction,
            TrayPrimaryWheelAction = NormalizeTrayWheelAction(TrayPrimaryWheelAction, defaults.TrayPrimaryWheelAction),
            TrayChordWheelAction = NormalizeTrayWheelAction(TrayChordWheelAction, defaults.TrayChordWheelAction),
            ArtworkHoverMode = Enum.IsDefined(ArtworkHoverMode) ? ArtworkHoverMode : defaults.ArtworkHoverMode,
            OutputDeviceClickTarget = NormalizeDeviceTarget(OutputDeviceClickTarget),
            OutputDeviceWheelTarget = NormalizeDeviceTarget(OutputDeviceWheelTarget),
            InputDeviceClickTarget = NormalizeDeviceTarget(InputDeviceClickTarget),
            InputDeviceWheelTarget = NormalizeDeviceTarget(InputDeviceWheelTarget)
        };
    }

    private static AudioDeviceSwitchTarget NormalizeDeviceTarget(AudioDeviceSwitchTarget target) =>
        Enum.IsDefined(target) ? target : AudioDeviceSwitchTarget.SystemDefault;

    private static TrayWheelBehavior NormalizeTrayWheelAction(TrayWheelBehavior action, TrayWheelBehavior fallback) =>
        action is TrayWheelBehavior.AdjustVolume or TrayWheelBehavior.SwitchOutputDevice or TrayWheelBehavior.SwitchInputDevice or TrayWheelBehavior.Disabled ? action : fallback;
}
