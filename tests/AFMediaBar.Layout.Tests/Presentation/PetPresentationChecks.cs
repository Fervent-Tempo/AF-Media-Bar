// 在已有隔离 WPF 进程中验证控件的真实加载、隐藏和重挂，不启动业务服务或鼠标 Hook。
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class PetPresentationChecks
{
    internal static void Verify()
    {
        var previous = SettingsManager.Current;
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
            control.BindPetInput(input);
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

            SettingsManager.Current.PetComponent = new() { SizeDip = 96, AllowOverflow = true };
            control.ApplyTaskbarExperienceSettings();
            control.ApplyPrimaryLength(500);
            DrainLayout(window);
            var overflow = window.OwnedWindows.Cast<Window>().Single();
            Assert.IsTrue(overflow.IsVisible);
            Assert.IsFalse(overflow.ShowActivated);
            Assert.AreEqual(96d, overflow.Width);
            Assert.IsTrue(overflow.Height > surface.ActualHeight, "越界窗口必须独立于媒体栏厚度。");

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
            Assert.IsFalse(input.IsSampling);
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
            control.BindPetInput(input);
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
}
