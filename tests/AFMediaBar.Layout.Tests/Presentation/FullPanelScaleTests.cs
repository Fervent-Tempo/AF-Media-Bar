// 验证自由宽高拖动的鼠标跟随、对侧固定、贴合边锁定及工作区限制。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>内容缩放不参与窗口边缘拖动，不代替真实 DPI 和鼠标验收。</summary>
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
    public void DraggedEdgesFollowEachMouseAxisAndOppositeEdgesStayFixed(FullPanelPlacementSide side, string edge)
    {
        var initial = new Rect(-1000, 250, 440, 500);
        var delta = new Vector(-37, 69);
        var bounds = FullPanelScalePolicy.Resize(initial, delta, edge, new Size(280, 136), new Rect(-1600, 0, 1600, 1000), side);
        Assert.AreEqual(initial.Left + (edge.Contains('L') ? delta.X : 0), bounds.Left, 0.001);
        Assert.AreEqual(initial.Right + (edge.Contains('R') ? delta.X : 0), bounds.Right, 0.001);
        Assert.AreEqual(initial.Top + (edge.Contains('T') ? delta.Y : 0), bounds.Top, 0.001);
        Assert.AreEqual(initial.Bottom + (edge.Contains('B') ? delta.Y : 0), bounds.Bottom, 0.001);
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
        var initial = new Rect(300, 200, 440, 500);
        Assert.IsFalse(FullPanelScalePolicy.CanResizeEdge(edge, side));
        Assert.AreEqual(initial, FullPanelScalePolicy.Resize(initial, new Vector(100, 100), edge,
            new Size(280, 136), new Rect(0, 0, 1600, 1000), side));
    }

    [TestMethod]
    public void WorkAreaAndMinimumSizeLimitDraggedEdgeWithoutMovingOppositeCorner()
    {
        var initial = new Rect(-1000, 250, 440, 500);
        var work = new Rect(-1600, 0, 1600, 1000);
        var minimum = new Size(280, 136);
        var large = FullPanelScalePolicy.Resize(initial, new Vector(-5000, -5000), "LT", minimum, work, FullPanelPlacementSide.Above);
        Assert.AreEqual(work.Left, large.Left);
        Assert.AreEqual(work.Top, large.Top);
        Assert.AreEqual(initial.BottomRight, large.BottomRight);
        var small = FullPanelScalePolicy.Resize(initial, new Vector(5000, 5000), "LT", minimum, work, FullPanelPlacementSide.Above);
        Assert.AreEqual(minimum, small.Size);
        Assert.AreEqual(initial.BottomRight, small.BottomRight);
        Assert.AreEqual(0.75, FullPanelScalePolicy.Clamp(-1));
        Assert.AreEqual(2, FullPanelScalePolicy.Clamp(100));
        Assert.AreEqual(1, FullPanelScalePolicy.Clamp(double.NaN));
    }

    [TestMethod]
    public void ResponsiveGridReflowsWithoutReducingChildSizes()
    {
        StaTest.Run(_ =>
        {
            var grid = new ResponsiveUniformGrid { MinimumColumnWidth = 112, MaximumColumns = 3 };
            for (var i = 0; i < 6; i++) grid.Children.Add(new Border { MinWidth = 100, Height = 48 });
            foreach (var width in new[] { 400d, 240d, 120d, 400d })
            {
                grid.Measure(new Size(width, double.PositiveInfinity));
                grid.Arrange(new Rect(0, 0, width, grid.DesiredSize.Height));
                Assert.AreEqual(width >= 336 ? 3 : width >= 224 ? 2 : 1, grid.Columns);
                foreach (FrameworkElement child in grid.Children)
                {
                    Assert.IsTrue(child.ActualWidth >= 100);
                    Assert.AreEqual(48d, child.ActualHeight);
                    Assert.IsTrue(child.TranslatePoint(new Point(), grid).X + child.ActualWidth <= width + 0.01);
                }
            }
            return Task.CompletedTask;
        });
    }
}
