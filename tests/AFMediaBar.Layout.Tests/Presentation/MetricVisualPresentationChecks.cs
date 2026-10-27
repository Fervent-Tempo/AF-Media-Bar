// 在现有隔离 WPF 进程中验证图形样式的宽度稳定、竖向容纳及实际绘制；不启动真实采样。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class MetricVisualPresentationChecks
{
    internal static void Verify(TaskBarMediaControl control)
    {
        var previous = SettingsManager.Current.Clone();
        try
        {
            SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { PerformanceVisible = true };
            foreach (var style in Enum.GetValues<MetricVisualStyle>())
            {
                var visuals = new MetricVisualSettings();
                foreach (var metric in Enum.GetValues<MetricKind>()) visuals = visuals.With(metric, style);
                SettingsManager.Current.PerformanceComponent = new(Enum.GetValues<MetricKind>(), 500, true)
                { DisplayMode = PerformanceDisplayMode.Parallel, Visuals = visuals };
                foreach (var vertical in new[] { false, true })
                {
                    control.ApplyLayout(WindowMode.Taskbar, vertical ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal, 100, 100, 44);
                    control.ApplyPrimaryLength(1400);
                    control.Width = vertical ? 44 : 1400;
                    control.Height = vertical ? 1400 : 44;
                    control.ApplyTaskbarExperienceSettings();
                    control.ApplyPerformanceSnapshot(new(40, 60, 20, 200, 1024, 2048), 0, true);
                    control.Measure(new Size(control.Width, control.Height));
                    control.Arrange(new Rect(0, 0, control.Width, control.Height));
                    control.UpdateLayout();
                    var surface = (Border)control.FindName("TaskbarPerformanceSurface");
                    var items = (StackPanel)control.FindName("TaskbarPerformanceItems");
                    var width = surface.Width;
                    var height = surface.Height;
                    control.ApplyPerformanceSnapshot(new(100, 0, null, null, 0, 0), 0, true);
                    Assert.AreEqual(width, surface.Width, 0.01, "采样不能改变图表预留长度。");
                    Assert.AreEqual(height, surface.Height, 0.01);
                    var cards = Descendants(control).OfType<MetricPresenter>().Where(card => ((Border)card.Parent).Visibility == Visibility.Visible).ToArray();
                    Assert.AreEqual(5, cards.Length, "并列模式应有五个独立指标卡片。");
                    foreach (var card in cards)
                    {
                        Assert.IsTrue(card.ActualWidth > 0 && card.ActualHeight > 0);
                        Assert.IsInstanceOfType<Border>(card.Parent, "每项应有独立的悬停与命中表面。");
                    }
                    var bitmap = new RenderTargetBitmap((int)control.Width, (int)control.Height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(control);
                    if (Environment.GetEnvironmentVariable("AFMB_LAYOUT_EVIDENCE") is { Length: > 0 } directory)
                    {
                        Directory.CreateDirectory(directory);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.Combine(directory, $"metrics-{style}-{(vertical ? "vertical" : "horizontal")}.png"));
                        encoder.Save(output);
                    }
                }
            }
        }
        finally { SettingsManager.Current = previous; }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

}
