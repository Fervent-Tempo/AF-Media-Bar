// 在独立测试进程中验证实际 WPF 控件和按钮几何，不启动任务栏宿主或业务服务。
// 测试拥有子进程；隔离 WPF Application 的进程级生命周期，超时后结束测试子进程。
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证真实 WPF 控件的按钮锚点及重复布局稳定性。</summary>
[TestClass]
[DoNotParallelize]
public sealed class TaskbarArrangementPresentationTests
{
    [TestMethod]
    public void FractionalWidthRepeatedLayoutKeepsButtonsStableAndResetsDirection()
    {
        const string IsolationVariable = "AFMB_ALIGNMENT_PRESENTATION_ISOLATED";
        if (Environment.GetEnvironmentVariable(IsolationVariable) != "1")
        {
            // WPF allows one Application per process and its shutdown state survives this STA.
            // Running only this method in a child test host keeps other WPF regressions independent.
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("vstest");
            startInfo.ArgumentList.Add(typeof(TaskbarArrangementPresentationTests).Assembly.Location);
            startInfo.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(TaskbarArrangementPresentationTests).FullName}.{nameof(FractionalWidthRepeatedLayoutKeepsButtonsStableAndResetsDirection)}");
            startInfo.Environment[IsolationVariable] = "1";
            startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
            using var child = Process.Start(startInfo)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(60000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                Assert.Fail("Isolated WPF presentation test timed out");
            }
            Assert.AreEqual(0, child.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            return;
        }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var original = SettingsManager.Current.Clone();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                app.Resources["AfBooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var resource in new[] { "MotionResources", "SettingsAppearanceResources", "SettingsDiagrams", "SettingsComponentResources" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative)
                    });
                // Parse the actual page BAML so enum literals, icons and resources are checked at runtime.
                _ = new AFMediaBar.Views.Pages.DisplayModesPage(null!);
                _ = new AFMediaBar.Views.Pages.AppearancePage(null!, null!);
                _ = new AFMediaBar.Views.Pages.ComponentsSettingsPage(null!);
                _ = new AFMediaBar.Views.Pages.ScreenAndPlacementPage(null!);
                var control = new TaskBarMediaControl();
                control.UpdateSongInfo(MediaSnapshot.Disconnected with
                {
                    IsConnected = true,
                    Title = "A short track",
                    Artist = "Artist",
                    Duration = 180,
                    CanPlayPause = true,
                    CanSkipPrevious = true,
                    CanSkipNext = true
                });
                var actions = (StackPanel)control.FindName("TaskbarHoverActions");
                var hover = (Grid)control.FindName("HoverRevealHost");
                var button = (Button)control.FindName("TaskbarPlayPauseButton");
                var artwork = (Border)control.FindName("SongImageBorder");
                foreach (var position in new[] { TaskbarBarPosition.Start, TaskbarBarPosition.Center, TaskbarBarPosition.End })
                {
                    SettingsManager.Current.Position = position;
                    SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                    {
                        Arrangement = position == TaskbarBarPosition.End ? TaskbarContentArrangement.Right : TaskbarContentArrangement.Left,
                        SpectrumVisible = false,
                        PerformanceVisible = false,
                        OutputDeviceVisible = false,
                        VolumeVisible = false
                    };
                    control.ApplyTaskbarExperienceSettings();
                    typeof(TaskBarMediaControl).GetField("_isTaskbarHoverVisible", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(control, true);
                    hover.Visibility = Visibility.Visible;
                    double? previous = null;
                    foreach (var width in new[] { 420.3, 520.7, 620.25 })
                    {
                        control.ApplyPrimaryLength(width);
                        hover.Width = ((FrameworkElement)control.FindName("SongInfoStackPanel")).Width;
                        control.Width = width;
                        control.Height = 44;
                        control.Measure(new Size(width, 44));
                        control.Arrange(new Rect(0, 0, width, 44));
                        control.UpdateLayout();
                        var origin = position switch
                        {
                            TaskbarBarPosition.Center => 1000 - width / 2,
                            TaskbarBarPosition.End => 2000 - width,
                            _ => 0
                        };
                        var point = button.TranslatePoint(new Point(button.ActualWidth / 2, 0), control).X + origin;
                        if (previous is { } expected)
                            Assert.AreEqual(expected, point, 1.1, $"{position}: button moved when content width changed");
                        previous = point;
                        for (var frame = 0; frame < 24; frame++)
                        {
                            control.ApplyPrimaryLength(width);
                            control.Measure(new Size(width, 44));
                            control.Arrange(new Rect(0, 0, width, 44));
                            control.UpdateLayout();
                            var repeated = button.TranslatePoint(new Point(button.ActualWidth / 2, 0), control).X + origin;
                            Assert.AreEqual(point, repeated, 0.01,
                                $"{position}: repeated geometry application moved the button at frame {frame}");
                        }
                        if (position == TaskbarBarPosition.End)
                            Assert.IsTrue(artwork.TranslatePoint(new Point(), control).X > width / 2);
                        Assert.IsTrue(actions.ActualWidth > 0);
                    }
                }
                SettingsManager.Current.Position = TaskbarBarPosition.Center;
                foreach (var arrangement in new[] { TaskbarContentArrangement.Left, TaskbarContentArrangement.Right })
                {
                    SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                    {
                        Arrangement = arrangement,
                        HoverControls = SettingsManager.Current.TaskbarExperience.HoverControls with { ProgressVisible = true }
                    };
                    control.ApplyTaskbarExperienceSettings();
                    var transportButtons = (StackPanel)control.FindName("TaskbarTransportButtons");
                    Assert.AreSame(control.FindName("TaskbarPreviousButton"), transportButtons.Children[0]);
                    Assert.AreSame(control.FindName("TaskbarPlayPauseButton"), transportButtons.Children[1]);
                    Assert.AreSame(control.FindName("TaskbarNextButton"), transportButtons.Children[2]);
                    Assert.AreSame(transportButtons, actions.Children[0]);
                    Assert.AreSame(control.FindName("TaskbarDeviceButton"), actions.Children[1]);
                    Assert.AreSame(control.FindName("TaskbarVolumeButton"), actions.Children[2]);
                    Assert.AreSame(control.FindName("TaskbarHoverProgress"), actions.Children[3]);
                }
                foreach (var direction in new[] { TaskbarContentArrangement.Left, TaskbarContentArrangement.Right })
                    foreach (var alignment in new[] { TaskbarBarPosition.Start, TaskbarBarPosition.Center, TaskbarBarPosition.End })
                    {
                        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                        {
                            Arrangement = direction
                        };
                        SettingsManager.Current.Position = alignment;
                        control.ApplyTaskbarExperienceSettings();
                        typeof(TaskBarMediaControl).GetField("_isTaskbarHoverVisible", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .SetValue(control, true);
                        hover.Visibility = Visibility.Visible;
                        const double Width = 520.7;
                        double? anchor = null;
                        for (var frame = 0; frame < 24; frame++)
                        {
                            control.ApplyPrimaryLength(Width);
                            hover.Width = ((FrameworkElement)control.FindName("SongInfoStackPanel")).Width;
                            control.Width = Width;
                            control.Measure(new Size(Width, 44));
                            control.Arrange(new Rect(0, 0, Width, 44));
                            control.UpdateLayout();
                            var point = button.TranslatePoint(new Point(), control).X;
                            if (anchor is { } expected)
                                Assert.AreEqual(expected, point, 0.01, $"{direction}/{alignment}: repeated layout moved the button");
                            anchor = point;
                        }
                    }
                SettingsManager.Current.Position = TaskbarBarPosition.Center;
                SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                { Arrangement = TaskbarContentArrangement.Right, SpectrumVisible = true, PerformanceVisible = true };
                control.ApplyTaskbarExperienceSettings();
                hover.Visibility = Visibility.Visible;
                control.UseLayoutRounding = false;
                foreach (var placement in new[] { TaskbarBarPosition.Start, TaskbarBarPosition.Center, TaskbarBarPosition.End })
                    foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                    {
                        SettingsManager.Current.Position = placement;
                        var minimum = (int)Math.Ceiling(control.GetMinimumPrimaryLength(scale) * scale);
                        double? expected = null;
                        foreach (var width in new[] { minimum, minimum + 1, minimum + 2, minimum + 121 })
                        {
                            const long CenterTwice = 2001;
                            var anchorTwice = placement == TaskbarBarPosition.Center ? CenterTwice : 2000;
                            var left = AFMediaBar.Classes.Services.TaskbarPlacementPolicy.Position(anchorTwice, width, placement);
                            control.ApplyPlacementAnchor(left, anchorTwice, scale);
                            control.ApplyPrimaryLength(width / scale);
                            hover.Width = ((FrameworkElement)control.FindName("SongInfoStackPanel")).Width;
                            control.Width = width / scale;
                            control.Measure(new Size(width / scale, 44));
                            control.Arrange(new Rect(0, 0, width / scale, 44));
                            control.UpdateLayout();
                            var absolute = actions.TranslatePoint(new Point(), control).X * scale + left;
                            if (expected is { } anchor)
                                Assert.AreEqual(anchor, absolute, 0.01, $"DPI {scale}: odd/even width moved the actions");
                            expected = absolute;
                        }
                    }
                SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                { Arrangement = TaskbarContentArrangement.Left };
                SettingsManager.Current.Position = TaskbarBarPosition.Start;
                control.ApplyTaskbarExperienceSettings();
                var transport = (StackPanel)control.FindName("TaskbarTransportButtons");
                Assert.AreSame(control.FindName("TaskbarPreviousButton"), transport.Children[0]);
                AssertPerformanceLayout(control);
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                MetadataMarqueePresentationChecks.Verify();
            }
            catch (Exception error) { failure = error; }
            finally
            {
                SettingsManager.Replace(original);
                app.Shutdown();
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "WPF layout did not finish");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void AssertPerformanceLayout(TaskBarMediaControl control)
    {
        SettingsManager.Current.PerformanceComponent = new PerformanceComponentSettings(
            [MetricKind.SystemMemory, MetricKind.SystemCpu, MetricKind.SystemNetwork], 500, true)
        {
            DisplayMode = PerformanceDisplayMode.Parallel,
            MetricOrder = [MetricKind.SystemNetwork, MetricKind.SystemCpu, MetricKind.SystemMemory]
        };
        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { PerformanceVisible = true };
        control.ApplyTaskbarExperienceSettings();
        control.ApplyPerformanceSnapshot(new(40, 60, null, null, 1024, 2048), 0, true);
        var items = (StackPanel)control.FindName("TaskbarPerformanceItems");
        var surface = (Border)control.FindName("TaskbarPerformanceSurface");
        var cells = items.Children.Cast<TextBlock>().ToArray();
        Assert.AreEqual(3, cells.Length);
        Assert.IsTrue(cells.All(item => item.Visibility == Visibility.Visible));
        Assert.IsTrue(cells[0].Text.StartsWith("↑ ") && cells[0].Text.Contains("\n↓ "));
        Assert.IsTrue(cells[1].Text.StartsWith("CPU "));
        Assert.IsTrue(cells[2].Text.StartsWith("MEM "));
        var parallelWidth = surface.Width;
        control.ApplyPerformanceSnapshot(new(80, 100, null, null, 0, 0), 2, true);
        Assert.AreEqual(parallelWidth, surface.Width, "Changing ordinary readings must not resize the widget.");

        SettingsManager.Current.PerformanceComponent = SettingsManager.Current.PerformanceComponent with { DisplayMode = PerformanceDisplayMode.Cycle };
        control.ApplyTaskbarExperienceSettings();
        control.ApplyPerformanceSnapshot(new(40, 60, null, null, 1024, 2048), 0, true);
        var cycleWidth = surface.Width;
        control.ApplyPerformanceSnapshot(new(40, 60, null, null, 1024, 2048), 1, true);
        Assert.AreEqual(cycleWidth, surface.Width, "Cycling reserves the widest selected metric.");
        Assert.AreEqual(1, items.Children.Cast<TextBlock>().Count(item => item.Visibility == Visibility.Visible));
        Assert.IsTrue(items.Children.Cast<TextBlock>().Single(item => item.Visibility == Visibility.Visible).Text.StartsWith("CPU "));
        Assert.IsTrue(cycleWidth < parallelWidth);

        SettingsManager.Current.PerformanceComponent = new([MetricKind.SystemNetwork], 500, true);
        control.ApplyTaskbarExperienceSettings();
        Assert.AreEqual(1, items.Children.Count);
        control.ApplyPerformanceSnapshot(default, 0, true);
        Assert.IsTrue(((TextBlock)items.Children[0]).Text.Contains("—"));
    }
}
