using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 悬停层进度条拖动跳转换算的测试：位置夹取、非法输入与"不可跳转来源保持只读"。
/// Tests for the hover-layer drag-to-seek math: position clamping, invalid inputs, and "a non-seekable source stays
/// display-only".
/// </summary>
[TestClass]
public sealed class TaskbarHoverSeekPolicyTests
{
    [TestMethod]
    public void RatioMapsTheBarEndsAndMiddle()
    {
        Assert.AreEqual(0.0, TaskbarHoverSeekPolicy.ResolveRatio(0, 96));
        Assert.AreEqual(0.5, TaskbarHoverSeekPolicy.ResolveRatio(48, 96));
        Assert.AreEqual(1.0, TaskbarHoverSeekPolicy.ResolveRatio(96, 96));
    }

    [TestMethod]
    public void RatioClampsBothEnds()
    {
        Assert.AreEqual(0.0, TaskbarHoverSeekPolicy.ResolveRatio(-25, 96));
        Assert.AreEqual(1.0, TaskbarHoverSeekPolicy.ResolveRatio(200, 96));
    }

    [TestMethod]
    public void RatioRejectsInvalidInputs()
    {
        Assert.IsNull(TaskbarHoverSeekPolicy.ResolveRatio(double.NaN, 96));
        Assert.IsNull(TaskbarHoverSeekPolicy.ResolveRatio(double.PositiveInfinity, 96));
        Assert.IsNull(TaskbarHoverSeekPolicy.ResolveRatio(48, 0));
        Assert.IsNull(TaskbarHoverSeekPolicy.ResolveRatio(48, -10));
        Assert.IsNull(TaskbarHoverSeekPolicy.ResolveRatio(48, double.NaN));
    }

    [TestMethod]
    public void TargetSecondsScaleWithDuration()
    {
        Assert.AreEqual(25.0, TaskbarHoverSeekPolicy.ResolveTargetSeconds(canSeek: true, durationSeconds: 100, pointerX: 24, trackWidth: 96));
        Assert.AreEqual(50.0, TaskbarHoverSeekPolicy.ResolveTargetSeconds(canSeek: true, durationSeconds: 100, pointerX: 48, trackWidth: 96));
        Assert.AreEqual(100.0, TaskbarHoverSeekPolicy.ResolveTargetSeconds(canSeek: true, durationSeconds: 100, pointerX: 96, trackWidth: 96));
    }

    [TestMethod]
    public void NonSeekableSourcesAndInvalidDurationsStayDisplayOnly()
    {
        // 不能跳转、时长无效或条宽无效时不产生目标：控件据此完全不起手势。
        // A non-seekable source, an invalid duration, or a bar without width produces no target: the control then never
        // starts the gesture.
        Assert.IsNull(TaskbarHoverSeekPolicy.ResolveTargetSeconds(canSeek: false, durationSeconds: 100, pointerX: 48, trackWidth: 96));
        Assert.IsNull(TaskbarHoverSeekPolicy.ResolveTargetSeconds(canSeek: true, durationSeconds: 0, pointerX: 48, trackWidth: 96));
        Assert.IsNull(TaskbarHoverSeekPolicy.ResolveTargetSeconds(canSeek: true, durationSeconds: double.NaN, pointerX: 48, trackWidth: 96));
        Assert.IsNull(TaskbarHoverSeekPolicy.ResolveTargetSeconds(canSeek: true, durationSeconds: 100, pointerX: 48, trackWidth: 0));
    }
}
