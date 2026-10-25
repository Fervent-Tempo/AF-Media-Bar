// 隔离 WPF 中核对真实位置、独立按钮、采样需求与固定手柄，不替代真实宿主鼠标验收。
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class ComponentLayoutPresentationChecks
{
    internal static void Verify(TaskBarMediaControl control)
    {
        var previous = SettingsManager.Current.Clone();
        try
        {
            SettingsManager.Current = new AppSettings();
            var settings = SettingsManager.Current;
            settings.PerformanceComponent = settings.PerformanceComponent with { DisplayMode = PerformanceDisplayMode.Parallel };
            settings.TaskbarExperience = settings.TaskbarExperience with
            {
                ComponentLayouts = new()
                {
                    Resident = [new(TaskbarRestComponent.Artwork, true), new(TaskbarRestComponent.MediaText, true),
                        new(TaskbarRestComponent.SystemGpu, true), new(TaskbarRestComponent.OutputDevice, true), new(TaskbarRestComponent.SystemCpu, true)],
                    Hover = [new(TaskbarRestComponent.NextTrack, false), new(TaskbarRestComponent.PreviousTrack, true),
                        new(TaskbarRestComponent.Volume, true), new(TaskbarRestComponent.PlayPause, true)],
                    Idle = []
                }
            };
            control.UpdateSongInfo(MediaSnapshot.Disconnected with { IsConnected = true, Title = "Layout" });
            Assert.IsTrue(control.EffectivePerformanceSettings.Metrics!.Contains(MetricKind.SystemGpu), "独立 GPU 不能被轮换参与项关闭。");
            foreach (var mode in new[] { WindowMode.Taskbar, WindowMode.FloatingBar })
                foreach (var vertical in new[] { false, true })
                    foreach (var arrangement in new[] { TaskbarContentArrangement.Left, TaskbarContentArrangement.Right })
                    {
                        settings.TaskbarExperience = settings.TaskbarExperience with { Arrangement = arrangement };
                        control.ApplyLayout(mode, vertical ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal, 100, 100, 44);
                        control.ApplyPrimaryLength(900);
                        control.Width = vertical ? 44 : 900; control.Height = vertical ? 900 : 44;
                        control.Measure(new Size(control.Width, control.Height));
                        control.Arrange(new Rect(0, 0, control.Width, control.Height)); control.UpdateLayout();
                        var handle = (Border)MediaBarTestAccess.GetField("_barMoveHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
                        var artwork = (Border)control.FindName("SongImageBorder");
                        var handlePoint = handle.TranslatePoint(new Point(), control);
                        var coverPoint = artwork.TranslatePoint(new Point(), control);
                        Assert.AreEqual(12d, vertical ? handle.Height : handle.Width, .01);
                        Assert.IsTrue(control.IsBarDragHandleHit(handle));
                        Assert.IsFalse(control.IsBarDragHandleHit(artwork));
                        if (vertical) Assert.IsTrue(handlePoint.Y > coverPoint.Y, "竖向手柄始终在底端。");
                        else Assert.IsTrue(arrangement == TaskbarContentArrangement.Left ? handlePoint.X > coverPoint.X : handlePoint.X < coverPoint.X);
                        Assert.AreEqual(Visibility.Visible, ((FrameworkElement)control.FindName("TaskbarPreviousButton")).Visibility);
                        Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)control.FindName("TaskbarNextButton")).Visibility);
                        var actions = (StackPanel)control.FindName("TaskbarHoverActions");
                        Assert.AreEqual("TaskbarPreviousButton", ((FrameworkElement)actions.Children[1]).Name);
                    }
            control.UpdateSongInfo(MediaSnapshot.Disconnected);
            control.ApplyTaskbarExperienceSettings();
            Assert.IsTrue(control.IsRestLayerEmpty, "全关闭的无媒体布局必须隐藏媒体栏。");
            var hidden = (Border)MediaBarTestAccess.GetField("_barMoveHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
            Assert.AreEqual(Visibility.Collapsed, hidden.Visibility);
        }
        finally { SettingsManager.Replace(previous); }
    }
}
