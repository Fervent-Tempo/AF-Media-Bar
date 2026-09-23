using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace AFMediaBar.Layout.Tests;

/// <summary>显示器目标、全屏边界和物理像素放置测试。 / Display-target, fullscreen-boundary, and physical-pixel placement tests.</summary>
[TestClass]
public sealed class DisplayTargetPolicyTests
{
    [TestMethod]
    public void FixedTargetFallsBackWithoutDiscardingReconnectPreference()
    {
        var primary = Monitor("DISPLAY1", true, new Rect(0, 0, 1920, 1040), 96);
        var secondary = Monitor("DISPLAY2", false, new Rect(-2560, 0, 2560, 1400), 144);

        Assert.AreSame(secondary, DisplayTargetPolicy.ResolveFixed([primary, secondary], "display2"));
        Assert.AreSame(primary, DisplayTargetPolicy.ResolveFixed([primary], "DISPLAY2"));
        Assert.AreSame(secondary, DisplayTargetPolicy.ResolveFixed([primary, secondary], "DISPLAY2"));
        Assert.AreSame(secondary, DisplayTargetPolicy.ResolveFixed([secondary], "missing"));
        Assert.IsNull(DisplayTargetPolicy.ResolveFixed([], "missing"));
    }

    [TestMethod]
    public void ExplicitTaskbarTargetsSupportOneOrManyWithPrimaryFirst()
    {
        var primary = Monitor("DISPLAY1", true, new Rect(0, 0, 1920, 1040), 96);
        var left = Monitor("DISPLAY3", false, new Rect(-1920, 0, 1920, 1040), 96);
        var right = Monitor("DISPLAY2", false, new Rect(1920, 0, 2560, 1400), 144);

        CollectionAssert.AreEqual(
            new[] { "DISPLAY1", "DISPLAY2", "DISPLAY3" },
            TaskbarTargetPolicy.ResolveDeviceIds(
                [right, left, primary],
                ["DISPLAY3", "DISPLAY1", "DISPLAY2"]).ToArray());
        CollectionAssert.AreEqual(
            new[] { "DISPLAY2" },
            TaskbarTargetPolicy.ResolveDeviceIds([primary, right], ["DISPLAY2"]).ToArray());
        CollectionAssert.AreEqual(
            new[] { "DISPLAY1" },
            TaskbarTargetPolicy.ResolveDeviceIds([primary, right], ["DISCONNECTED"]).ToArray());
        CollectionAssert.AreEqual(
            new[] { "DISPLAY1", "DISPLAY2" },
            TaskbarTargetPolicy.ResolveDeviceIds(
                [right, primary],
                null,
                TaskbarTargetPolicy.LegacyAllTaskbarsDeviceId).ToArray());
    }

    [TestMethod]
    public void MultipleTaskbarsPublishTheSharedLengthIntersection()
    {
        var constraints = new TaskbarLengthConstraintsService();
        var first = new object();
        var second = new object();

        constraints.Update(first, 240, 900);
        constraints.Update(second, 320, 700);
        Assert.AreEqual(320, constraints.MinimumLengthDip);
        Assert.AreEqual(700, constraints.MaximumLengthDip);

        constraints.Remove(second);
        Assert.AreEqual(240, constraints.MinimumLengthDip);
        Assert.AreEqual(900, constraints.MaximumLengthDip);
    }

    [TestMethod]
    public void NotificationTargetUsesForegroundThenFixedAndPrimaryFallbacks()
    {
        var primary = Monitor("DISPLAY1", true, new Rect(0, 0, 1920, 1040), 96);
        var fixedTarget = Monitor("DISPLAY2", false, new Rect(1920, 0, 2560, 1400), 120);
        var foreground = Monitor("DISPLAY3", false, new Rect(-1920, 0, 1920, 1040), 144);
        var monitors = new[] { primary, fixedTarget, foreground };

        Assert.AreSame(foreground, DisplayTargetPolicy.ResolveNotification(
            monitors, NotificationTargetMode.ForegroundWindow, "DISPLAY2", "DISPLAY3"));
        Assert.AreSame(fixedTarget, DisplayTargetPolicy.ResolveNotification(
            monitors, NotificationTargetMode.ForegroundWindow, "DISPLAY2", null));
        Assert.AreSame(primary, DisplayTargetPolicy.ResolveNotification(
            monitors, NotificationTargetMode.ForegroundWindow, "missing", "missing"));
        Assert.AreSame(fixedTarget, DisplayTargetPolicy.ResolveNotification(
            monitors, NotificationTargetMode.Fixed, "DISPLAY2", "DISPLAY3"));
    }

    [TestMethod]
    public void SixPositionsUseNegativePhysicalWorkAreaAndClampOversizedWindows()
    {
        var area = new Rect(-1920, 0, 1920, 1040);
        var size = new Size(360, 120);

        Assert.AreEqual(new Point(-1904, 16), NotificationPlacementCalculator.Calculate(area, size, TrackChangeNotificationPosition.TopLeft, 16));
        Assert.AreEqual(new Point(-1140, 16), NotificationPlacementCalculator.Calculate(area, size, TrackChangeNotificationPosition.TopCenter, 16));
        Assert.AreEqual(new Point(-376, 16), NotificationPlacementCalculator.Calculate(area, size, TrackChangeNotificationPosition.TopRight, 16));
        Assert.AreEqual(new Point(-1904, 904), NotificationPlacementCalculator.Calculate(area, size, TrackChangeNotificationPosition.BottomLeft, 16));
        Assert.AreEqual(new Point(-1140, 904), NotificationPlacementCalculator.Calculate(area, size, TrackChangeNotificationPosition.BottomCenter, 16));
        Assert.AreEqual(new Point(-376, 904), NotificationPlacementCalculator.Calculate(area, size, TrackChangeNotificationPosition.BottomRight, 16));

        Assert.AreEqual(
            new Point(-1904, 16),
            NotificationPlacementCalculator.Calculate(area, new Size(4000, 2000), TrackChangeNotificationPosition.BottomRight, 16));
    }

    [TestMethod]
    public void DipSizeConvertsAtCommonEffectiveDpiValues()
    {
        Assert.AreEqual(new Size(360, 120), NotificationPlacementCalculator.ToPhysicalSize(new Size(360, 120), 96, 96));
        Assert.AreEqual(new Size(450, 150), NotificationPlacementCalculator.ToPhysicalSize(new Size(360, 120), 120, 120));
        Assert.AreEqual(new Size(540, 180), NotificationPlacementCalculator.ToPhysicalSize(new Size(360, 120), 144, 144));
        Assert.AreEqual(new Size(360, 120), NotificationPlacementCalculator.ToPhysicalSize(new Size(360, 120), 0, 0));
    }

    [TestMethod]
    public void FullscreenCoverageAllowsSmallFrameToleranceOnly()
    {
        var monitor = new Rect(-1920, 0, 1920, 1080);
        Assert.IsTrue(ForegroundFullscreenPolicy.IsFullscreen(new Rect(-1921, -1, 1922, 1082), monitor));
        Assert.IsTrue(ForegroundFullscreenPolicy.IsFullscreen(new Rect(-1919, 1, 1918, 1078), monitor));
        Assert.IsFalse(ForegroundFullscreenPolicy.IsFullscreen(new Rect(-1900, 20, 1880, 1040), monitor));
        Assert.IsFalse(ForegroundFullscreenPolicy.IsFullscreen(Rect.Empty, monitor));
    }

    private static DisplayMonitorInfo Monitor(string id, bool primary, Rect workArea, uint dpi) =>
        new(id, id, primary, workArea, workArea, dpi, dpi);
}
