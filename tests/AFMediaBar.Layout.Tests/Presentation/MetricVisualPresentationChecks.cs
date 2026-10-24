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
                    Assert.AreEqual(5, items.Children.Count);
                    if (vertical)
                        Assert.IsTrue(items.DesiredSize.Height <= surface.Height, "竖向图表和读数必须完整容纳。");
                    var bitmap = new RenderTargetBitmap((int)control.Width, (int)control.Height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(control);
                }
            }
        }
        finally { SettingsManager.Current = previous; }
    }
}
