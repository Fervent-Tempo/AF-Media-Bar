// 回归主轴偏移、多屏负坐标与吸附迟滞，防止共用策略让主体或组件跳位、写回错误显示器。
using System.Windows;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Views.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>覆盖两种宿主共用的拖动几何与手势失效边界，不发送全局鼠标输入。</summary>
[TestClass]
public sealed class DragSnapPolicyTests
{
    [TestMethod]
    [DataRow(1d)]
    [DataRow(1.5d)]
    [DataRow(2d)]
    public void TaskbarCenterOnlyChangesPrimaryAxis(double dpi)
    {
        var area = new Rect(-1920 * dpi, -60 * dpi, 1920 * dpi, 60 * dpi);
        var raw = new Rect(area.Left + 859.5 * dpi, area.Top + 7 * dpi, 201 * dpi, 35 * dpi);
        var result = DragSnapPolicy.Resolve(raw, area, dpi, DragSnapMode.HorizontalTaskbar, DragSnapTarget.None);
        Assert.AreEqual(DragSnapTarget.Center, result.Target);
        Assert.AreEqual(area.Left + (area.Width - raw.Width) / 2, result.Bounds.Left);
        Assert.AreEqual(raw.Top, result.Bounds.Top, "主轴居中不能重设横轴偏移。");
        var verticalArea = new Rect(area.Top, area.Left, area.Height, area.Width);
        var vertical = DragSnapPolicy.Resolve(new Rect(raw.Top, raw.Left, raw.Height, raw.Width), verticalArea, dpi,
            DragSnapMode.VerticalTaskbar, DragSnapTarget.None);
        Assert.AreEqual(result.Bounds.Left, vertical.Bounds.Top);
        Assert.AreEqual(raw.Top, vertical.Bounds.Left);
    }

    [TestMethod]
    public void HysteresisUsesRawPositionAndDoesNotStickToPreviousSnappedFrame()
    {
        var area = new Rect(0, 0, 1000, 50);
        DragSnapResult Resolve(double x, DragSnapTarget held) => DragSnapPolicy.Resolve(new Rect(x, 3, 200, 30), area, 1,
            DragSnapMode.HorizontalTaskbar, held);
        Assert.AreEqual(DragSnapTarget.None, Resolve(383.9, DragSnapTarget.None).Target);
        var entered = Resolve(384, DragSnapTarget.None);
        Assert.AreEqual(400d, entered.Bounds.Left);
        Assert.AreEqual(DragSnapTarget.Center, Resolve(424, entered.Target).Target);
        var escaped = Resolve(424.1, entered.Target);
        Assert.AreEqual(DragSnapTarget.None, escaped.Target);
        Assert.AreEqual(424.1, escaped.Bounds.Left);
        Assert.AreEqual(DragSnapTarget.None, Resolve(420, escaped.Target).Target);
    }

    [TestMethod]
    public void FourFloatingTargetsStayInsideTheirOwnNegativeCoordinateArea()
    {
        var area = new Rect(-1600, -900, 1201, 801);
        var size = new Size(301, 51);
        var raw = new Rect(area.TopLeft, size);
        var targets = DragSnapPolicy.Targets(DragSnapMode.Floating);
        Assert.AreEqual(4, targets.Length);
        foreach (var target in targets)
        {
            var expected = DragSnapPolicy.TargetBounds(raw, area, DragSnapMode.Floating, target);
            var result = DragSnapPolicy.Resolve(expected, area, 1.5, DragSnapMode.Floating, DragSnapTarget.None);
            Assert.AreEqual(target, result.Target);
            Assert.AreEqual(expected, result.Bounds);
            Assert.IsTrue(area.Contains(result.Bounds));
            if (target is DragSnapTarget.Top or DragSnapTarget.Bottom)
                Assert.AreEqual(area.Left + area.Width / 2, result.Bounds.Left + size.Width / 2);
            else Assert.AreEqual(area.Top + area.Height / 2, result.Bounds.Top + size.Height / 2);
        }
        var corner = DragSnapPolicy.Resolve(raw, area, 1, DragSnapMode.Floating, DragSnapTarget.None);
        Assert.AreEqual(DragSnapTarget.None, corner.Target, "角点不应形成第五个吸附目标。");
    }

    [TestMethod]
    public void FloatingDistanceRequiresBothCoordinatesAndClampsBeforeSnapping()
    {
        var area = new Rect(0, 0, 1000, 800);
        var diagonal = DragSnapPolicy.Resolve(new Rect(413, 13, 200, 40), area, 1, DragSnapMode.Floating, DragSnapTarget.None);
        Assert.AreEqual(DragSnapTarget.None, diagonal.Target);
        var outside = DragSnapPolicy.Resolve(new Rect(402, -500, 200, 40), area, 1, DragSnapMode.Floating, DragSnapTarget.None);
        Assert.AreEqual(DragSnapTarget.Top, outside.Target);
        Assert.AreEqual(new Rect(400, 0, 200, 40), outside.Bounds);
    }

    [TestMethod]
    public void OccupiedCenterStillPreviewsCenterAndDropAvoidanceWins()
    {
        var result = DragSnapPolicy.Resolve(new Rect(402, 7, 200, 30), new Rect(0, 0, 1000, 50), 1,
            DragSnapMode.HorizontalTaskbar, DragSnapTarget.None);
        Assert.AreEqual(400d, result.Bounds.Left);
        var free = ComponentPlacementPolicy.Subtract([new TaskbarPrimaryRange(0, 1000)], [new TaskbarPrimaryRange(350, 650)]);
        Assert.AreEqual(150, ComponentPlacementPolicy.Nearest(free, (int)result.Bounds.Left, 200));
    }

    [TestMethod]
    public void SuccessfulMergeClearsLatchAndLeavingRequiresFreshEntry()
    {
        using var session = new DragSnapSession(null);
        var context = new DragSnapContext(new Rect(0, 0, 1000, 800), 1, DragSnapMode.Floating, "monitor", 1);
        Assert.IsTrue(session.TryResolve(new Rect(400, 0, 200, 40), context, false, out _));
        Assert.AreEqual(DragSnapTarget.Top, session.Target);
        Assert.IsTrue(session.TryResolve(new Rect(420, 0, 200, 40), context, true, out _));
        Assert.AreEqual(DragSnapTarget.None, session.Target);
        Assert.IsTrue(session.TryResolve(new Rect(420, 0, 200, 40), context, false, out var free));
        Assert.AreEqual(DragSnapTarget.None, session.Target);
        Assert.AreEqual(420d, free.Left);
    }

    [TestMethod]
    public void ChangedMonitorDpiAreaOrSizeRejectsStaleGestureUntilOwnerResets()
    {
        var context = new DragSnapContext(new Rect(-1000, 0, 1000, 800), 1, DragSnapMode.Floating, "A", 1);
        var raw = new Rect(-600, 0, 200, 40);
        using var session = new DragSnapSession(null);
        foreach (var changed in new[] { context with { Dpi = 1.5 }, context with { Host = 2 }, context with { MonitorId = "B" },
            context with { Area = new Rect(0, 0, 1000, 800) }, context with { Mode = DragSnapMode.HorizontalTaskbar } })
        {
            session.Reset();
            Assert.IsTrue(session.TryResolve(raw, context, false, out _));
            Assert.IsFalse(session.TryResolve(raw, changed, false, out _));
        }
        Assert.IsFalse(session.TryResolve(new Rect(raw.Location, new Size(201, 40)), context, false, out _));
        session.Dispose();
        Assert.IsFalse(session.TryResolve(raw, context, false, out _));
    }
}
