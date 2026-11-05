// 隔离 WPF 中核对真实位置、独立按钮、采样需求与固定手柄，不替代真实宿主鼠标验收。
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
                        new(TaskbarRestComponent.NextTrack, true), new(TaskbarRestComponent.PlayPause, true), new(TaskbarRestComponent.PreviousTrack, true),
                        new(TaskbarRestComponent.SystemGpu, true), new(TaskbarRestComponent.OutputDevice, true), new(TaskbarRestComponent.SystemCpu, true)],
                    Hover = [new(TaskbarRestComponent.NextTrack, false), new(TaskbarRestComponent.PreviousTrack, true),
                        new(TaskbarRestComponent.Volume, true), new(TaskbarRestComponent.PlayPause, true)],
                    Idle = []
                }
            };
            control.UpdateSongInfo(MediaSnapshot.Disconnected with
            { IsConnected = true, Title = "Layout", IsPlaying = true, CanPlayPause = true, CanSkipPrevious = true });
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
                        var popup = (Popup)MediaBarTestAccess.GetField("_barMovePopup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
                        Assert.AreSame(handle, popup.Child);
                        Assert.IsFalse(((Grid)control.FindName("InteractionSurface")).Children.Contains(handle), "手柄不得进入背景或内容布局。");
                        Assert.AreEqual(24d, vertical ? handle.Height : handle.Width, .01);
                        Assert.IsTrue(control.IsBarDragHandleHit(handle));
                        Assert.IsFalse(control.IsBarDragHandleHit(artwork));
                        var targetSize = new Size(control.Width, control.Height);
                        var candidates = popup.CustomPopupPlacementCallback(new Size(handle.Width, handle.Height), targetSize, new Point());
                        if (vertical) Assert.AreEqual(targetSize.Height, candidates[0].Point.Y, "竖向手柄位于媒体栏外侧。");
                        else Assert.AreEqual(arrangement == TaskbarContentArrangement.Left ? targetSize.Width : -handle.Width, candidates[0].Point.X);
                        var layoutMethod = MediaBarTestAccess.GetMethod(vertical ? "ResolveVerticalLayout" : "ResolveRestLayout", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        var layout = (TaskbarRestLayout)layoutMethod.Invoke(control, vertical ? [900d] : [settings.TaskbarExperience, 900d])!;
                        Assert.IsNull(layout.Find(TaskbarRestComponent.DragHandle), "手柄不能压缩文字或增加内容长度。");
                        Assert.AreEqual(Visibility.Visible, ((FrameworkElement)control.FindName("TaskbarPreviousButton")).Visibility);
                        Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)control.FindName("TaskbarNextButton")).Visibility);
                        var actions = (StackPanel)control.FindName("TaskbarHoverActions");
                        Assert.AreEqual("TaskbarPreviousButton", ((FrameworkElement)actions.Children[1]).Name);
                        var next = (FrameworkElement)control.FindName("ResidentNextSurface");
                        var play = (FrameworkElement)control.FindName("ResidentPlayPauseSurface");
                        var previousButton = (Button)control.FindName("ResidentPreviousButton");
                        Assert.AreEqual(Visibility.Visible, next.Visibility);
                        Assert.AreEqual(Visibility.Visible, play.Visibility);
                        Assert.IsFalse(((Button)control.FindName("ResidentNextButton")).IsEnabled);
                        Assert.IsTrue(previousButton.IsEnabled);
                        Assert.IsTrue(((Button)control.FindName("ResidentPlayPauseButton")).IsEnabled);
                        var nextPoint = next.TranslatePoint(new Point(), control);
                        var playPoint = play.TranslatePoint(new Point(), control);
                        if (vertical) Assert.IsTrue(nextPoint.Y + next.ActualHeight <= playPoint.Y);
                        else Assert.IsTrue(arrangement == TaskbarContentArrangement.Left
                            ? nextPoint.X + next.ActualWidth <= playPoint.X
                            : playPoint.X + play.ActualWidth <= nextPoint.X);
                        Assert.AreNotSame(previousButton, control.FindName("TaskbarPreviousButton"));
                    }
            // 独立 Popup 的残留会截住桌面输入，使用真实 HWND 验证挂起和隐藏立即关闭。
            var host = new Window
            {
                Content = control,
                Width = 300,
                Height = 44,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None
            };
            try
            {
                control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100, 44);
                control.ApplyPrimaryLength(300);
                control.Width = 300;
                control.Height = 44;
                host.Show();
                host.UpdateLayout();
                control.SetBarDragActive(true);
                var popup = (Popup)MediaBarTestAccess.GetField("_barMovePopup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
                var timer = (System.Windows.Threading.DispatcherTimer)MediaBarTestAccess.GetField("_barMoveTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
                Assert.IsTrue(popup.IsOpen);
                Assert.IsTrue(timer.IsEnabled);
                control.ApplyHostVisibilitySuspension(true);
                Assert.IsFalse(popup.IsOpen, "宿主挂起不能留下可命中的浮层。");
                Assert.IsFalse(timer.IsEnabled);
                control.ApplyHostVisibilitySuspension(false);
                control.SetBarDragActive(true);
                Assert.IsTrue(popup.IsOpen);
                host.Hide();
                Assert.IsFalse(popup.IsOpen, "宿主隐藏必须立即关闭手柄。");
                Assert.IsFalse(timer.IsEnabled);
            }
            finally { host.Close(); host.Content = null; }
            control.UpdateSongInfo(MediaSnapshot.Disconnected);
            control.ApplyTaskbarExperienceSettings();
            Assert.IsTrue(control.IsRestLayerEmpty, "全关闭的无媒体布局必须隐藏媒体栏。");
            var hidden = (Border)MediaBarTestAccess.GetField("_barMoveHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
            Assert.AreEqual(Visibility.Collapsed, hidden.Visibility);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)control.FindName("ResidentPlayPauseSurface")).Visibility);
        }
        finally { SettingsManager.Replace(previous); }
    }
}
