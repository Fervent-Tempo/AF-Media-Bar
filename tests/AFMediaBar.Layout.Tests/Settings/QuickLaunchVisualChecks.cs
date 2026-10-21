// 复用隔离 WPF 测试的资源，验证真实单击计时器和图层清理；不发送系统输入或读取 Shell 图标。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止双击同时打开列表、挂起后迟到操作及动画残留。</summary>
internal static class QuickLaunchVisualChecks
{
    internal static void Verify()
    {
        var previous = SettingsManager.Current;
        TaskBarMediaControl? control = null;
        Window? window = null;
        try
        {
            var a = new QuickLaunchEntry("a", "A", QuickLaunchTargetKind.Executable, "a.exe");
            var b = new QuickLaunchEntry("b", "B", QuickLaunchTargetKind.Executable, "b.exe");
            SettingsManager.Current = new AppSettings
            {
                LyricsEnabled = false,
                QuickLaunch = new([a, b]) { InteractionMode = QuickLaunchInteractionMode.SelectThenLaunch }
            };
            using var selection = new QuickLaunchService();
            control = new TaskBarMediaControl();
            control.BindQuickLaunch(selection, () => { }, null);
            window = new Window { Content = control, Width = 150, Height = 70, Left = -16000,
                ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            window.Show();
            var menus = 0;
            var launches = new List<string>();
            control.QuickLaunchMenuRequested += (_, _) => menus++;
            control.QuickLaunchDirectRequested += entry => launches.Add(entry.Id);
            var tick = Environment.TickCount64;
            control.CompleteQuickLaunchClick(1, tick);
            control.CompleteQuickLaunchClick(2, tick);
            Assert.AreEqual(0, menus);
            CollectionAssert.AreEqual(new[] { "a" }, launches);
            Pump(TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime() + 100));
            Assert.AreEqual(0, menus, "双击之后不能迟到弹出单击菜单。");

            selection.MoveSelection(-120);
            Assert.AreEqual(1, launches.Count, "切换目标本身不能启动程序。");
            control.CompleteQuickLaunchClick(2, Environment.TickCount64);
            CollectionAssert.AreEqual(new[] { "a", "b" }, launches);
            control.CompleteQuickLaunchClick(1, Environment.TickCount64);
            var request = control.QuickLaunchRequestToken;
            control.ApplyHostVisibilitySuspension(true);
            Assert.IsTrue(request.IsCancellationRequested);
            Pump(TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime() + 100));
            Assert.AreEqual(0, menus, "挂起必须取消待执行的单击。");
            control.ApplyHostVisibilitySuspension(false);

            control.CompleteQuickLaunchClick(1, Environment.TickCount64);
            Pump(TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime() + 100));
            Assert.AreEqual(1, menus);
            SettingsManager.Current.QuickLaunch = selection.Settings with { InteractionMode = QuickLaunchInteractionMode.ListLaunch };
            control.CompleteQuickLaunchClick(1, Environment.TickCount64);
            Assert.AreEqual(2, menus, "旧模式单击应立即打开列表。");

            var first = new DrawingImage(new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
            var second = new DrawingImage(new GeometryDrawing(Brushes.Blue, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
            control.ShowQuickLaunchIcon(first, 0);
            control.ShowQuickLaunchIcon(second, 120);
            control.ShowQuickLaunchIcon(first, -120);
            var incoming = (Image)control.FindName("QuickLaunchIconIncoming");
            Assert.AreSame(first, incoming.Source, "连续反向切换应只保留最新目标。");
            var viewport = (Grid)control.FindName("QuickLaunchIconViewport");
            Assert.AreEqual(2, viewport.Children.Count);
            control.SuspendQuickLaunchPresentation();
            Assert.IsNull(incoming.Source);
            Assert.IsNull(((Image)control.FindName("QuickLaunchIconOutgoing")).Source);
            Assert.AreEqual(0d, viewport.Opacity);
            Assert.AreEqual(1d, ((UIElement)control.FindName("SongImagePlaceholder")).Opacity);
            Assert.AreEqual(1d, ((ScaleTransform)control.FindName("QuickLaunchPressScale")).ScaleX);
        }
        finally
        {
            control?.DisposePresentation();
            window?.Close();
            SettingsManager.Current = previous;
        }
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }
}
