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
    public void LoopHoldsBothEndsAndMovesAtConstantSpeedAcrossTheSeam()
    {
        var plan = MetadataMarqueePolicy.Create(440, 240);
        Assert.AreEqual(0d, plan.Sample(TimeSpan.FromSeconds(0.9)).Offset);
        Assert.AreEqual(40d, plan.Sample(TimeSpan.FromSeconds(2)).Offset, 0.001);
        Assert.AreEqual(MetadataMarqueePhase.TailHold, plan.Sample(TimeSpan.FromSeconds(6.4)).Phase);
        Assert.AreEqual(200d, plan.Sample(TimeSpan.FromSeconds(6.4)).Offset);
        Assert.AreEqual(240d, plan.Sample(TimeSpan.FromSeconds(7.8)).Offset, 0.001);
        Assert.AreEqual(24d, plan.Gap);
        Assert.AreEqual(0d, plan.Sample(plan.Duration).Offset);
        Assert.AreEqual(9d, MetadataMarqueePolicy.Create(440, 90).Gap);
        // 回绕时副本的起点恰好到达原文起点，尾部先完整进入视口。
        Assert.AreEqual(0d, plan.Period - plan.Segments[^1].To);
        Assert.AreEqual(plan.ViewportWidth, plan.TextWidth - plan.Tail);
    }

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
    public void ResizePreservesRemainingHeadHoldAndFinishesOnlyTheCurrentLoop()
    {
        var original = MetadataMarqueePolicy.Create(500, 240);
        var cursor = original.Sample(TimeSpan.FromSeconds(0.7));
        var resized = MetadataMarqueePolicy.Create(500, 180, cursor);
        Assert.IsFalse(resized.Repeat);
        Assert.AreEqual(0.3, resized.Segments[0].End.TotalSeconds, 0.001);
        Assert.AreEqual(MetadataMarqueePhase.ToTail, resized.Sample(TimeSpan.FromSeconds(0.4)).Phase);
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

    [TestMethod]
    public void FinishingAResizedLoopStartsTheNextLoopWithItsHeadHold()
    {
        StaTest.Run(async _ =>
        {
            var canvas = new Canvas { Width = 240, Height = 30 };
            var element = new TextBlock { Text = new string('W', 80) };
            var duplicate = new TextBlock();
            canvas.Children.Add(element); canvas.Children.Add(duplicate);
            // 完成事件由真实呈现目标驱动；独立 TextBlock 的时钟只能用于同步 Seek 检查。
            using var source = new HwndSource(new HwndSourceParameters("AFMB marquee test")
            {
                Width = 240, Height = 30, PositionX = -10000, PositionY = -10000,
                WindowStyle = unchecked((int)0x90000000), ExtendedWindowStyle = 0x80
            }) { RootVisual = canvas };
            var presenter = new MetadataMarqueePresenter(element, duplicate);
            try
            {
                presenter.Configure(true, 240, false, TextAlignment.Left);
                presenter.Clock!.Controller!.SeekAlignedToLastTick(TimeSpan.FromSeconds(3), TimeSeekOrigin.BeginTime);
                presenter.Configure(true, 180, false, TextAlignment.Left);
                var continuation = presenter.Clock!;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler completed = (_, _) => completion.TrySetResult();
                continuation.Completed += completed;
                try
                {
                    continuation.Controller!.SkipToFill();
                    await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    Assert.AreNotSame(continuation, presenter.Clock);
                    Assert.IsTrue(presenter.Plan!.Repeat);
                    Assert.AreEqual(MetadataMarqueePhase.HeadHold, presenter.Plan.Segments[0].Phase);
                }
                finally { continuation.Completed -= completed; }
            }
            finally { presenter.Stop(); }
        });
    }

    [TestMethod]
    public void DisplayFormattedCopiesHaveEnoughWidthForUntrimmedTextAndInkEdges()
    {
        StaTest.Run(_ =>
        {
            foreach (var text in new[] { new string('i', 160) + " END", "中文长标题 " + new string('W', 100) + " 👩🏽‍💻 é END" })
            {
                var canvas = new Canvas();
                TextOptions.SetTextFormattingMode(canvas, TextFormattingMode.Display);
                var element = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 16, FontStyle = FontStyles.Italic };
                var duplicate = new TextBlock();
                canvas.Children.Add(element); canvas.Children.Add(duplicate);
                var presenter = new MetadataMarqueePresenter(element, duplicate);
                try
                {
                    presenter.Configure(true, 240, true, TextAlignment.Left);
                    var reference = new TextBlock
                    {
                        Text = text, FontFamily = element.FontFamily, FontSize = element.FontSize, FontStyle = element.FontStyle,
                        Padding = element.Padding, TextTrimming = TextTrimming.None
                    };
                    TextOptions.SetTextFormattingMode(reference, TextFormattingMode.Display);
                    reference.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Assert.IsTrue(element.Width + 0.1 >= reference.DesiredSize.Width,
                        $"The full text needs {reference.DesiredSize.Width} DIP, allocated {element.Width} DIP.");
                    Assert.IsTrue(element.Padding.Left >= 2);
                    Assert.AreEqual(text, duplicate.Text);
                }
                finally { presenter.Stop(); }
            }
            return Task.CompletedTask;
        });
    }
}
