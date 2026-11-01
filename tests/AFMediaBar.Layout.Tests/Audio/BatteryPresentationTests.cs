// 防止多设备布局身份合并、模式切换丢位置及旧配置扩大追踪范围；不查询真实外设。
using System.Text.Json;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class BatteryPresentationTests
{
    [TestMethod]
    public void IndependentBatteryPositionsSurviveModeChangesAndTemporaryDeselection()
    {
        var settings = new AppSettings { BluetoothBatteryWidget = new() { Enabled = true, DeviceId = "a",
            PresentationMode = BatteryPresentationMode.Parallel, SelectedDevices = [new("a", "Mouse"), new("b", "Keyboard")] } };
        var state = BarLayoutState.Resident;
        var keys = ComponentLayoutPolicy.Resolve(settings, state).Select(e => e.Key).ToArray();
        var keyboard = new ComponentKey(TaskbarRestComponent.BluetoothBattery, "b");
        var target = Array.IndexOf(keys, new ComponentKey(TaskbarRestComponent.OutputDevice));
        Assert.IsTrue(ComponentLayoutPolicy.TryMove(settings, state, keyboard, target, keys, out var reordered));
        settings.TaskbarExperience = reordered;
        var expected = ComponentLayoutPolicy.Resolve(settings, state).Select(e => e.Key).ToArray();
        Assert.AreNotEqual(Array.IndexOf(expected, keyboard), Array.IndexOf(expected, new ComponentKey(TaskbarRestComponent.BluetoothBattery, "a")));
        settings.BluetoothBatteryWidget = settings.BluetoothBatteryWidget with { PresentationMode = BatteryPresentationMode.Cycle };
        Assert.AreEqual(1, ComponentLayoutPolicy.Resolve(settings, state).Count(e => e.Component == TaskbarRestComponent.BluetoothBattery));
        settings.BluetoothBatteryWidget = settings.BluetoothBatteryWidget with { PresentationMode = BatteryPresentationMode.Parallel, SelectedDevices = [new("a", "Mouse")] };
        Assert.IsFalse(ComponentLayoutPolicy.Resolve(settings, state).Any(e => e.Key == keyboard));
        settings.BluetoothBatteryWidget = settings.BluetoothBatteryWidget with { SelectedDevices = [new("a", "Mouse"), new("b", "Keyboard")] };
        var json = JsonSerializer.Serialize(settings.TaskbarExperience.ComponentLayouts);
        settings.TaskbarExperience = settings.TaskbarExperience with { ComponentLayouts = JsonSerializer.Deserialize<ComponentLayoutsSettings>(json)!.Normalize() };
        CollectionAssert.AreEqual(expected, ComponentLayoutPolicy.Resolve(settings, state).Select(e => e.Key).ToArray());
        var layout = TaskbarRestLayoutPolicy.ArrangeKeys(expected, _ => 50, _ => true, 0, 900, 8, 0);
        Assert.AreNotEqual(layout.Find(keyboard)!.Value.Left, layout.Find(new ComponentKey(TaskbarRestComponent.BluetoothBattery, "a"))!.Value.Left);
    }

    [TestMethod]
    public void OldSelectionAndExplicitEmptySelectionHaveDifferentTrackingScopes()
    {
        var legacy = JsonSerializer.Deserialize<BluetoothBatteryWidgetSettings>("{\"DeviceId\":\"main\",\"LastKnownName\":\"Headset\"}")!.Normalize();
        CollectionAssert.AreEqual(new[] { "main" }, BluetoothBatteryService.TrackingIds(legacy, true).ToArray());
        Assert.AreEqual(BatteryVisualStyle.Numeric, legacy.VisualStyle);
        var multi = legacy with { SelectedDevices = [new("mouse", "Mouse"), new("keyboard", "Keyboard")], Notifications = true };
        CollectionAssert.AreEquivalent(new[] { "mouse", "keyboard", "main" }, BluetoothBatteryService.TrackingIds(multi, true).ToArray());
        CollectionAssert.AreEqual(new[] { "main" }, BluetoothBatteryService.TrackingIds(multi, false).ToArray());
        Assert.AreEqual(0, BluetoothBatteryService.TrackingIds(legacy with { SelectedDevices = [] }, true).Count);
        Assert.AreEqual(2, (multi with { SelectedDevices = [new("a", "A"), new("A", "Alias"), new("b", "B")] }).Normalize().SelectedDevices!.Count);
    }

    [TestMethod]
    public void SystemClassificationDoesNotGuessMixedPeripheralsOrSpeakersAreHeadphones()
    {
        Assert.AreEqual(BatteryDeviceKind.Mouse, BluetoothBatteryReader.Classify(null, null, 0x03C2));
        Assert.AreEqual(BatteryDeviceKind.Keyboard, BluetoothBatteryReader.Classify(5, 0x10, null));
        Assert.AreEqual(BatteryDeviceKind.Headset, BluetoothBatteryReader.Classify(4, 6, null));
        Assert.AreEqual(BatteryDeviceKind.Controller, BluetoothBatteryReader.Classify(5, 2, null));
        Assert.AreEqual(BatteryDeviceKind.Unknown, BluetoothBatteryReader.Classify(5, 0x30, null));
        Assert.AreEqual(BatteryDeviceKind.Unknown, BluetoothBatteryReader.Classify(4, 5, null));
    }
}
