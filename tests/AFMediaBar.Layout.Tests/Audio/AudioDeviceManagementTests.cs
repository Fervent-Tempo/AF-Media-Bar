// 用目录替身验证真实设置编辑器的分组和断开记录恢复，不访问系统设备。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>设备可用状态与黑名单管理的分组回归。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AudioDeviceManagementTests
{
    [TestMethod]
    public void LowBatteryAlertsEstablishBaselineAndRearmOnlyAfterHysteresis()
    {
        var policy = new BluetoothBatteryAlertPolicy();
        var id = Guid.NewGuid();
        BluetoothBatterySnapshot Reading(int percent) => new(id, percent, BluetoothBatterySource.WindowsProperty, true, true, DateTimeOffset.UtcNow);
        Assert.IsFalse(policy.Evaluate(id, Reading(15), true, 20), "启动时已经低电量只建立基线。");
        Assert.IsFalse(policy.Evaluate(id, Reading(25), true, 20));
        Assert.IsTrue(policy.Evaluate(id, Reading(20), true, 20));
        Assert.IsFalse(policy.Evaluate(id, Reading(19), true, 20));
        Assert.IsFalse(policy.Evaluate(id, Reading(22), true, 20));
        Assert.IsFalse(policy.Evaluate(id, Reading(20), true, 20), "回升不足 5% 不重新提醒。");
        Assert.IsFalse(policy.Evaluate(id, Reading(25), true, 20));
        Assert.IsTrue(policy.Evaluate(id, Reading(20), true, 20));
    }

    [TestMethod]
    public void SwitchingReconnectingAndFailedReadingsDoNotRepeatLowBatteryCycle()
    {
        var policy = new BluetoothBatteryAlertPolicy();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        BluetoothBatterySnapshot Reading(Guid id, int percent) => new(id, percent, BluetoothBatterySource.BleGatt, true, true, DateTimeOffset.UtcNow);
        Assert.IsFalse(policy.Evaluate(first, Reading(first, 30), true, 20));
        Assert.IsTrue(policy.Evaluate(first, Reading(first, 20), true, 20));
        Assert.IsFalse(policy.Evaluate(second, Reading(second, 60), true, 20));
        Assert.IsFalse(policy.Evaluate(first, Reading(first, 22), true, 20));
        Assert.IsFalse(policy.Evaluate(first, Reading(first, 20), true, 20));
        Assert.IsFalse(policy.Evaluate(first, Reading(first, 18) with { IsConnected = false, ReadSucceeded = false }, true, 20));
        Assert.IsFalse(policy.Evaluate(first, Reading(first, 18), true, 20));
        Assert.IsFalse(policy.Evaluate(first, Reading(first, 10) with { ReadSucceeded = false }, true, 20));
        Assert.IsFalse(policy.Evaluate(first, Reading(first, 10), true, 20));
        Assert.IsFalse(policy.Evaluate(first, Reading(first, 30), false, 20));
        Assert.IsFalse(policy.Evaluate(first, Reading(first, 15), true, 20));
    }

    [TestMethod]
    public void GroupsEndpointsByAvailabilityAndRecoversDisconnectedBlacklistRecords()
    {
        StaTest.Run(async _ =>
        {
            var previous = SettingsManager.Current;
            try
            {
                SettingsManager.Current = new AppSettings
                {
                    AudioDevices = new() { OutputBlacklist = [new("active", "Same"), new("gone", "Old headset")] }
                };
                var catalog = new Catalog
                {
                    Output = [new("active", "active", "Same", true), new("active", "ACTIVE", "Duplicate", false),
                        new("disabled", "disabled", "Same", false) { IsActive = false, Availability = AudioDeviceAvailability.Disabled },
                        new("unplugged", "unplugged", "Same", false) { IsActive = false, Availability = AudioDeviceAvailability.Unplugged }],
                    Input = [new("mic", "mic", "Same", false) { Direction = AudioDeviceDirection.Input }]
                };
                using var context = new SettingsPageContext();
                using var editor = new AudioDeviceSettingsViewModel(catalog, new LegacySettingsConfiguration(context));
                await editor.RefreshAsync();
                Assert.AreEqual(1, editor.OutputDevices.Count);
                Assert.IsFalse(editor.OutputDevices[0].IsAllowed, "活动的隐藏设备仍必须可管理。");
                Assert.AreEqual(3, editor.OtherOutputDevices.Count);
                Assert.AreEqual(AudioDeviceAvailability.Disabled, editor.OtherOutputDevices.Single(row => row.EndpointId == "disabled").Availability);
                Assert.AreEqual(AudioDeviceAvailability.Unplugged, editor.OtherOutputDevices.Single(row => row.EndpointId == "unplugged").Availability);
                Assert.IsTrue(editor.InputDevices[0].IsAllowed, "同名输入不能继承输出黑名单。");
                editor.OtherOutputDevices.Single(row => row.EndpointId == "gone").IsAllowed = true;
                Assert.IsTrue(SettingsManager.Current.AudioDevices.IsAllowed(AudioDeviceDirection.Output, "gone"));
                catalog.Output = [new("gone", "gone", "Reconnected", true)];
                await editor.RefreshAsync();
                Assert.AreEqual("gone", editor.OutputDevices.Single().EndpointId);
                Assert.IsTrue(editor.OutputDevices.Single().IsAllowed);
                Assert.AreEqual("active", editor.OtherOutputDevices.Single().EndpointId, "断开的隐藏活动设备保留管理记录。");
                editor.Dispose();
                await editor.RefreshAsync();
                Assert.AreEqual("gone", editor.OutputDevices.Single().EndpointId);
            }
            finally { SettingsManager.Current = previous; }
        });
    }

    private sealed class Catalog : IAudioDeviceCatalog
    {
        public IReadOnlyList<AudioDeviceOption> Output { get; set; } = [];
        public IReadOnlyList<AudioDeviceOption> Input { get; set; } = [];
        public event EventHandler? Changed { add { } remove { } }
        public Task<IReadOnlyList<AudioDeviceOption>> GetDevicesAsync(AudioDeviceDirection direction, bool includeHidden = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(direction == AudioDeviceDirection.Output ? Output : Input);
    }
}
