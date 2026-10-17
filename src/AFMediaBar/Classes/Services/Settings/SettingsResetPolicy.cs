// 为两份配置计算页面重置值；隐藏的实验选项保持原值，不发布事件或修改设置。
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>与页面呈现范围一致的配置重置策略。</summary>
internal static class SettingsResetPolicy
{
    public static GlobalInteractionSettings Interaction(GlobalInteractionSettings current, GlobalInteractionSettings defaults, bool audioEnabled) =>
        audioEnabled ? defaults : defaults with
        {
            OutputDeviceClickTarget = current.OutputDeviceClickTarget,
            OutputDeviceWheelTarget = current.OutputDeviceWheelTarget,
            InputDeviceClickTarget = current.InputDeviceClickTarget,
            InputDeviceWheelTarget = current.InputDeviceWheelTarget,
            PrimaryWheelAction = current.PrimaryWheelAction == WheelAction.InputDevice ? current.PrimaryWheelAction : defaults.PrimaryWheelAction,
            ChordWheelAction = current.ChordWheelAction == WheelAction.InputDevice ? current.ChordWheelAction : defaults.ChordWheelAction,
            TrayPrimaryWheelAction = current.TrayPrimaryWheelAction == TrayWheelBehavior.SwitchInputDevice ? current.TrayPrimaryWheelAction : defaults.TrayPrimaryWheelAction,
            TrayChordWheelAction = current.TrayChordWheelAction == TrayWheelBehavior.SwitchInputDevice ? current.TrayChordWheelAction : defaults.TrayChordWheelAction,
            TrayClickAction = current.TrayClickAction == TrayClickAction.OpenInputDeviceMenu ? current.TrayClickAction : defaults.TrayClickAction
        };

    public static TaskbarExperienceSettings Content(TaskbarExperienceSettings current, TaskbarExperienceSettings defaults, bool audioEnabled)
    {
        var next = defaults with { MediaFontSizePercent = current.MediaFontSizePercent };
        if (audioEnabled) return next;
        var order = defaults.RestComponentOrder;
        if (current.RestComponentOrder?.Contains(TaskbarRestComponent.InputDevice) == true)
        {
            var preserved = TaskbarRestLayoutPolicy.KeepInputDeviceOrder(current.RestComponentOrder, order);
            var defaultOrder = TaskbarRestLayoutPolicy.ResolveOrder(order).Where(component => !TaskbarRestLayoutPolicy.FixedOrder.Contains(component));
            if (!preserved.SequenceEqual(defaultOrder)) order = preserved;
        }
        var defaultIdle = TaskbarRestLayoutPolicy.ResolveIdleComponents(defaults.IdleComponents);
        var idle = defaultIdle.Where(component => component != TaskbarRestComponent.InputDevice)
            .Concat(current.IdleComponents?.Contains(TaskbarRestComponent.InputDevice) == true ? new[] { TaskbarRestComponent.InputDevice } : []).ToArray();
        return next with
        {
            InputDeviceVisible = current.InputDeviceVisible,
            HoverControls = next.HoverControls with { InputDeviceVisible = current.HoverControls.InputDeviceVisible },
            RestComponentOrder = order,
            IdleComponents = idle.SequenceEqual(defaultIdle) ? defaults.IdleComponents : idle
        };
    }
}
