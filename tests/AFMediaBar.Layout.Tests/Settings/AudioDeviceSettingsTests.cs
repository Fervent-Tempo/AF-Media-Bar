// 验证真实设置序列化、黑名单语义和快照重置，恢复所有全局状态。
using System.IO;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>设备策略兼容及全局重置边界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AudioDeviceSettingsTests
{
    [TestMethod]
    public void ProcessIdentityRejectsPidReuseAndSameNameDifferentExecutable()
    {
        var expected = new AudioProcessIdentity(42, 1000, @"C:\Apps\Player\player.exe", null);
        Assert.IsTrue(AFMediaBar.Classes.Services.Audio.AudioApplicationIdentityPolicy.IsSameProcess(expected, expected with { ExecutablePath = @"c:\apps\player\PLAYER.exe" }));
        Assert.IsFalse(AFMediaBar.Classes.Services.Audio.AudioApplicationIdentityPolicy.IsSameProcess(expected, expected with { StartedAtUtcTicks = 2000 }));
        Assert.IsFalse(AFMediaBar.Classes.Services.Audio.AudioApplicationIdentityPolicy.IsSameProcess(expected, expected with { ExecutablePath = @"C:\Other\player.exe" }));
        Assert.AreNotEqual(AFMediaBar.Classes.Services.Audio.AudioApplicationIdentityPolicy.GetKey(expected),
            AFMediaBar.Classes.Services.Audio.AudioApplicationIdentityPolicy.GetKey(expected with { ExecutablePath = @"C:\Other\player.exe" }));
    }

    [TestMethod]
    public void BlacklistUsesDirectionAndStableIdRatherThanName()
    {
        var settings = new AudioDeviceSettings
        {
            OutputBlacklist = [new(" id-A ", "Same name"), new("ID-a", "Renamed"), new("", "Invalid")]
        }.Normalize();
        Assert.AreEqual(1, settings.OutputBlacklist.Count);
        Assert.IsFalse(settings.IsAllowed(AudioDeviceDirection.Output, "ID-A"));
        Assert.IsTrue(settings.IsAllowed(AudioDeviceDirection.Output, "id-B"));
        Assert.IsTrue(settings.IsAllowed(AudioDeviceDirection.Input, "id-A"));
        var clone = new AppSettings { AudioDevices = settings }.Clone();
        Assert.AreNotSame(settings.OutputBlacklist, clone.AudioDevices.OutputBlacklist);
        Assert.IsFalse(clone.AudioDevices.IsAllowed(AudioDeviceDirection.Output, "id-A"));
    }

    [TestMethod]
    public void OlderSettingsMissingNewFieldsRetainDefaultsAndNewSettingsRoundTrip()
    {
        var previous = SettingsManager.Current;
        var defaults = SettingsManager.UserDefaults;
        var directory = Path.Combine(Path.GetTempPath(), "afmb-audio-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"), """
                {"schemaVersion":2,"settings":{"interaction":{"trayPrimaryWheelAction":"SwitchOutputDevice","inputDeviceClickTarget":"invalid"}}}
                """);
            using (var persistence = new SettingsPersistenceService(directory))
            {
                persistence.Initialize();
                Assert.AreEqual(TrayWheelBehavior.SwitchOutputDevice, SettingsManager.Current.Interaction.TrayPrimaryWheelAction);
                Assert.AreEqual(AudioDeviceSwitchTarget.SystemDefault, SettingsManager.Current.Interaction.InputDeviceClickTarget);
                Assert.IsFalse(SettingsManager.Current.AudioDevices.SwitchInputCommunications);
                Assert.AreEqual(0, SettingsManager.Current.AudioDevices.InputBlacklist.Count);
                SettingsManager.Current.AudioDevices = new() { InputBlacklist = [new("mic-id", "Mic")], SwitchInputCommunications = true };
                SettingsManager.Current.Interaction = SettingsManager.Current.Interaction with { InputDeviceWheelTarget = AudioDeviceSwitchTarget.ForegroundApplication };
                persistence.Flush();
            }
            using (var persistence = new SettingsPersistenceService(directory))
            {
                persistence.Initialize();
                Assert.IsFalse(SettingsManager.Current.AudioDevices.IsAllowed(AudioDeviceDirection.Input, "mic-id"));
                Assert.IsTrue(SettingsManager.Current.AudioDevices.SwitchInputCommunications);
                Assert.AreEqual(AudioDeviceSwitchTarget.ForegroundApplication, SettingsManager.Current.Interaction.InputDeviceWheelTarget);
            }
        }
        finally
        {
            SettingsManager.SetUserDefaults(defaults);
            SettingsManager.Current = previous;
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void InteractionResetUsesSnapshotWithoutChangingButtonVisibility()
    {
        var previous = SettingsManager.Current;
        var defaults = SettingsManager.UserDefaults;
        try
        {
            var saved = new AppSettings
            {
                AudioDevices = new() { OutputBlacklist = [new("speaker", "Speaker")], SwitchOutputCommunications = true },
                Interaction = GlobalInteractionSettings.Default with { OutputDeviceClickTarget = AudioDeviceSwitchTarget.ForegroundApplication }
            };
            SettingsManager.SetUserDefaults(saved);
            SettingsManager.Current = new AppSettings
            {
                TaskbarExperience = TaskbarExperienceSettings.Default with { InputDeviceVisible = true }
            };
            SettingsManager.ResetInteraction();
            Assert.IsFalse(SettingsManager.Current.AudioDevices.SwitchOutputCommunications);
            Assert.IsTrue(SettingsManager.Current.AudioDevices.IsAllowed(AudioDeviceDirection.Output, "speaker"));
            SettingsManager.ResetDeviceManagement();
            Assert.IsTrue(SettingsManager.Current.AudioDevices.SwitchOutputCommunications);
            Assert.IsFalse(SettingsManager.Current.AudioDevices.IsAllowed(AudioDeviceDirection.Output, "speaker"));
            Assert.AreEqual(AudioDeviceSwitchTarget.ForegroundApplication, SettingsManager.Current.Interaction.OutputDeviceClickTarget);
            Assert.IsTrue(SettingsManager.Current.TaskbarExperience.InputDeviceVisible);
            SettingsManager.ResetContentLayout();
            Assert.IsFalse(SettingsManager.Current.TaskbarExperience.InputDeviceVisible);
            Assert.IsFalse(SettingsManager.Current.AudioDevices.IsAllowed(AudioDeviceDirection.Output, "speaker"));
        }
        finally { SettingsManager.SetUserDefaults(defaults); SettingsManager.Current = previous; }
    }
}
