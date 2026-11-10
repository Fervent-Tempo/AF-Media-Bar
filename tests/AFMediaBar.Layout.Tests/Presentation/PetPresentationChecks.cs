// 在已有隔离 WPF 进程中验证控件的真实加载、隐藏和重挂，不启动业务服务或鼠标 Hook。
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using static AFMediaBar.Classes.Interop.NativeMethods;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;

namespace AFMediaBar.Layout.Tests;

internal static class PetPresentationChecks
{
    internal static void Verify()
    {
        var previous = SettingsManager.Current;
        using var zOrder = new DesktopWidgetZOrderService();
        using var input = new PetInputService(Dispatcher.CurrentDispatcher, () => new Point(-900, 300));
        var window = new Window
        {
            Width = 500,
            Height = 44,
            Left = -20000,
            Top = -20000,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None
        };
        TaskBarMediaControl? control = null;
        try
        {
            SettingsManager.Current = new AppSettings
            {
                LyricsEnabled = false,
                TaskbarExperience = TaskbarExperienceSettings.Default with
                {
                    PetVisible = true,
                    ArtworkVisible = false,
                    SpectrumVisible = false,
                    PerformanceVisible = false,
                    HoverLayerEnabled = false
                }
            };
            control = new TaskBarMediaControl { AllowRestConnectionTransition = false };
            control.BindPetInput(input, zOrder: zOrder);
            control.UpdateSongInfo(MediaSnapshot.Disconnected with { IsConnected = true, Title = "Pet test" });
            control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100, 44);
            control.ApplyPrimaryLength(500);
            window.Content = control;
            window.Show();
            DrainLayout(window);
            Assert.IsTrue(input.IsSampling);
            var surface = (FrameworkElement)control.FindName("TaskbarPetSurface");
            Assert.IsTrue(control.IsPetComponentClick(surface));
            Assert.IsTrue(surface.IsHitTestVisible);

            var inline = (FrameworkElement)control.FindName("TaskbarPet");
            SettingsManager.Current.PetComponent = new() { SizeDip = 20, AllowOverflow = false };
            control.ApplyTaskbarExperienceSettings();
            DrainLayout(window);
            var smallWidth = inline.ActualWidth;
            SettingsManager.Current.PetComponent = new() { SizeDip = 120, AllowOverflow = false };
            control.ApplyTaskbarExperienceSettings();
            DrainLayout(window);
            Assert.IsTrue(inline.ActualWidth > smallWidth, "增大设置不能被上一次槽位尺寸卡住。");
            Assert.IsTrue(inline.ActualHeight <= surface.ActualHeight - 2, $"栏内宠物必须留出实际内边距。inline={inline.ActualWidth}x{inline.ActualHeight}, surface={surface.ActualWidth}x{surface.ActualHeight}");

            SettingsManager.Current.PetComponent = new() { SizeDip = 96, AllowOverflow = true };
            control.ApplyTaskbarExperienceSettings();
            control.ApplyPrimaryLength(500);
            DrainLayout(window);
            var overflow = window.OwnedWindows.Cast<Window>().Single();
            Assert.IsTrue(overflow.IsVisible);
            Assert.IsFalse(overflow.ShowActivated);
            Assert.AreEqual(96d, overflow.Width);
            Assert.IsTrue(overflow.Height > surface.ActualHeight, "越界窗口必须独立于媒体栏厚度。");

            // 菜单曾漏接跨 HWND 关闭，气泡曾因悬停无限延时；在真实 WPF 弹层上回归。
            var openMenu = typeof(TaskBarMediaControl).GetMethod("OpenPetMenuAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var menuField = typeof(TaskBarMediaControl).GetField("_petMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _ = (System.Threading.Tasks.Task)openMenu.Invoke(control, [surface, true])!;
            DrainLayout(window);
            var menu = (ContextMenu)menuField.GetValue(control)!;
            Assert.IsTrue(menu.IsOpen);
            input.PublishPress(new Point(-30000, -30000), Stopwatch.GetTimestamp());
            Assert.IsFalse(menu.IsOpen, "跨窗口点击必须关闭菜单。");
            WaitUntil(() => menu.PlacementTarget is null);
            Assert.IsNull(menu.PlacementTarget, "关闭必须释放旧锚点。");
            _ = (System.Threading.Tasks.Task)openMenu.Invoke(control, [surface, true])!;
            menu = (ContextMenu)menuField.GetValue(control)!;
            control.ApplyHostVisibilitySuspension(true);
            Assert.IsFalse(menu.IsOpen, "隐藏宿主必须关闭菜单。");
            WaitUntil(() => menu.PlacementTarget is null);
            control.ApplyHostVisibilitySuspension(false);

            using (var bubble = new PetSpeechBubble())
            {
                Assert.IsTrue(bubble.Show(surface, "两秒后收起"));
                var bubbleSurface = (Border)typeof(PetSpeechBubble).GetField("_surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bubble)!;
                var bubbleTimer = (DispatcherTimer)typeof(PetSpeechBubble).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bubble)!;
                bubbleSurface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                Assert.IsTrue(bubbleTimer.IsEnabled, "普通台词悬停不能停止计时。");
                var frame = new DispatcherFrame();
                var deadlineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2300) };
                deadlineTimer.Tick += (_, _) => { deadlineTimer.Stop(); frame.Continue = false; };
                deadlineTimer.Start();
                Dispatcher.PushFrame(frame);
                Assert.IsFalse(bubble.IsOpen, "普通台词应在两秒后关闭。");
                Assert.IsTrue(bubble.ShowStatus(surface, "volume", "音量 36%"));
                Assert.AreEqual(TimeSpan.FromSeconds(2), bubbleTimer.Interval);
                bubble.Close();
                Assert.IsTrue(bubble.Show(surface, "查看详情", activation: () => { }));
                bubbleSurface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                Assert.IsTrue(bubbleTimer.IsEnabled, "详情气泡悬停也不能停止计时。");
            }

