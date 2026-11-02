// 在已有隔离 WPF 进程中验证实际组件重挂后的绑定和轮换身份，不操作 Explorer。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class DetachedComponentPresentationChecks
{
    internal static void Verify()
    {
        var previous = SettingsManager.Current;
        var control = new TaskBarMediaControl();
        var owner = new Window { Left = -32000, Top = -32000, Width = 600, Height = 100, ShowActivated = false, Content = control };
        var components = new StackPanel();
        var detached = new Window { Left = -32000, Top = -32000, Width = 400, Height = 100, ShowActivated = false, Content = components };
        try
        {
            var settings = new AppSettings();
            settings.PerformanceComponent = settings.PerformanceComponent with { Metrics = [MetricKind.SystemCpu], DisplayMode = PerformanceDisplayMode.Cycle, OpenTaskManagerOnClick = true };
            settings.TaskbarExperience = ComponentLayoutPolicy.SetVisible(settings, BarLayoutState.Resident, TaskbarRestComponent.PlayPause, true);
            settings.TaskbarExperience = ComponentLayoutPolicy.SetVisible(settings, BarLayoutState.Resident, TaskbarRestComponent.Performance, true);
            var profile = MonitorBarProfilePolicy.Capture(settings, BarConfigurationTarget.Taskbar, "A") with
            { Detached = [new(TaskbarRestComponent.PlayPause, 100, 0), new(TaskbarRestComponent.Performance, 200, 0)] };
            MonitorBarProfilePolicy.Store(settings, profile);
            SettingsManager.Current = settings;
            settings = SettingsManager.Current;
            control.SetComponentScope(BarConfigurationTarget.Taskbar, "A");
            control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100, 44);
            var snapshot = MediaSnapshot.Disconnected with { IsConnected = true, Title = "Detached", IsPlaying = true, CanPlayPause = true };
            control.UpdateSongInfo(snapshot);
            control.ApplyPrimaryLength(600);
            foreach (var visual in control.DetachedVisuals) { components.Children.Add(visual.View); visual.IsPlaced = true; }
            owner.Show(); detached.Show(); owner.UpdateLayout(); detached.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var play = (Button)control.FindName("ResidentPlayPauseButton");
            var residentIcon = (Wpf.Ui.Controls.SymbolIcon)play.Content;
            var sourceIcon = (Wpf.Ui.Controls.SymbolIcon)control.FindName("TaskbarPlayPauseIcon");
            Assert.IsTrue(play.IsEnabled);
            Assert.AreEqual(sourceIcon.Symbol, residentIcon.Symbol, "跨窗口重挂后播放图标必须继续解析原名称作用域。");
            control.ComponentPlacementAllowed = () => true;
            TaskbarRestComponent? returnIntent = null;
            control.ComponentReturnRequested += component => returnIntent = component;
            var returnItem = (MenuItem)control.DetachedVisuals.Single(visual => visual.Component == TaskbarRestComponent.PlayPause).View.ContextMenu!.Items[0];
            returnItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.AreEqual(TaskbarRestComponent.PlayPause, returnIntent);
            Assert.AreEqual(2, MonitorBarProfilePolicy.Find(SettingsManager.Current, BarConfigurationTarget.Taskbar, "A")!.Detached.Count,
                "控件只发布放回意图，不直接改写设置。");
            var performance = control.DetachedVisuals.Single(visual => visual.Component == TaskbarRestComponent.Performance);
            Assert.IsTrue(performance.View.Background is SolidColorBrush { Color.A: > 0 },
                "独立组件空白处也必须有与媒体栏相同的非零 alpha 命中底。");
            Assert.IsNotNull(performance.View.TryFindResource("TaskbarHoverButtonOverBrush"), "重挂后必须保留组件悬停样式的资源作用域。");
            Assert.IsTrue(performance.SizeDip.Width > 0 && performance.SizeDip.Height > 0);
            Assert.IsTrue(performance.View.Children.Cast<FrameworkElement>().Any(element => element.Visibility == Visibility.Visible &&
                Equals(element.Tag, TaskbarRestComponent.Performance)));
            var taskManagerRequests = 0;
            control.OpenTaskManagerRequested += (_, _) => taskManagerRequests++;
            var metricSurface = performance.View.Children.Cast<FrameworkElement>().Single(element => element.Visibility == Visibility.Visible);
            metricSurface.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Assert.IsTrue(control.HasComponentGesture, "鼠标按下应到达独立容器的输入入口。");
            metricSurface.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseUpEvent });
            Assert.AreEqual(1, taskManagerRequests, "独立性能表面的短按必须保留任务管理器输入意图。");
            control.UpdateSongInfo(snapshot with { CanPlayPause = false, IsPlaying = false });
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert.IsFalse(play.IsEnabled, "独立按钮不能保留已失效的媒体能力。");
            Assert.AreEqual(sourceIcon.Symbol, residentIcon.Symbol);
            control.ApplyHostVisibilitySuspension(true);
            Assert.IsFalse(control.DetachedVisuals.Single(visual => visual.Component == TaskbarRestComponent.PlayPause).View.IsHitTestVisible);
            control.ApplyHostVisibilitySuspension(false);
            Assert.IsTrue(control.DetachedVisuals.Single(visual => visual.Component == TaskbarRestComponent.PlayPause).View.IsHitTestVisible,
                "恢复宿主后独立容器必须恢复输入。");
            MonitorBarProfilePolicy.Store(settings, profile with { Detached = [] });
            control.ApplyTaskbarExperienceSettings();
            owner.UpdateLayout();
            Assert.AreEqual(0, control.DetachedVisuals.Count);
            Assert.IsTrue(control.IsAncestorOf((FrameworkElement)control.FindName("ResidentPlayPauseSurface")));
            var interaction = (FrameworkElement)control.FindName("InteractionSurface");
            Assert.AreEqual(HorizontalAlignment.Left, metricSurface.HorizontalAlignment, "归还不能保留独立窗口的居中对齐。");
            Assert.AreEqual(metricSurface.Margin.Left, metricSurface.TransformToAncestor(interaction).Transform(new Point()).X, 0.1,
                "归还的指标必须在同一帧按本体的左起坐标排列。");
        }
        finally
        {
            components.Children.Clear(); control.ReleaseDetachedVisuals();
            control.DisposePresentation(); detached.Close(); owner.Close();
            SettingsManager.Current = previous;
        }
    }
}
