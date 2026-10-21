using System.Windows.Media;
using System.Text.Json;
using System.Text.Json.Nodes;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class TaskbarBackgroundMaterialPolicyTests
{
    [TestMethod]
    public void HighContrastMaterialIsOpaqueAndDisablesTexture()
    {
        var result = TaskbarBackgroundMaterialPolicy.Resolve(TaskbarBackgroundMaterial.Frosted, true, true, 72);

        Assert.AreEqual(byte.MaxValue, result.Tint.A);
        Assert.AreEqual(0, result.ArtworkOpacity);
    }

    [TestMethod]
    public void InvalidSavedMaterialFallsBackToTransparent()
    {
        var settings = (AppearanceSettings.Default with
        {
            TaskbarBackgroundMaterial = (TaskbarBackgroundMaterial)999
        }).Normalize();

        Assert.AreEqual(TaskbarBackgroundMaterial.Transparent, settings.TaskbarBackgroundMaterial);
    }

    [TestMethod]
    public void InvalidFrostedStyleFallsBackToNeutral()
    {
        var settings = (AppearanceSettings.Default with
        {
            TaskbarFrostedStyle = (TaskbarFrostedStyle)999
        }).Normalize();

        Assert.AreEqual(TaskbarFrostedStyle.Neutral, settings.TaskbarFrostedStyle);
    }

    [TestMethod]
    public void FrostedMaterialRoundTripsAndMissingFieldKeepsLegacyDefault()
    {
        var frosted = AppearanceSettings.Default with
        {
            TaskbarBackgroundMaterial = TaskbarBackgroundMaterial.Frosted
        };
        var json = JsonSerializer.Serialize(frosted);

        Assert.AreEqual(
            TaskbarBackgroundMaterial.Frosted,
            JsonSerializer.Deserialize<AppearanceSettings>(json).TaskbarBackgroundMaterial);

        var legacy = JsonNode.Parse(json)!.AsObject();
        legacy.Remove(nameof(AppearanceSettings.TaskbarBackgroundMaterial));
        Assert.AreEqual(
            TaskbarBackgroundMaterial.Transparent,
            JsonSerializer.Deserialize<AppearanceSettings>(legacy.ToJsonString()).TaskbarBackgroundMaterial);
    }

    [TestMethod]
    public void TaskbarOnlyAppearanceChangeDoesNotPublishGlobalAppearanceRefresh()
    {
        var original = SettingsManager.Current.Clone();
        var globalRefreshes = 0;
        var taskbarRefreshes = 0;
        EventHandler<AppearanceSettingsChangedEventArgs> globalHandler = (_, _) => globalRefreshes++;
        EventHandler<AppearanceSettingsChangedEventArgs> taskbarHandler = (_, _) => taskbarRefreshes++;

        try
        {
            SettingsManager.Current.Appearance = AppearanceSettings.Default;
            SettingsManager.AppearanceSettingsChanged += globalHandler;
            SettingsManager.TaskbarAppearanceSettingsChanged += taskbarHandler;

            SettingsManager.SetAppearanceSettings(AppearanceSettings.Default with
            {
                TaskbarBackgroundOpacityPercent = AppearanceSettings.DefaultTaskbarBackgroundOpacityPercent + 1,
                ShowTaskbarBackgroundWhenIdle = false,
                ShowTaskbarBackgroundTint = false,
                ShowTaskbarBackgroundBorder = false,
                ShowTaskbarBackgroundTexture = false
            });

            Assert.AreEqual(0, globalRefreshes);
            Assert.AreEqual(1, taskbarRefreshes);
        }
        finally
        {
            SettingsManager.AppearanceSettingsChanged -= globalHandler;
            SettingsManager.TaskbarAppearanceSettingsChanged -= taskbarHandler;
            SettingsManager.Replace(original);
        }
    }
}
