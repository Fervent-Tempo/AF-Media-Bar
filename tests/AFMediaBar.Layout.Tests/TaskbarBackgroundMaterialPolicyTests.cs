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
    public void TransparentMaterialDoesNotDrawSurface()
    {
        var result = TaskbarBackgroundMaterialPolicy.Resolve(TaskbarBackgroundMaterial.Transparent, false, true, 72);

        Assert.AreEqual(Colors.Transparent, result.Tint);
        Assert.AreEqual(Colors.Transparent, result.Stroke);
        Assert.AreEqual(0, result.ArtworkOpacity);
    }

    [TestMethod]
    public void FrostedMaterialUsesDarkTintBehindLightText()
    {
        var result = TaskbarBackgroundMaterialPolicy.Resolve(TaskbarBackgroundMaterial.Frosted, false, true, 72);

        Assert.IsTrue(result.Tint.R < 64);
        Assert.IsTrue(result.Tint.A >= 0xB0);
        Assert.IsTrue(result.ArtworkOpacity > 0);
    }

    [TestMethod]
    public void FrostedMaterialUsesLightTintBehindDarkText()
    {
        var result = TaskbarBackgroundMaterialPolicy.Resolve(TaskbarBackgroundMaterial.Frosted, false, false, 72);

        Assert.IsTrue(result.Tint.R is >= 0x70 and <= 0xA0);
        Assert.IsTrue(result.Tint.A is >= 0xA0 and <= 0xC8);
        Assert.IsTrue(result.ArtworkOpacity > 0);
    }

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
    public void FrostStrengthControlsTintAlphaAndClampsToSupportedRange()
    {
        var minimum = TaskbarBackgroundMaterialPolicy.Resolve(TaskbarBackgroundMaterial.Frosted, false, false, 0);
        var maximum = TaskbarBackgroundMaterialPolicy.Resolve(TaskbarBackgroundMaterial.Frosted, false, false, 100);

        Assert.AreEqual(
            (byte)Math.Round(byte.MaxValue * AppearanceSettings.MinimumTaskbarBackgroundOpacityPercent / 100d),
            minimum.Tint.A);
        Assert.AreEqual(
            (byte)Math.Round(byte.MaxValue * AppearanceSettings.MaximumTaskbarBackgroundOpacityPercent / 100d),
            maximum.Tint.A);
        Assert.IsTrue(maximum.Tint.A > minimum.Tint.A);
    }

    [TestMethod]
    public void FrostedStylesProvideDistinctPalettesWithoutReducingContrastMode()
    {
        var neutral = TaskbarBackgroundMaterialPolicy.Resolve(
            TaskbarBackgroundMaterial.Frosted, false, true, 72, TaskbarFrostedStyle.Neutral);
        var cool = TaskbarBackgroundMaterialPolicy.Resolve(
            TaskbarBackgroundMaterial.Frosted, false, true, 72, TaskbarFrostedStyle.Cool);
        var warm = TaskbarBackgroundMaterialPolicy.Resolve(
            TaskbarBackgroundMaterial.Frosted, false, true, 72, TaskbarFrostedStyle.Warm);
        var artwork = TaskbarBackgroundMaterialPolicy.Resolve(
            TaskbarBackgroundMaterial.Frosted, false, true, 72, TaskbarFrostedStyle.Artwork);

        Assert.AreNotEqual(neutral.Tint, cool.Tint);
        Assert.AreNotEqual(neutral.Tint, warm.Tint);
        Assert.IsTrue(artwork.ArtworkOpacity > neutral.ArtworkOpacity);
        Assert.AreEqual(neutral.Tint.A, cool.Tint.A);
        Assert.AreEqual(neutral.Tint.A, warm.Tint.A);
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
    public void TaskbarOpacitySupportsOnePercentSteps()
    {
        Assert.AreEqual(1, AppearanceSettings.TaskbarBackgroundOpacityStep);
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
                TaskbarBackgroundOpacityPercent = AppearanceSettings.DefaultTaskbarBackgroundOpacityPercent + 1
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
