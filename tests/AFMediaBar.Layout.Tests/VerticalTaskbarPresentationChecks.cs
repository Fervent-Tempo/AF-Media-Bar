// 在既有隔离 WPF 测试进程中检查实际控件，避免为每个方向重复启动 Application。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class VerticalTaskbarPresentationChecks
{
    internal static void Verify(TaskBarMediaControl control)
    {
        var settings = SettingsManager.Current.Clone();
        try
        {
            SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
            {
                PerformanceVisible = true,
                SpectrumVisible = true,
                MediaFontSizePercent = 125
            };
            SettingsManager.Current.PerformanceComponent = new([MetricKind.SystemCpu, MetricKind.ProcessMemory], 500, false)
            {
                DisplayMode = PerformanceDisplayMode.Parallel
            };
            foreach (var width in new[] { 40d, 48d, 72d })
            {
                control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Vertical, 100, 70, width);
                control.ApplyPrimaryLength(600);
                control.Width = width;
                control.Height = 600;
                control.Measure(new Size(width, 600));
                control.Arrange(new Rect(0, 0, width, 600));
                var artwork = (Border)control.FindName("SongImageBorder");
                var title = (TextBlock)control.FindName("SongTitle");
                var text = (FrameworkElement)control.FindName("SongInfoStackPanel");
                var items = (StackPanel)control.FindName("TaskbarPerformanceItems");
                Assert.AreEqual(width - 4, artwork.Width, 0.01);
                Assert.AreEqual(22.5, title.FontSize, 0.01, "Taskbar thickness must not shrink text.");
                Assert.AreEqual(2, Canvas.GetLeft(text), 0.01);
                Assert.AreEqual(width - 4, text.Width, 0.01);
                Assert.AreEqual(Orientation.Vertical, items.Orientation);
                var performance = (FrameworkElement)control.FindName("TaskbarPerformanceHoverSurface");
                var height = performance.Height;
                control.ApplyPerformanceSnapshot(new(100, 100, 100, 99999), 0, false);
                control.ApplyPrimaryLength(600);
                Assert.AreEqual(height, performance.Height, 0.01, "Sampling must not move surrounding components.");
                Assert.IsTrue(items.Children.Cast<TextBlock>().All(cell => cell.Width <= width - 4));
                Assert.AreEqual(Orientation.Vertical, ((StackPanel)control.FindName("TaskbarHoverActions")).Orientation);
            }
            control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100);
            control.ApplyPrimaryLength(600);
            Assert.AreEqual(Orientation.Horizontal, ((StackPanel)control.FindName("TaskbarHoverActions")).Orientation);
            Assert.AreEqual(Orientation.Horizontal, ((StackPanel)control.FindName("TaskbarPerformanceItems")).Orientation);
        }
        finally { SettingsManager.Replace(settings); }
    }
}
