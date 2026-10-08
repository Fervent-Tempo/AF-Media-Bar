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
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var resource in new[] { "MotionResources", "SettingsAppearanceResources", "SettingsDiagrams", "SettingsComponentResources" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative)
                    });
                // Parse the actual page BAML so enum literals, icons and resources are checked at runtime.
                _ = new AFMediaBar.Views.Pages.DisplayModesPage(null!);
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
                    Assert.AreSame(control.FindName(arrangement == TaskbarContentArrangement.Right
                        ? "TaskbarNextButton" : "TaskbarPreviousButton"), transportButtons.Children[0]);
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
                SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                { Arrangement = TaskbarContentArrangement.Left };
                SettingsManager.Current.Position = TaskbarBarPosition.Start;
                control.ApplyTaskbarExperienceSettings();
                var transport = (StackPanel)control.FindName("TaskbarTransportButtons");
                Assert.AreSame(control.FindName("TaskbarPreviousButton"), transport.Children[0]);
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
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
}
