using System.Windows;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 胶囊岛几何/动画纯逻辑测试：圆角命中、marquee 位移回绕、边缘吸附判定。
/// Tests for capsule-island geometry/animation primitives: rounded-rect hit test,
/// marquee offset wrap-around, and edge snap detection.
/// </summary>
[TestClass]
public sealed class CapsuleIslandGeometryTests
{
    [TestMethod]
    public void RoundedRectHitTest_中心点命中()
    {
        Assert.IsTrue(RoundedRectHitTest.Contains(220, 44, 22, 110, 22));
    }

    [TestMethod]
    public void RoundedRectHitTest_圆角外角点不命中()
    {
        Assert.IsFalse(RoundedRectHitTest.Contains(220, 44, 22, 1, 1));
        Assert.IsFalse(RoundedRectHitTest.Contains(220, 44, 22, 219, 43));
    }

    [TestMethod]
    public void RoundedRectHitTest_圆角内弧上命中_矩形外不命中()
    {
        // 半径 22 的圆角圆心在 (22,22)；到圆心距离恰为 22 的弧上点命中，矩形外不命中。
        const double radius = 22;
        var inset = radius - radius * Math.Sqrt(0.5);
        var arcX = 22 - inset;
        var arcY = 22 - inset;
        Assert.IsTrue(RoundedRectHitTest.Contains(220, 44, radius, arcX, arcY));
        Assert.IsFalse(RoundedRectHitTest.Contains(220, 44, radius, -1, 22));
        Assert.IsFalse(RoundedRectHitTest.Contains(220, 44, radius, 110, 60));
    }

    [TestMethod]
    public void MarqueeOffset_按速度累积并在周期长度处回绕()
    {
        var first = MarqueeOffsetCalculator.Advance(0, 60, 16, 200);
        Assert.AreEqual(60 * 0.016, first, 1e-9);
        var advanced = MarqueeOffsetCalculator.Advance(199.5, 60, 16, 200);
        Assert.IsTrue(advanced < 1.0, "越过周期末尾应回绕到周期起点");
    }

    [TestMethod]
    public void MarqueeOffset_零周期不产生位移()
    {
        Assert.AreEqual(0, MarqueeOffsetCalculator.Advance(0, 60, 16, 0));
    }

    [TestMethod]
    public void IslandEdgeSnap_靠近上边缘时吸附到上边缘()
    {
        var area = new Rect(0, 0, 1920, 1040);
        Assert.AreEqual(
            DynamicIslandEdge.Top,
            IslandEdgeSnap.FindEdge(area, new Point(960, 10), CapsuleIslandMetrics.CapsuleSize, CapsuleIslandMetrics.EdgeSnapThresholdDip));
    }

    [TestMethod]
    public void IslandEdgeSnap_远离所有边缘时不吸附()
    {
        var area = new Rect(0, 0, 1920, 1040);
        Assert.IsNull(IslandEdgeSnap.FindEdge(area, new Point(960, 300), CapsuleIslandMetrics.CapsuleSize, CapsuleIslandMetrics.EdgeSnapThresholdDip));
    }

    [TestMethod]
    public void IslandEdgeSnap_靠近右边缘时吸附到右边缘()
    {
        var area = new Rect(0, 0, 1920, 1040);
        var topLeft = new Point(area.Right - CapsuleIslandMetrics.CapsuleSize.Width - 6, 500);
        Assert.AreEqual(
            DynamicIslandEdge.Right,
            IslandEdgeSnap.FindEdge(area, topLeft, CapsuleIslandMetrics.CapsuleSize, CapsuleIslandMetrics.EdgeSnapThresholdDip));
    }
}
