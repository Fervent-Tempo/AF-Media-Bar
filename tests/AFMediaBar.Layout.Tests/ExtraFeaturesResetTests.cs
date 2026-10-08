// 验证快速启动按用户快照重置且不污染快照或相邻页面设置；结束时恢复全部静态设置。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>额外功能重置与用户默认快照的回归验证。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ExtraFeaturesResetTests
{
    [TestMethod]
    public void QuickLaunchUsesSnapshotWithoutChangingOtherScopesOrSnapshot()
    {
        var previous = SettingsManager.Current.Clone();
        var defaults = SettingsManager.UserDefaults?.Clone();
        try
        {
            var entry = new QuickLaunchEntry("saved", "Player", QuickLaunchTargetKind.AppUserModelId, "Test.Player", null);
            SettingsManager.SetUserDefaults(new AppSettings { QuickLaunch = new([entry]) });
            SettingsManager.Current = new AppSettings { QuickLaunch = QuickLaunchSettings.Default, LyricsEnabled = false };
            SettingsManager.ResetExtraFeatures();
            CollectionAssert.AreEqual(new[] { entry }, SettingsManager.Current.QuickLaunch.Entries!.ToArray());
            Assert.IsFalse(SettingsManager.Current.LyricsEnabled);
            Assert.AreNotSame(SettingsManager.UserDefaults!.QuickLaunch.Entries, SettingsManager.Current.QuickLaunch.Entries);
            SettingsManager.Current.QuickLaunch = QuickLaunchSettings.Default;
            Assert.AreEqual(entry, SettingsManager.UserDefaults.QuickLaunch.Entries!.Single());
            SettingsManager.SetUserDefaults(null);
            SettingsManager.Current.QuickLaunch = new([entry]);
            SettingsManager.ResetExtraFeatures();
            Assert.AreEqual(0, SettingsManager.Current.QuickLaunch.Entries!.Count);
        }
        finally { SettingsManager.SetUserDefaults(defaults); SettingsManager.Current = previous; }
    }
}
