// 验证旧设置不意外启用新组件，且显隐、排序和无媒体保留保持现有语义。
using System.Text.Json;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.ViewModels.Pages;
using AFMediaBar.Components;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>宠物组件的配置兼容与布局边界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class PetComponentSettingsTests
{
    [TestMethod]
    public void ColorDraftDoesNotOverwriteStoredColorAndInactiveEditorCannotWrite()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current;
            using var context = new SettingsPageContext();
            using var localization = new LocalizationService();
            try
            {
                SettingsManager.Current = JsonSerializer.Deserialize<AppSettings>("{}")!.Normalize();
                Assert.IsTrue(SettingsManager.Current.PetComponent.IdleAnimationsEnabled);
                if (!SystemParameters.HighContrast)
                    Assert.AreEqual("#1C1C1C", PetVisualDefinition.Colors(SettingsManager.Current.PetComponent).Eyes);
                using var editor = new ComponentsSettingsViewModel(new LegacySettingsConfiguration(context), localization);
                editor.PetFillColorHex = "#12";
                Assert.AreEqual(PetComponentSettings.DefaultFillColorHex, SettingsManager.Current.PetComponent.FillColorHex);
                editor.PetFillColorHex = "ffcc00";
                editor.PetIdleAnimationsEnabled = false;
                editor.PetSizeDip = 96;
                editor.PetAllowOverflow = true;
                editor.PetAnimation = "radar";
                editor.PetOutfit = "badge";
                editor.PetOutfitColorHex = "#42CBA6";
                editor.PetOutfitColorHex = "#12";
                Assert.AreEqual("#42CBA6", SettingsManager.Current.PetComponent.OutfitColorHex);
                editor.PetVoiceEnabled = true;
                editor.PetVoiceVolume = 35;
                editor.PetNotificationsEnabled = true;
                editor.PetMusicSensitivity = 150;
                editor.PetFollowPlayback = false;
                Assert.AreEqual("#FFCC00", SettingsManager.Current.PetComponent.FillColorHex);
                var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(SettingsManager.Current.Clone()))!.Normalize();
                Assert.AreEqual(SettingsManager.Current.PetComponent, copy.PetComponent);
                context.Deactivate();
                editor.PetFillColorHex = "#000000";
                editor.PetIdleAnimationsEnabled = true;
                editor.PetSizeDip = 32;
                editor.PetAllowOverflow = false;
                editor.PetAnimation = "happy";
                Assert.AreEqual(copy.PetComponent, SettingsManager.Current.PetComponent);
                Assert.AreEqual(PetComponentSettings.DefaultFillColorHex, (new PetComponentSettings { FillColorHex = "broken" }).Normalize().FillColorHex);
            }
            finally { SettingsManager.Current = previous; }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void InvalidSizeAndAnimationCannotEscapeTheSettingsContract()
    {
        Assert.IsNull(new PetComponentSettings().Normalize().OutfitColorHex);
        Assert.IsNull((new PetComponentSettings { OutfitColorHex = "broken" }).Normalize().OutfitColorHex);
        Assert.AreEqual(48d, new PetComponentSettings().SizeDip);
        Assert.AreEqual(48d, (new PetComponentSettings { SizeDip = double.NaN }).Normalize().SizeDip);
        Assert.AreEqual(20d, (new PetComponentSettings { SizeDip = -1 }).Normalize().SizeDip);
        Assert.AreEqual(160d, (new PetComponentSettings { SizeDip = 1000 }).Normalize().SizeDip);
        Assert.AreEqual("auto", (new PetComponentSettings { Animation = null! }).Normalize().Animation);
        Assert.AreEqual("auto", (new PetComponentSettings { Animation = "unknown" }).Normalize().Animation);
        Assert.AreEqual("writing", (new PetComponentSettings { Animation = "writing" }).Normalize().Animation);
        Assert.IsFalse(new PetComponentSettings().AllowOverflow);
        Assert.AreEqual("original", (new PetComponentSettings { Outfit = "unknown" }).Normalize().Outfit);
        Assert.AreEqual(20d, (new PetComponentSettings { VoiceVolume = double.NaN }).Normalize().VoiceVolume);
        Assert.AreEqual(100d, (new PetComponentSettings { VoiceVolume = 500 }).Normalize().VoiceVolume);
        Assert.AreEqual(25d, (new PetComponentSettings { MusicSensitivity = -1 }).Normalize().MusicSensitivity);
        Assert.IsFalse(new PetComponentSettings().VoiceEnabled);
        Assert.IsFalse(new PetComponentSettings().NotificationsEnabled);
    }

    [TestMethod]
    public void OverflowExtendsTowardScreenInteriorAndClampsToTheCorrectMonitor()
    {
        Assert.IsTrue(PetOverflowHost.ContainsVisualPoint(new Point(32, 9)), "旋转后的球体顶端应能接收局部点击。");
        Assert.IsTrue(PetOverflowHost.ContainsVisualPoint(new Point(23.3, 6.1)), "歪身后移出的耳尖应能接收局部点击。");
        Assert.IsFalse(PetOverflowHost.ContainsVisualPoint(new Point(0, 0)), "空白角落不能扩大命中区域。");
        Assert.AreEqual(new Rect(90, 920, 120, 120), PetPlacementPolicy.Arrange(new Rect(100, 1040, 100, 40),
            new Rect(0, 0, 1920, 1080), 120, false, false, new Rect(0, 0, 1920, 1040)));
        Assert.AreEqual(new Rect(-1880, 290, 120, 120), PetPlacementPolicy.Arrange(new Rect(-1920, 300, 40, 100),
            new Rect(-1920, 0, 1920, 1080), 120, true, true, new Rect(-1880, 0, 1880, 1080)));
        Assert.AreEqual(new Rect(90, 960, 120, 120), PetPlacementPolicy.Arrange(new Rect(100, 1040, 100, 40),
            new Rect(0, 0, 1920, 1080), 120, false, false));
        Assert.AreEqual(new Rect(90, 0, 120, 120), PetPlacementPolicy.Arrange(new Rect(100, 0, 100, 40),
            new Rect(0, 0, 1920, 1080), 120, false, false));
        Assert.AreEqual(new Rect(-1920, 290, 120, 120), PetPlacementPolicy.Arrange(new Rect(-1920, 300, 40, 100),
            new Rect(-1920, 0, 1920, 1080), 120, true, true));
        Assert.AreEqual(new Rect(1800, 960, 120, 120), PetPlacementPolicy.Arrange(new Rect(1900, 1060, 20, 20),
            new Rect(0, 0, 1920, 1080), 120, false, false));
    }

    [TestMethod]
    public void OldConfigurationDoesNotEnablePetOrChangeExistingOrder()
    {
        var restored = JsonSerializer.Deserialize<TaskbarExperienceSettings>(
            """{"RestComponentOrder":[5,2,7],"IdleComponents":null}""").Normalize();
        Assert.IsFalse(restored.PetVisible);
        var order = TaskbarRestLayoutPolicy.ResolveOrder(restored.RestComponentOrder).ToArray();
        CollectionAssert.AreEqual(new[] { TaskbarRestComponent.Volume, TaskbarRestComponent.Spectrum, TaskbarRestComponent.BluetoothBattery },
            order.Skip(2).Take(3).ToArray());
        Assert.AreEqual(TaskbarRestComponent.Pet, order[^1]);
        var visibility = new TaskbarRestLayoutPolicy.Visibility(true, null, true, true, false, false);
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Pet, visibility));
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Pet, visibility with { MediaConnected = false }));
        Assert.IsTrue(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Pet, visibility with { PetEnabled = true }));
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Pet,
            visibility with { MediaConnected = false, PetEnabled = true, IdleComponents = [] }));
        Assert.IsTrue(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Pet,
            visibility with { MediaConnected = false, IdleComponents = [TaskbarRestComponent.Pet] }));
    }

    [TestMethod]
    public void PetOnlyIdleLayoutUsesItsRealWidthAndCanBeRemovedEntirely()
    {
        var visibility = new TaskbarRestLayoutPolicy.Visibility(false, [TaskbarRestComponent.Pet], true, true, false, false);
        var order = TaskbarRestLayoutPolicy.ResolveOrder([TaskbarRestComponent.Pet, TaskbarRestComponent.Volume]);
        var layout = TaskbarRestLayoutPolicy.Arrange(order, _ => 36,
            component => TaskbarRestLayoutPolicy.IsVisible(component, visibility), 2, 40, 12, 2);
        Assert.AreEqual(40, layout.ContentWidth);
        Assert.AreEqual(TaskbarRestComponent.Pet, layout.Placements.Single().Component);
        Assert.AreEqual(36, layout.Placements.Single().Width);
        Assert.IsTrue(TaskbarRestLayoutPolicy.Arrange(order, _ => 36,
            component => TaskbarRestLayoutPolicy.IsVisible(component, visibility with { IdleComponents = [] }),
            2, 40, 12, 2).IsEmpty);
    }
}
