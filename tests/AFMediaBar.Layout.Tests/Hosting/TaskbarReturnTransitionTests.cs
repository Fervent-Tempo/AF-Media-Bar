// 使用单调时钟推进恢复帧，验证连续回位和重定向；不创建桌面窗口。
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>安全区内的位置和宽度过渡回归。</summary>
[TestClass]
public sealed class TaskbarReturnTransitionTests
{
    [TestMethod]
    public void SecondClosureRetargetsFromCurrentFrameWithoutJumping()
    {
        var transition = new TaskbarReturnTransition();
        transition.Retarget(600, 300, 500, 400, 0, 0.22);
        var frame = transition.Advance(0.1);
        Assert.IsTrue(frame.Position < 600 && frame.Position > 500);
        Assert.IsTrue(frame.Width > 300 && frame.Width < 400);
        transition.Retarget(frame.Position, frame.Width, 300, 500, 0.1, 0.22);
        Assert.AreEqual(frame, transition.Advance(0.1));
        var final = transition.Advance(0.33);
        Assert.AreEqual((300.0, 500.0), final);
        Assert.IsFalse(transition.IsActive);
    }

    [TestMethod]
    public void RepeatedIdenticalTargetDoesNotRestartDuration()
    {
        var transition = new TaskbarReturnTransition();
        transition.Retarget(500, 300, 200, 600, 1, 0.22);
        var frame = transition.Advance(1.1);
        transition.Retarget(frame.Position, frame.Width, 200, 600, 1.1, 0.22);
        Assert.AreEqual((200.0, 600.0), transition.Advance(1.23));
        Assert.IsFalse(transition.IsActive);
    }
}
