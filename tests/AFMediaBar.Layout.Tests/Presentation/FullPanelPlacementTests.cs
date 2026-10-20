// 防止外侧横杆与面板方向分离；覆盖负坐标显示器与不同缩放下的物理像素/DIP 边界。
using System.Windows;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>四个展开侧在负坐标显示器上保持横杆、命中区与面板一致。</summary>
[TestClass]
public sealed class FullPanelPlacementTests
{
    [TestMethod]
    public void ShortWorkAreaLimitsPanelHeightWithoutCoveringBar()
    {
        var work = new Rect(0, 0, 1280, 600);
        var bar = new Rect(400, 240, 240, 44);
        var side = FullPanelPlacementPolicy.ResolveSide(bar, work, false);
        var limit = FullPanelPlacementPolicy.GetPanelLimit(bar, work, side);
        var panel = FullPanelPlacementPolicy.PlacePanel(bar, new Size(480, Math.Min(500, limit.Height)), work, side);
        Assert.AreEqual(FullPanelPlacementSide.Below, side);
        Assert.AreEqual(308d, panel.Height);
        Assert.AreEqual(bar.Bottom + 8, panel.Top);
        Assert.IsTrue(work.Contains(panel));
    }

    [DataTestMethod]
    [DataRow(false, -1000d, 180d, FullPanelPlacementSide.Below)]
    [DataRow(false, -1000d, 680d, FullPanelPlacementSide.Above)]
    [DataRow(true, -1400d, 300d, FullPanelPlacementSide.Right)]
    [DataRow(true, -250d, 300d, FullPanelPlacementSide.Left)]
    public void HandleAndPanelOpenTowardMonitorCenter(bool vertical, double x, double y, FullPanelPlacementSide expected)
    {
        var work = new Rect(-1600, 0, 1600, 900);
        var bar = new Rect(x, y, vertical ? 44 : 240, vertical ? 240 : 44);
        var side = FullPanelPlacementPolicy.ResolveSide(bar, work, vertical);
        Assert.AreEqual(expected, side);
        var panel = FullPanelPlacementPolicy.PlacePanel(bar, new Size(460, 360), work, side);
        const double dpi = 1.5;
        var physical = new Rect(bar.X * dpi, bar.Y * dpi, bar.Width * dpi, bar.Height * dpi);
        var screen = new Rect(work.X * dpi, work.Y * dpi, work.Width * dpi, work.Height * dpi);
        var geometry = FullPanelPlacementPolicy.PlaceHandle(physical, physical, screen,
            new Point((bar.Left + bar.Width / 2) * dpi, (bar.Top + bar.Height / 2) * dpi), side, dpi);
        Assert.IsTrue(work.Contains(panel));
        Assert.IsTrue(screen.Contains(geometry.HitBounds));
        Assert.IsTrue(geometry.HitBounds.Contains(geometry.VisualBounds));
        var overlap = Rect.Intersect(geometry.HitBounds, physical);
        Assert.IsTrue(overlap.IsEmpty || overlap.Width == 0 || overlap.Height == 0, "扩大命中区不能覆盖媒体栏。");
        switch (side)
        {
            case FullPanelPlacementSide.Above:
                Assert.IsTrue(panel.Bottom < bar.Top);
                Assert.AreEqual(physical.Top, geometry.VisualBounds.Bottom);
                break;
            case FullPanelPlacementSide.Below:
                Assert.IsTrue(panel.Top > bar.Bottom);
                Assert.AreEqual(physical.Bottom, geometry.VisualBounds.Top);
                break;
            case FullPanelPlacementSide.Left:
                Assert.IsTrue(panel.Right < bar.Left);
                Assert.AreEqual(physical.Left, geometry.VisualBounds.Right);
                break;
            case FullPanelPlacementSide.Right:
                Assert.IsTrue(panel.Left > bar.Right);
                Assert.AreEqual(physical.Right, geometry.VisualBounds.Left);
                break;
        }
    }
}
