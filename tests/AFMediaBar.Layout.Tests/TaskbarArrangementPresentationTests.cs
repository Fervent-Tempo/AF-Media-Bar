// Exercises the compiled WPF control and button geometry without creating a taskbar HWND or starting services.
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TaskbarArrangementPresentationTests
{
    [TestMethod]
    public void CompiledControlKeepsButtonsAtTheirAnchorsAndResetsDirection()
    {
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
                        Arrangement = TaskbarArrangement.Automatic,
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
                    foreach (var width in new[] { 420.0, 520.0, 620.0 })
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
                        if (position == TaskbarBarPosition.End)
                            Assert.IsTrue(artwork.TranslatePoint(new Point(), control).X > width / 2);
                        Assert.IsTrue(actions.ActualWidth > 0);
                    }
                }
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
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "WPF layout did not finish");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
