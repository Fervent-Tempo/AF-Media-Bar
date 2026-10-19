// 检查运行时能力投影不会覆盖用户保存的输入、黑名单或高级策略偏好。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class ExperimentalAudioPolicyTests
{
    [TestMethod]
    public void DisabledExperimentBlocksInputAndUsesLegacyOutputTargetWithoutRewritingPreferences()
    {
        var stored = GlobalInteractionSettings.Default with
        {
            PrimaryWheelAction = WheelAction.InputDevice,
            ChordWheelAction = WheelAction.OutputDevice,
            TrayClickAction = TrayClickAction.OpenInputDeviceMenu,
            TrayPrimaryWheelAction = TrayWheelBehavior.SwitchInputDevice,
            OutputDeviceClickTarget = AudioDeviceSwitchTarget.ForegroundApplication,
            InputDeviceClickTarget = AudioDeviceSwitchTarget.ForegroundApplication
        };
        var runtime = ExperimentalAudioPolicy.Interaction(stored, false);
        Assert.AreEqual(WheelAction.Disabled, runtime.PrimaryWheelAction);
        Assert.AreEqual(WheelAction.OutputDevice, runtime.ChordWheelAction);
        Assert.AreEqual(TrayClickAction.None, runtime.TrayClickAction);
        Assert.AreEqual(TrayWheelBehavior.Disabled, runtime.TrayPrimaryWheelAction);
        Assert.AreEqual(AudioDeviceSwitchTarget.SystemDefault, runtime.OutputDeviceClickTarget);
        Assert.AreEqual(AudioDeviceSwitchTarget.Disabled, runtime.InputDeviceClickTarget);
        Assert.AreEqual(AudioDeviceSwitchTarget.ForegroundApplication, stored.OutputDeviceClickTarget);
        Assert.AreEqual(stored, ExperimentalAudioPolicy.Interaction(stored, true));
    }

    [TestMethod]
    public void DisabledExperimentHidesInputInRestHoverAndIdleWhilePreservingStoredSelection()
    {
        var stored = TaskbarExperienceSettings.Default with
        {
            InputDeviceVisible = true,
            IdleComponents = [TaskbarRestComponent.InputDevice, TaskbarRestComponent.Artwork],
            HoverControls = TaskbarExperienceSettings.Default.HoverControls with { InputDeviceVisible = true }
        };
        var runtime = ExperimentalAudioPolicy.Experience(stored, false);
        Assert.IsFalse(runtime.InputDeviceVisible);
        Assert.IsFalse(runtime.HoverControls.InputDeviceVisible);
        CollectionAssert.AreEqual(new[] { TaskbarRestComponent.Artwork }, runtime.IdleComponents!.ToArray());
        Assert.IsTrue(stored.InputDeviceVisible);
        Assert.IsTrue(stored.HoverControls.InputDeviceVisible);
        Assert.AreEqual(stored, ExperimentalAudioPolicy.Experience(stored, true));
    }
}
