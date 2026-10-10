// 复用已隔离的 WPF 测试进程验证真实媒体控件；窗口和动画仅由本检查创建并释放。
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class MetadataMarqueePresentationChecks
{
    internal static void Verify()
    {
        SettingsManager.Current.LyricsEnabled = false;
        SettingsManager.Current.TaskbarExperience = TaskbarExperienceSettings.Default with
        {
            LengthMode = TaskbarLengthMode.FollowContent,
            ContentLayout = TaskbarContentLayout.AdaptiveStack,
            SpectrumVisible = false, PerformanceVisible = false, OutputDeviceVisible = false, VolumeVisible = false
        };
        var control = new TaskBarMediaControl { AllowRestConnectionTransition = false };
        control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);
        var snapshot = MediaSnapshot.Disconnected with
        {
            IsConnected = true, Title = new string('W', 120) + " 中文 👩🏽‍💻 END", Artist = new string('A', 90)
        };
        control.UpdateSongInfo(snapshot);
        control.ApplyPrimaryLength(340);
        var window = new Window
        {
            Content = control, Width = 340, Height = 80, Left = -10000, Top = -10000,
            Opacity = 0, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None
        };
        var presenters = (List<MetadataMarqueePresenter>)typeof(TaskBarMediaControl)
            .GetField("_marqueeTexts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        try
        {
            window.Show(); window.UpdateLayout();
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(40));
            control.ApplyPrimaryLength(340);
            var viewportWidth = ((FrameworkElement)control.FindName("SongInfoStackPanel")).Width;
            control.ApplyMarqueeLayout(viewportWidth);
            var title = (TextBlock)control.FindName("SongTitle");
            var titleDuplicate = (TextBlock)control.FindName("SongTitleDuplicate");
            Assert.IsFalse(titleDuplicate.IsHitTestVisible);
            Assert.AreEqual(snapshot.Title, title.Text);
            Assert.AreEqual(title.FontFamily, titleDuplicate.FontFamily);
            Assert.AreEqual(title.FontSize, titleDuplicate.FontSize);
            if (MotionPolicy.ResolveCurrent().UseContinuousMotion)
            {
                Assert.IsTrue(presenters.All(presenter => presenter.Clock is not null));
                var hover = (Grid)control.FindName("HoverRevealHost");
                hover.Visibility = Visibility.Visible;
                control.ApplyMarqueeLayout(viewportWidth);
                Assert.IsTrue(presenters.All(presenter => presenter.IsPaused));
                hover.Visibility = Visibility.Collapsed;
                control.ApplyMarqueeLayout(viewportWidth);
                Assert.IsTrue(presenters.All(presenter => !presenter.IsPaused));
                var clock = presenters[0].Clock;
                control.ApplyBackgroundPruneLevel(MemoryPruneLevel.DisplayOff);
                Assert.IsTrue(presenters[0].IsPaused);
                control.ApplyBackgroundPruneLevel(MemoryPruneLevel.None);
                Assert.AreSame(clock, presenters[0].Clock);
                Assert.IsFalse(presenters[0].IsPaused);
            }
            SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
            { ContentLayout = TaskbarContentLayout.CompactInline };
            control.ApplyTaskbarExperienceSettings();
            control.ApplyMarqueeLayout(240);
            Assert.IsNull(presenters[1].Clock, "The hidden artist row must not own a clock.");
            Assert.AreEqual(Visibility.Collapsed, presenters[1].Duplicate.Visibility);
            Assert.AreEqual(snapshot.Title + " · " + snapshot.Artist, title.Text);
            window.Close();
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(40));
            Assert.IsTrue(presenters.All(presenter => presenter.Clock is null));
        }
        finally
        {
            window.Close();
            control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
    }
}
