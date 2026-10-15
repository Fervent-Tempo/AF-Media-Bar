// 只投影生效配置，保留用户保存的实验偏好；不执行平台操作。
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>新旧音频界面的运行时能力边界。</summary>
public static class ExperimentalAudioPolicy
{
    public static GlobalInteractionSettings Interaction(GlobalInteractionSettings settings, bool enabled) => enabled ? settings : settings with
    {
        PrimaryWheelAction = settings.PrimaryWheelAction == WheelAction.InputDevice ? WheelAction.Disabled : settings.PrimaryWheelAction,
        ChordWheelAction = settings.ChordWheelAction == WheelAction.InputDevice ? WheelAction.Disabled : settings.ChordWheelAction,
        TrayPrimaryWheelAction = settings.TrayPrimaryWheelAction == TrayWheelBehavior.SwitchInputDevice ? TrayWheelBehavior.Disabled : settings.TrayPrimaryWheelAction,
        TrayChordWheelAction = settings.TrayChordWheelAction == TrayWheelBehavior.SwitchInputDevice ? TrayWheelBehavior.Disabled : settings.TrayChordWheelAction,
        TrayClickAction = settings.TrayClickAction == TrayClickAction.OpenInputDeviceMenu ? TrayClickAction.None : settings.TrayClickAction,
        OutputDeviceClickTarget = AudioDeviceSwitchTarget.SystemDefault,
        OutputDeviceWheelTarget = AudioDeviceSwitchTarget.SystemDefault,
        InputDeviceClickTarget = AudioDeviceSwitchTarget.Disabled,
        InputDeviceWheelTarget = AudioDeviceSwitchTarget.Disabled
    };

    public static TaskbarExperienceSettings Experience(TaskbarExperienceSettings settings, bool enabled) => enabled ? settings : settings with
    {
        InputDeviceVisible = false,
        HoverControls = settings.HoverControls with { InputDeviceVisible = false },
        IdleComponents = settings.IdleComponents?.Where(component => component != TaskbarRestComponent.InputDevice).ToArray()
    };
}
