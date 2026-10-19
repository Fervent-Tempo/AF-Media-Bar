// 使用独立配置目录验证音量记忆的兼容、离线保留和重置，不创建音频会话或改变实际音量。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace AFMediaBar.Layout.Tests;

/// <summary>应用音量记忆的持久化与默认快照隔离。</summary>
[TestClass, DoNotParallelize]
public sealed class ApplicationVolumeMemorySettingsTests
{
    [TestMethod]
    public void OldSettingsDoNotEnrollApplicationsAndNormalizationRetainsOfflineAndZeroVolumes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "afmb-volume-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new SettingsPersistenceService(directory);
            foreach (var field in new[] { "", ",\"applicationVolumeMemory\":null", ",\"applicationVolumeMemory\":{\"entries\":null}" })
            {
                File.WriteAllText(Path.Combine(directory, "user-defaults.json"),
                    "{\"schemaVersion\":2,\"settings\":{\"lyricsEnabled\":false" + field + "}}");
                var settings = store.LoadUserDefaults();
                Assert.IsNotNull(settings);
                Assert.IsFalse(settings.LyricsEnabled);
                Assert.AreEqual(0, settings.ApplicationVolumeMemory.Entries.Count);
            }
            var path = Path.Combine(directory, "offline-player.exe");
            var normalized = new ApplicationVolumeMemorySettings
            {
                Entries = [new(path, 0), new(path.ToUpperInvariant(), 100), new("relative.exe", 40), new(Path.Combine(directory, "other.exe"), 900)]
            }.Normalize();
            Assert.AreEqual(2, normalized.Entries.Count);
            Assert.AreEqual(0, normalized.Entries[0].VolumePercent);
            Assert.AreEqual(100, normalized.Entries[1].VolumePercent);
            Assert.AreEqual(path, normalized.Entries[0].ExecutablePath, "应用离线不能清除已保存的配置。");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void VolumeMemoryRoundTripsAndResetsWithoutMutatingUserDefaultsOrOtherSettings()
    {
        var original = SettingsManager.Current.Clone();
        var defaults = SettingsManager.UserDefaults?.Clone();
        var directory = Path.Combine(Path.GetTempPath(), "afmb-volume-defaults-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new ApplicationVolumeMemoryEntry(Path.Combine(directory, "Nons.exe"), 40);
            SettingsManager.Current = new AppSettings
            {
                ApplicationVolumeMemory = new() { Entries = [entry] }, LyricsEnabled = false
            };
            using var store = new SettingsPersistenceService(directory);
            Assert.IsNull(store.SaveCurrentAsUserDefaults());
            var saved = store.LoadUserDefaults();
            Assert.IsNotNull(saved);
            Assert.AreEqual(entry, saved.ApplicationVolumeMemory.Entries.Single());
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "user-defaults.json")));
            Assert.IsFalse(json.RootElement.GetProperty("settings").GetProperty("applicationVolumeMemory")
                .GetProperty("entries")[0].TryGetProperty("displayName", out _));
            SettingsManager.Current.ApplicationVolumeMemory = new() { Entries = [entry with { VolumePercent = 60 }] };
            SettingsManager.ResetExtraFeatures();
            Assert.AreEqual(40, SettingsManager.Current.ApplicationVolumeMemory.Entries.Single().VolumePercent);
            Assert.AreNotSame(SettingsManager.Current.ApplicationVolumeMemory.Entries, SettingsManager.UserDefaults!.ApplicationVolumeMemory.Entries);
            Assert.IsFalse(SettingsManager.Current.LyricsEnabled);
            SettingsManager.Current.ApplicationVolumeMemory = ApplicationVolumeMemorySettings.Default;
            Assert.AreEqual(40, SettingsManager.UserDefaults.ApplicationVolumeMemory.Entries.Single().VolumePercent);
            SettingsManager.SetUserDefaults(null);
            SettingsManager.ResetExtraFeatures();
            Assert.AreEqual(0, SettingsManager.Current.ApplicationVolumeMemory.Entries.Count);
        }
        finally
        {
            SettingsManager.SetUserDefaults(defaults);
            SettingsManager.Current = original;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