            overflow.Topmost = false;
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while ((GetWindowLong(new WindowInteropHelper(overflow).Handle, GWL_EXSTYLE) & 8) == 0 && DateTime.UtcNow < deadline)
            {
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(frame);
            }
            Assert.IsTrue((GetWindowLong(new WindowInteropHelper(overflow).Handle, GWL_EXSTYLE) & 8) != 0, "矩形不变时也必须恢复层级。");

            control.ApplyPlacementMotionSuspension(true);
            Assert.IsTrue(overflow.IsVisible, "拖动不能隐藏独立宠物或切回栏内。");
            Assert.AreEqual(Visibility.Hidden, inline.Visibility);
            control.ApplyPlacementMotionSuspension(false);
            Assert.AreSame(overflow, window.OwnedWindows.Cast<Window>().Single());
            control.ApplyHostVisibilitySuspension(true);
            Assert.IsFalse(input.IsSampling);
            Assert.IsFalse(overflow.IsVisible, "挂起不能留下越界宠物。");
            control.ApplyHostVisibilitySuspension(false);
            Assert.IsTrue(input.IsSampling);
            Assert.IsTrue(overflow.IsVisible);
            Assert.AreSame(overflow, window.OwnedWindows.Cast<Window>().Single(), "短时挂起应复用窗口。");
            control.ApplyBackgroundPruneLevel(MemoryPruneLevel.DisplayOff);
            Assert.IsFalse(input.IsSampling);
            control.ApplyBackgroundPruneLevel(MemoryPruneLevel.None);
            Assert.IsTrue(input.IsSampling);
            control.ApplyPlacementMotionSuspension(true);
            Assert.IsTrue(input.IsSampling, "拖动保持宠物输入与呈现租约。");
            control.ApplyPlacementMotionSuspension(false);
            Assert.IsTrue(input.IsSampling);

            window.Content = null;
            DrainLayout(window);
            Assert.IsFalse(input.IsSampling, "卸载应释放输入租约。");
            Assert.AreEqual(0, window.OwnedWindows.Count, "卸载必须销毁越界窗口。");
            window.Content = control;
            DrainLayout(window);
            Assert.IsTrue(input.IsSampling, "重新加载应取得新的输入租约。");
            Assert.AreEqual(1, window.OwnedWindows.Count);

            SettingsManager.Current.PetComponent = SettingsManager.Current.PetComponent with { AllowOverflow = false };
            control.ApplyTaskbarExperienceSettings();
            DrainLayout(window);
            Assert.AreEqual(0, window.OwnedWindows.Count, "关闭越界显示应移除独立窗口。");

            SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { PetVisible = false };
            control.ApplyTaskbarExperienceSettings();
            control.ApplyPrimaryLength(500);
            Assert.IsFalse(input.IsSampling);
            Assert.AreEqual(Visibility.Collapsed, surface.Visibility);
            Assert.IsFalse(surface.IsHitTestVisible);
            control.DisposePresentation();
            control.DisposePresentation();
            control.BindPetInput(input, zOrder: zOrder);
            Assert.IsFalse(input.IsSampling, "最终释放后不得重新订阅。");
        }
        finally
        {
            control?.DisposePresentation();
            window.Close();
            SettingsManager.Current = previous;
        }
    }

    private static void DrainLayout(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var deadline = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (condition() || deadline.ElapsedMilliseconds > 1000) { timer.Stop(); frame.Continue = false; } };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
