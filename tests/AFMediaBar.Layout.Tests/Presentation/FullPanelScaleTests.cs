// 验证同一缩放系数在四个贴合方向的拖动符号与中心展开语义，不代替真实 DPI 验收。
using System.Windows;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止锚定缩放方向相反以及极端输入破坏几何。</summary>
[TestClass]
public sealed class FullPanelScaleTests
{
    [DataTestMethod]
    [DataRow(FullPanelPlacementSide.Above, "T", 0d, -50d)]
    [DataRow(FullPanelPlacementSide.Below, "B", 0d, 50d)]
    [DataRow(FullPanelPlacementSide.Left, "L", -42.4d, 0d)]
    [DataRow(FullPanelPlacementSide.Right, "R", 42.4d, 0d)]
    public void OutwardEdgeEnlargesWithoutMovingAttachment(FullPanelPlacementSide side, string edge, double x, double y)
    {
        var scale = FullPanelScalePolicy.DragScale(1, new Vector(x, y), new Size(FullPanelScalePolicy.ContentWidthDip, 500), edge, side);
        Assert.AreEqual(1.1, scale, 0.001);
        var work = new Rect(-1600, 0, 1600, 1000);
        var anchor = new Rect(-900, 400, 200, 40);
        var before = FullPanelPlacementPolicy.PlacePanel(anchor, new Size(16 + FullPanelScalePolicy.ContentWidthDip, 16 + 300), work, side);
        var after = FullPanelPlacementPolicy.PlacePanel(anchor, new Size(16 + FullPanelScalePolicy.ContentWidthDip * scale, 16 + 300 * scale), work, side);
        Assert.IsTrue(work.Contains(after));
        var visibleBefore = new Rect(before.Left + 8, before.Top + 8, before.Width - 16, before.Height - 16);
        var visibleAfter = new Rect(after.Left + 8, after.Top + 8, after.Width - 16, after.Height - 16);
        Assert.AreEqual(visibleBefore.Width * scale, visibleAfter.Width, 0.001);
        Assert.AreEqual(visibleBefore.Height * scale, visibleAfter.Height, 0.001);
        switch (side)
        {
            case FullPanelPlacementSide.Above: Assert.AreEqual(visibleBefore.Bottom, visibleAfter.Bottom); break;
            case FullPanelPlacementSide.Below: Assert.AreEqual(visibleBefore.Top, visibleAfter.Top); break;
            case FullPanelPlacementSide.Left: Assert.AreEqual(visibleBefore.Right, visibleAfter.Right); break;
            case FullPanelPlacementSide.Right: Assert.AreEqual(visibleBefore.Left, visibleAfter.Left); break;
        }
    }

    [TestMethod]
    public void TangentialEdgeExpandsSymmetricallyAndScaleRemainsBounded()
    {
        Assert.AreEqual(1.2, FullPanelScalePolicy.DragScale(1, new Vector(-42.4, 0), new Size(FullPanelScalePolicy.ContentWidthDip, 500), "L", FullPanelPlacementSide.Above), 0.001);
        Assert.AreEqual(0.75, FullPanelScalePolicy.Clamp(-1));
        Assert.AreEqual(2, FullPanelScalePolicy.Clamp(100));
        Assert.AreEqual(1, FullPanelScalePolicy.Clamp(double.NaN));
    }
}
