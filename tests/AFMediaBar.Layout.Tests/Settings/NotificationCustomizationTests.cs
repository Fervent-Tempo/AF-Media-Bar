// 验证旧配置、新通知样式和偏移的边界，不显示桌面窗口或修改用户设置。
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>通知配置读取、偏移与预览使用同一份设置契约。</summary>
[TestClass]
public sealed class NotificationCustomizationTests
{
    [TestMethod]
    public void OlderSettingsUseExistingVisualsAndExplicitChoicesSurvive()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacy = JsonSerializer.Deserialize<TrackChangeNotificationSettings>("""{"enabled":true,"durationMilliseconds":3000,"position":2}""", options).Normalize();
        Assert.AreEqual(15, legacy.TitleFontSize);
        Assert.AreEqual(12, legacy.ArtistFontSize);
        Assert.AreEqual(0d, legacy.HorizontalOffsetDip);
        Assert.IsTrue(JsonSerializer.Deserialize<QuickLaunchSettings>("{}", options).ShowTooltips);
        Assert.AreEqual(4, JsonSerializer.Deserialize<TaskbarExperienceSettings>("{}", options).HoverTextBlurRadius);
        var custom = legacy with { HorizontalOffsetDip = -120, VerticalOffsetDip = 45, TitleFontSize = 26, ArtistFontSize = 18, TitleColor = "#FF3300" };
        Assert.AreEqual(custom, JsonSerializer.Deserialize<TrackChangeNotificationSettings>(JsonSerializer.Serialize(custom, options), options).Normalize());
        Assert.IsFalse(JsonSerializer.Deserialize<QuickLaunchSettings>("""{"showTooltips":false}""", options).ShowTooltips);
        var previous = SpectrumComponentSettings.Default with { BandCount = 9 };
        Assert.AreEqual(9, previous.Normalize().BandCount);
        Assert.AreEqual(TaskbarInformationDensity.Minimal, (TaskbarExperienceSettings.Default with { Density = TaskbarInformationDensity.Minimal }).Normalize().Density);
    }

    [TestMethod]
    public void OffsetsRespectWorkAreaAndDpiAcrossNegativeMonitorCoordinates()
    {
        var work = new Rect(-1920, -120, 1920, 1080);
        var size = NotificationPlacementCalculator.ToPhysicalSize(new Size(300, 100), 144, 144);
        var original = NotificationPlacementCalculator.Calculate(work, size, TrackChangeNotificationPosition.TopCenter, 24);
        var moved = NotificationPlacementCalculator.Calculate(work, size, TrackChangeNotificationPosition.TopCenter, 24, 150, 75);
        Assert.AreEqual(original.X + 150, moved.X);
        Assert.AreEqual(original.Y + 75, moved.Y);
        var clipped = NotificationPlacementCalculator.Calculate(work, size, TrackChangeNotificationPosition.BottomRight, 24, 99999, 99999);
        Assert.AreEqual(work.Right - 24 - size.Width, clipped.X);
        Assert.AreEqual(work.Bottom - 24 - size.Height, clipped.Y);
        var invalid = NotificationPlacementCalculator.Calculate(work, size, TrackChangeNotificationPosition.TopCenter, 24, double.NaN, double.PositiveInfinity);
        Assert.AreEqual(original, invalid);
    }

    [TestMethod]
    public void SharedContentAndScreenPreviewUpdateWithoutShowingAWindow()
    {
        StaTest.Run(_ =>
        {
            var settings = TrackChangeNotificationSettings.Default with { Position = TrackChangeNotificationPosition.TopLeft, TitleFontSize = 26, ArtistFontSize = 18, TitleColor = "#FF0000" };
            var snapshot = MediaSnapshot.Disconnected with { Title = "Track", Artist = "Artist" };
            var content = new TrackNotificationContent { Settings = settings, Snapshot = snapshot };
            var title = (TextBlock)content.FindName("TitleText");
            Assert.AreEqual(26d, title.FontSize);
            Assert.AreEqual(Colors.Red, ((SolidColorBrush)title.Foreground).Color);
            Assert.AreEqual(18d, ((TextBlock)content.FindName("ArtistText")).FontSize);
            var preview = new TrackNotificationPreview { Settings = settings, Snapshot = snapshot, WorkAreaSize = new Size(1920, 1040) };
            preview.Measure(new Size(800, 800));
            preview.Arrange(new Rect(0, 0, 800, 800));
            preview.UpdateLayout();
            var card = (Border)preview.FindName("PositionCard");
            var before = Canvas.GetLeft(card);
            preview.Settings = settings with { HorizontalOffsetDip = 120 };
            Assert.AreEqual(before + 120, Canvas.GetLeft(card));
            return Task.CompletedTask;
        });
    }
}
