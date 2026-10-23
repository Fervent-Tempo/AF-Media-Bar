// 在自有 Dispatcher 中检查反向接续、重复目标和停止后的时钟回收，不依赖全局动效设置。
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class ColorTransitionBrushTests
{
    [TestMethod]
    public void ReversalContinuesFromDisplayedColorAndFinishKeepsLatestTarget()
    {
        RunWithHost(async (transition, brush) =>
        {
            transition.Apply(Colors.Black, TimeSpan.FromSeconds(1));
            await WaitUntilAsync(() => brush.Color.ScR is > 0.1f and < 0.8f);
            var displayed = brush.Color;
            transition.Apply(Colors.White, TimeSpan.FromSeconds(1));
            Assert.AreEqual(displayed, brush.Color, "反向换色必须从当前画面接续。");
            transition.Finish();
            Assert.IsFalse(brush.HasAnimatedProperties);
            Assert.AreEqual(Colors.White, brush.Color);
            await Task.Delay(1100);
            Assert.AreEqual(Colors.White, brush.Color, "旧动画不能覆盖停止后的目标。");
            Assert.IsFalse(brush.HasAnimatedProperties);
        });
    }

    [TestMethod]
    public void RepeatedTargetCompletesWithoutRestartingAndReleasesClock()
    {
        RunWithHost(async (transition, brush) =>
        {
            transition.Apply(Colors.Black, TimeSpan.FromMilliseconds(180));
            var timeout = Stopwatch.StartNew();
            while (brush.HasAnimatedProperties && timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                transition.Apply(Colors.Black, TimeSpan.FromMilliseconds(180));
                await Task.Delay(20);
            }
            Assert.IsFalse(brush.HasAnimatedProperties, "重复的目标不能一直重启颜色动画。");
            Assert.AreEqual(Colors.Black, brush.Color);
        });
    }

    private static void RunWithHost(Func<ColorTransitionBrush, SolidColorBrush, Task> verify)
    {
        StaTest.Run(async _ =>
        {
            var transition = new ColorTransitionBrush();
            var brush = transition.Apply(Colors.White, TimeSpan.Zero);
            var window = new Window
            {
                Content = new Border { Background = brush },
                Width = 64,
                Height = 32,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            try
            {
                window.Show();
                await verify(transition, brush);
            }
            finally { transition.Finish(); window.Close(); }
        });
    }

    private static async Task WaitUntilAsync(Func<bool> ready)
    {
        var timeout = Stopwatch.StartNew();
        while (!ready() && timeout.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        Assert.IsTrue(ready(), "颜色动画未在有界等待内推进。");
    }
}
