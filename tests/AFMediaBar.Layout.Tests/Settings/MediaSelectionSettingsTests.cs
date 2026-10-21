// 使用独立目录验证设置读取和用户快照，结束时恢复全局设置；不读写用户实际配置。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>选择配置兼容性、重置与四语言文案回归。</summary>
[TestClass, DoNotParallelize]
public sealed class MediaSelectionSettingsTests
{
    [TestMethod]
    public void MissingNullAndInvalidModesKeepOtherOldSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "afmb-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new SettingsPersistenceService(directory);
            foreach (var field in new[]
            {
                "",
                ",\"mediaSelection\":null",
                ",\"mediaSelection\":{}",
                ",\"mediaSelection\":{\"mode\":999}",
                ",\"mediaSelection\":{\"mode\":\"unknown\",\"preferredSourceId\":\"  player  \"}"
            })
            {
                File.WriteAllText(Path.Combine(directory, "user-defaults.json"),
                    "{\"schemaVersion\":2,\"settings\":{\"lyricsEnabled\":false" + field + "}}");
                var settings = store.LoadUserDefaults();
                Assert.IsNotNull(settings);
                Assert.IsFalse(settings.LyricsEnabled, "新字段无效不能导致其他旧设置丢失。");
                Assert.AreEqual(MediaSelectionMode.Automatic, settings.MediaSelection.Mode);
                if (field.Contains("player", StringComparison.Ordinal))
                    Assert.AreEqual("player", settings.MediaSelection.PreferredSourceId);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void SelectionRoundTripsAndResetsFromUserDefaultsWithoutChangingOtherScopes()
    {
        var original = SettingsManager.Current.Clone();
        var defaults = SettingsManager.UserDefaults?.Clone();
        var directory = Path.Combine(Path.GetTempPath(), "afmb-selection-defaults-" + Guid.NewGuid().ToString("N"));
        try
        {
            SettingsManager.Current = new AppSettings
            {
                MediaSelection = new(MediaSelectionMode.PreferredApplication, "offline-player")
            };
            using var store = new SettingsPersistenceService(directory);
            Assert.IsNull(store.SaveCurrentAsUserDefaults());
            var saved = store.LoadUserDefaults();
            Assert.IsNotNull(saved);
            Assert.AreEqual(SettingsManager.Current.MediaSelection, saved.MediaSelection);
            SettingsManager.Current.MediaSelection = new(MediaSelectionMode.LatestSession);
            SettingsManager.Current.LyricsEnabled = false;
            SettingsManager.ResetExtraFeatures();
            Assert.AreEqual(saved.MediaSelection, SettingsManager.Current.MediaSelection);
            Assert.IsFalse(SettingsManager.Current.LyricsEnabled);
            SettingsManager.Current.MediaSelection = MediaSelectionSettings.Default;
            Assert.AreEqual("offline-player", SettingsManager.UserDefaults!.MediaSelection.PreferredSourceId);
            SettingsManager.SetUserDefaults(null);
            SettingsManager.ResetExtraFeatures();
            Assert.AreEqual(MediaSelectionSettings.Default, SettingsManager.Current.MediaSelection);
        }
        finally
        {
            SettingsManager.SetUserDefaults(defaults);
            SettingsManager.Current = original;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
