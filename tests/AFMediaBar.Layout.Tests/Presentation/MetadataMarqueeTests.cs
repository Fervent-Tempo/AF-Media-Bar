// 验证循环阅读节奏与 WPF 时钟生命周期；测试只拥有自己的文字元素，不启动播放器或任务栏。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Classes.Services;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>长标题循环、续跑与完整文字渲染回归。</summary>
[TestClass]
public sealed class MetadataMarqueeTests
{
    [TestMethod]
    public void ResizeContinuesFromItsPhaseWithoutReplayingPassedTailHold()
    {
        var original = MetadataMarqueePolicy.Create(500, 240);
        var moving = original.Sample(TimeSpan.FromSeconds(6));
        var expanded = MetadataMarqueePolicy.Create(500, 350, moving);
        Assert.AreEqual(moving.Offset, expanded.InitialOffset);
        Assert.IsFalse(expanded.Segments.Any(segment => segment.Phase == MetadataMarqueePhase.TailHold));
        Assert.IsTrue(expanded.Segments.All(segment => segment.To >= segment.From));
        var tail = original.Sample(TimeSpan.FromSeconds(7.8));
        var same = MetadataMarqueePolicy.Create(500, 240, tail);
        Assert.AreEqual(tail.HoldRemaining, same.Segments[0].End);
        Assert.IsFalse(MetadataMarqueePolicy.Create(500, 350, tail).Segments.Any(segment => segment.Phase == MetadataMarqueePhase.TailHold));
        var shrunk = MetadataMarqueePolicy.Create(500, 120, tail);
        Assert.AreEqual(MetadataMarqueePhase.ToTail, shrunk.Segments[0].Phase);
        Assert.AreEqual(380d, shrunk.Segments[0].To);
        var afterTail = original.Sample(TimeSpan.FromSeconds(10));
        var laterResize = MetadataMarqueePolicy.Create(500, 120, afterTail);
        Assert.IsFalse(laterResize.Segments.Any(segment => segment.Phase == MetadataMarqueePhase.TailHold));
        var seam = MetadataMarqueePolicy.Create(500, 120, new(MetadataMarqueePhase.ToHead, 520, TimeSpan.Zero));
        Assert.AreEqual(8d, seam.InitialOffset);
        Assert.AreEqual(MetadataMarqueePhase.ToTail, seam.Segments[0].Phase);
    }

    [TestMethod]
    public void UnicodeCopiesStayCompleteAndClockPauseDoesNotReplaceContent()
    {
        StaTest.Run(async _ =>
        {
            var text = string.Concat(Enumerable.Repeat("中文 video 👩🏽‍💻 é END ", 12));
            var element = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 16 };
            var duplicate = new TextBlock { IsHitTestVisible = false };
            var presenter = new MetadataMarqueePresenter(element, duplicate);
            try
            {
                Assert.IsTrue(presenter.Configure(true, 240, false, TextAlignment.Center));
                var clock = presenter.Clock!;
                clock.Controller!.SeekAlignedToLastTick(TimeSpan.FromSeconds(3), TimeSeekOrigin.BeginTime);
                Assert.AreEqual(-80d, presenter.Transform.X, 0.01);
                presenter.Configure(true, 240, true, TextAlignment.Center);
                await Task.Yield();
                Assert.IsTrue(presenter.IsPaused);
                Assert.AreSame(clock, presenter.Clock);
                Assert.AreEqual(text, element.Text);
                Assert.AreEqual(text, duplicate.Text);
                Assert.AreEqual(TextTrimming.None, element.TextTrimming);
                Assert.AreEqual(element.Width + presenter.Plan!.Gap, Canvas.GetLeft(duplicate), 0.001);
                presenter.Configure(true, 240, false, TextAlignment.Center);
                Assert.AreSame(clock, presenter.Clock);
                Assert.IsFalse(presenter.IsPaused);
                presenter.Configure(false, 240, false, TextAlignment.Right);
                Assert.IsNull(presenter.Clock);
                Assert.AreEqual(TextTrimming.CharacterEllipsis, element.TextTrimming);
                Assert.AreEqual(TextAlignment.Right, element.TextAlignment);
                Assert.AreEqual(0d, presenter.Transform.X);
                Assert.AreEqual(text, element.Text);
            }
            finally { presenter.Stop(); }
        });
    }

    [TestMethod]
    public void WidthAndDpiContinueButTextAndFontRestartAtTheHead()
    {
        StaTest.Run(_ =>
        {
            var element = new TextBlock { Text = new string('W', 80), FontSize = 16 };
            var presenter = new MetadataMarqueePresenter(element, new TextBlock());
            try
            {
                presenter.Configure(true, 240, false, TextAlignment.Left, 1);
                presenter.Clock!.Controller!.SeekAlignedToLastTick(TimeSpan.FromSeconds(3), TimeSeekOrigin.BeginTime);
                var position = -presenter.Transform.X;
                presenter.Configure(true, 180, true, TextAlignment.Left, 1.5);
                Assert.AreEqual(position, presenter.Plan!.InitialOffset, 0.01);
                Assert.IsFalse(presenter.Plan.Repeat);
                Assert.AreEqual(MetadataMarqueePhase.ToTail, presenter.Plan.Segments[0].Phase);
                element.Text += " changed";
                presenter.Configure(true, 180, true, TextAlignment.Left, 1.5);
                Assert.IsTrue(presenter.Plan!.Repeat);
                Assert.AreEqual(0d, presenter.Plan.InitialOffset);
                Assert.AreEqual(MetadataMarqueePhase.HeadHold, presenter.Plan.Segments[0].Phase);
                presenter.Clock!.Controller!.SeekAlignedToLastTick(TimeSpan.FromSeconds(3), TimeSeekOrigin.BeginTime);
                element.FontSize = 18;
                presenter.Configure(true, 180, false, TextAlignment.Left, 1.5);
                Assert.AreEqual(0d, presenter.Plan!.InitialOffset);
            }
            finally { presenter.Stop(); }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void CompletingAnObsoleteResizeClockCannotRestartAfterStopOrReplacement()
    {
        StaTest.Run(async _ =>
        {
            var element = new TextBlock { Text = new string('W', 80) };
            var duplicate = new TextBlock();
            var presenter = new MetadataMarqueePresenter(element, duplicate);
            try
            {
                presenter.Configure(true, 240, false, TextAlignment.Left);
                presenter.Configure(true, 180, false, TextAlignment.Left);
                var obsolete = presenter.Clock!;
                element.Text = new string('X', 90);
                presenter.Configure(true, 180, false, TextAlignment.Left);
                var replacement = presenter.Clock;
                obsolete.Controller!.SkipToFill();
                await Task.Yield();
                Assert.AreSame(replacement, presenter.Clock);
                presenter.Configure(true, 160, false, TextAlignment.Left);
                var removed = presenter.Clock!;
                presenter.Stop(); presenter.Stop();
                removed.Controller!.SkipToFill();
                await Task.Yield();
                Assert.IsNull(presenter.Clock);
                Assert.IsFalse(presenter.Transform.HasAnimatedProperties);
                Assert.AreEqual(Visibility.Collapsed, duplicate.Visibility);
                Assert.AreEqual(new string('X', 90), element.Text);
            }
            finally { presenter.Stop(); }
        });
    }
}
