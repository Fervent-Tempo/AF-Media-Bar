// 验证等比拖动的边缘跟随、对侧与贴合边固定，以及工作区限制。
using System.Windows;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>窗口和内容同步缩放，不代替真实 DPI 和鼠标验收。</summary>
[TestClass]
public sealed class FullPanelScaleTests
{
    [DataTestMethod]
    [DataRow(FullPanelPlacementSide.Above, "L")]
    [DataRow(FullPanelPlacementSide.Above, "R")]
    [DataRow(FullPanelPlacementSide.Above, "T")]
    [DataRow(FullPanelPlacementSide.Above, "LT")]
    [DataRow(FullPanelPlacementSide.Above, "RT")]
    [DataRow(FullPanelPlacementSide.Below, "B")]
    [DataRow(FullPanelPlacementSide.Below, "LB")]
    [DataRow(FullPanelPlacementSide.Below, "RB")]
    [DataRow(FullPanelPlacementSide.Left, "L")]
    [DataRow(FullPanelPlacementSide.Right, "R")]
    public void DraggedEdgeFollowsMouseWhileWindowAndContentKeepOneScale(FullPanelPlacementSide side, string edge)
    {
        var initial = new Rect(-1000, 250, 440, 516);
        var delta = new Vector(edge.Contains('L') ? -42.4 : edge.Contains('R') ? 42.4 : 0,
            edge.Contains('T') ? -50 : edge.Contains('B') ? 50 : 0);
        var scale = FullPanelScalePolicy.DragScale(1, delta, new Size(424, 500), edge, new Size(2000, 1500));
        Assert.AreEqual(1.1, scale, 0.001);
        var bounds = FullPanelScalePolicy.PlaceResizeBounds(initial, new Size(16 + 424 * scale, 16 + 500 * scale), edge, side);
        if (edge.Contains('L')) Assert.AreEqual(initial.Left + delta.X, bounds.Left, 0.001);
        if (edge.Contains('R')) Assert.AreEqual(initial.Right + delta.X, bounds.Right, 0.001);
        if (edge.Contains('T')) Assert.AreEqual(initial.Top + delta.Y, bounds.Top, 0.001);
        if (edge.Contains('B')) Assert.AreEqual(initial.Bottom + delta.Y, bounds.Bottom, 0.001);
        if (edge.Contains('L') || side == FullPanelPlacementSide.Left) Assert.AreEqual(initial.Right, bounds.Right, 0.001);
        if (edge.Contains('R') || side == FullPanelPlacementSide.Right) Assert.AreEqual(initial.Left, bounds.Left, 0.001);
        if (edge.Contains('T') || side == FullPanelPlacementSide.Above) Assert.AreEqual(initial.Bottom, bounds.Bottom, 0.001);
        if (edge.Contains('B') || side == FullPanelPlacementSide.Below) Assert.AreEqual(initial.Top, bounds.Top, 0.001);
    }

    [DataTestMethod]
    [DataRow(FullPanelPlacementSide.Above, "B")]
    [DataRow(FullPanelPlacementSide.Above, "LB")]
    [DataRow(FullPanelPlacementSide.Below, "T")]
    [DataRow(FullPanelPlacementSide.Below, "RT")]
    [DataRow(FullPanelPlacementSide.Left, "R")]
    [DataRow(FullPanelPlacementSide.Right, "L")]
    public void AttachedEdgeCannotBeResized(FullPanelPlacementSide side, string edge)
    {
        Assert.IsFalse(FullPanelScalePolicy.CanResizeEdge(edge, side));
    }

    [TestMethod]
    public void WorkAreaLimitsUniformScaleWithoutMovingOppositeCorner()
    {
        var initial = new Rect(-1000, 250, 440, 516);
        var work = new Rect(-1600, 0, 1600, 1000);
        var limit = FullPanelScalePolicy.GetResizeLimit(initial, work, "LT", FullPanelPlacementSide.Above);
        var scale = FullPanelScalePolicy.DragScale(1, new Vector(-5000, -5000), new Size(424, 500), "LT", limit);
        var bounds = FullPanelScalePolicy.PlaceResizeBounds(initial, new Size(16 + 424 * scale, 16 + 500 * scale), "LT", FullPanelPlacementSide.Above);
        Assert.IsTrue(work.Contains(bounds));
        Assert.AreEqual(work.Top, bounds.Top);
        Assert.AreEqual(initial.BottomRight, bounds.BottomRight);
        Assert.AreEqual(0.75, FullPanelScalePolicy.DragScale(1, new Vector(5000, 5000), new Size(424, 500), "LT", limit));
        Assert.AreEqual(0.75, FullPanelScalePolicy.Clamp(-1));
        Assert.AreEqual(2, FullPanelScalePolicy.Clamp(100));
        Assert.AreEqual(1, FullPanelScalePolicy.Clamp(double.NaN));
    }
}
