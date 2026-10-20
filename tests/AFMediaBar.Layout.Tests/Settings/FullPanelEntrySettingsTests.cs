// 覆盖已发布总开关折合为入口可见性，以及正式入口设置的用户默认快照重置。
using System.Text.Json;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>旧开关、用户默认快照与正式入口设置的兼容回归。</summary>
[TestClass]
[DoNotParallelize]
public sealed class FullPanelEntrySettingsTests
{
    [DataTestMethod]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, true)]
    public void PublishedFlagsFoldIntoExternalEntryWithoutNewFields(bool oldEnabled, bool oldEntry, bool expected)
    {
        var document = JsonSerializer.SerializeToNode(new AppSettings())!;
        var experience = document[nameof(AppSettings.TaskbarExperience)]!;
        experience[nameof(TaskbarExperienceSettings.FullLayerEnabled)] = oldEnabled;
        experience[nameof(TaskbarExperienceSettings.FullPanelEntryVisible)] = oldEntry;
        experience.AsObject().Remove(nameof(TaskbarExperienceSettings.FullPanelEntryTrigger));
        var restored = document.Deserialize<AppSettings>()!.Normalize().Clone();
        Assert.AreEqual(expected, restored.TaskbarExperience.FullPanelEntryVisible);
        Assert.IsTrue(restored.TaskbarExperience.FullLayerEnabled, "旧字段不再关闭完整面板。");
        Assert.AreEqual(FullPanelHandleTrigger.Click, restored.TaskbarExperience.FullPanelEntryTrigger);
        var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(restored))!.Normalize();
        Assert.AreEqual(expected, roundTrip.TaskbarExperience.FullPanelEntryVisible);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ContentResetRestoresUserEntryVisibilityAndTrigger(bool legacyReset)
    {
        var previous = SettingsManager.Current;
        var previousDefaults = SettingsManager.UserDefaults;
        try
        {
            SettingsManager.SetUserDefaults(new AppSettings
            {
                TaskbarExperience = TaskbarExperienceSettings.Default with
                { FullPanelEntryVisible = false, FullPanelEntryTrigger = FullPanelHandleTrigger.Hover }
            });
            SettingsManager.Current = new AppSettings();
            if (legacyReset) SettingsManager.ResetDisplayModes();
            else SettingsManager.ResetContentLayout();
            Assert.IsFalse(SettingsManager.Current.TaskbarExperience.FullPanelEntryVisible);
            Assert.AreEqual(FullPanelHandleTrigger.Hover, SettingsManager.Current.TaskbarExperience.FullPanelEntryTrigger);
        }
        finally
        {
            SettingsManager.Current = previous;
            SettingsManager.SetUserDefaults(previousDefaults);
        }
    }
}
