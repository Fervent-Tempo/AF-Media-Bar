// 在既有隔离 WPF 测试进程中检查实际控件，避免为每个方向重复启动 Application。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Reflection;
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
            SettingsManager.Current.PerformanceComponent = new([MetricKind.SystemCpu, MetricKind.ProcessMemory, MetricKind.SystemNetwork], 500, false)
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
                Assert.AreEqual(width - 4, text.Width, 0.01);
                Assert.AreEqual(Orientation.Vertical, items.Orientation);
                var performance = (FrameworkElement)control.FindName("TaskbarPerformanceHoverSurface");
                var height = performance.Height;
                control.ApplyPerformanceSnapshot(new(100, 100, 100, 99999), 0, false);
                control.ApplyPrimaryLength(600);
                Assert.AreEqual(height, performance.Height, 0.01, "Sampling must not move surrounding components.");
                Assert.IsTrue(items.Children.Cast<TextBlock>().All(cell => cell.Width <= width - 4));
                Assert.AreEqual(Orientation.Vertical, ((StackPanel)control.FindName("TaskbarHoverActions")).Orientation);
                var card = (Border)control.FindName("TaskbarPerformanceSurface");
                Assert.IsTrue(card.Height >= items.Children.Cast<TextBlock>().Sum(cell => cell.Height),
                    "The inner card must expose both metric rows, not retain the horizontal 24 DIP height.");
                foreach (var cell in items.Children.Cast<TextBlock>())
                    Assert.AreEqual(1, cell.Inlines.OfType<LineBreak>().Count(), "Each metric uses two readable rows.");
                control.ApplyPerformanceSnapshot(new(40, 7, 3, 123, 1024, 2048), 0, false);
                var cells = items.Children.Cast<TextBlock>().ToArray();
                Assert.IsTrue(cells[2].Inlines.OfType<Run>().First().Text.StartsWith("↑ "));
                Assert.IsTrue(cells[2].Inlines.OfType<Run>().Last().Text.StartsWith("↓ "));
                control.ApplyPerformanceSnapshot(default, 0, false);
                Assert.AreEqual("—", cells[0].Inlines.OfType<Run>().Last().Text, "Unavailable readings must replace the previous value.");
                Assert.AreEqual("—", cells[1].Inlines.OfType<Run>().Last().Text);
                Assert.IsTrue(cells.All(cell => cell.Inlines.OfType<Run>().All(run => run.FontSize is >= 1 and <= 14)));
                foreach (var onLeft in new[] { true, false })
                {
                    control.ApplyVerticalTaskbarSide(onLeft);
                    control.ApplyPrimaryLength(600);
                    var spectrum = (FrameworkElement)control.FindName("TaskbarSpectrum");
                    Assert.AreEqual(onLeft ? 90d : -90d, ((RotateTransform)spectrum.LayoutTransform).Angle);
                    Assert.IsTrue(spectrum.Height <= width - 8, $"Spectrum cross-axis overflow: {spectrum.Height} / {width}.");
                    var hover = (Grid)control.FindName("HoverRevealHost");
                    MediaBarTestAccess.GetField("_isTaskbarHoverVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, true);
                    hover.Visibility = Visibility.Visible;
                    control.ApplyPrimaryLength(600);
                    control.Measure(new Size(width, 600));
                    control.Arrange(new Rect(0, 0, width, 600));
                    control.UpdateLayout();
                    var layer = (Border)control.FindName("TaskbarHoverLayer");
                    Assert.AreSame(hover, layer.Parent, "Vertical hover stays in the media bar's visual/input tree.");
                    Assert.AreEqual(width - 4, layer.Width, 0.01);
                    Assert.AreEqual(text.Height, layer.Height, 0.01);
                    var position = layer.TranslatePoint(new Point(), control);
                    Assert.IsTrue(position.X >= 0 && position.X + layer.ActualWidth <= width + 0.01,
                        $"Hover is outside the bar: left={position.X}, actualWidth={layer.ActualWidth}, bar={width}.");
                    hover.Visibility = Visibility.Collapsed;
                    MediaBarTestAccess.GetField("_isTaskbarHoverVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, false);
                }
            }
            SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
            {
                InputDeviceVisible = true,
                HoverControls = SettingsManager.Current.TaskbarExperience.HoverControls with { InputDeviceVisible = true }
            };
            control.ApplyTaskbarExperienceSettings();
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)control.FindName("TaskbarInputDeviceHoverSurface")).Visibility);
            control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100);
            control.ApplyPrimaryLength(600);
            Assert.AreEqual(Orientation.Horizontal, ((StackPanel)control.FindName("TaskbarHoverActions")).Orientation);
            Assert.AreEqual(Orientation.Horizontal, ((StackPanel)control.FindName("TaskbarPerformanceItems")).Orientation);
            Assert.AreEqual(26d, ((Border)control.FindName("TaskbarPerformanceSurface")).Height);
            Assert.AreEqual(0d, ((RotateTransform)((FrameworkElement)control.FindName("TaskbarSpectrum")).LayoutTransform).Angle);
        }
        finally { SettingsManager.Replace(settings); }
    }

}
