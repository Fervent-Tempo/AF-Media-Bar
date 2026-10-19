// 通过独立 WPF 窗口验证菜单不依附可隐藏的入口；不连接 Explorer、播放器或音频服务。
// 测试拥有窗口和 Application，退出时关闭 Popup 并结束 Dispatcher。
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Views.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>独立菜单窗口在入口隐藏后的交互与释放回归。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MenuLifetimeTests
{
    [TestMethod]
    public void CompactMenusRemainVisibleWhenAnchorHidesAndEscapeStillDismisses()
    {
        if (RunIsolated(nameof(CompactMenusRemainVisibleWhenAnchorHidesAndEscapeStillDismisses))) return;
        StaTest.Run(async dispatcher =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var icons = new AppIconService();
            using var appearance = new WindowAppearanceService(new NativeWindowBackdropAdapter(), icons);
            var host = new Window { Left = -32000, Top = -32000, Width = 320, Height = 44, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                LoadResources(app);
                foreach (var mode in Enum.GetValues<TaskbarCompactFlyoutMode>())
                {
                    host.Show();
                    using var menu = new TaskbarCompactFlyoutWindow(appearance)
                    {
                        Left = -32000, Top = -32200, Width = 320, ShowActivated = false, ShowInTaskbar = false, Topmost = false
                    };
                    // 只准备内容，不调用带屏幕锚点的 ShowMode，避免激活用户桌面。
                    typeof(TaskbarCompactFlyoutWindow).GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(menu, mode);
                    var panel = mode == TaskbarCompactFlyoutMode.Volume ? "VolumePanel" :
                        mode == TaskbarCompactFlyoutMode.QuickLaunch ? "QuickLaunchPanel" : "OutputDevicePanel";
                    ((FrameworkElement)menu.FindName(panel)).Visibility = Visibility.Visible;
                    menu.Show();
                    var location = new Point(menu.Left, menu.Top);
                    Assert.IsNull(menu.Owner, "独立菜单不能由会隐藏的任务栏窗口拥有。");
                    host.Top -= 22;
                    host.Hide();
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.IsTrue(menu.IsVisible, mode.ToString());
                    Assert.AreEqual(location, new Point(menu.Left, menu.Top));
                    var source = System.Windows.PresentationSource.FromVisual(menu);
                    var escape = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape)
                        { RoutedEvent = UIElement.PreviewKeyDownEvent };
                    var hidden = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
                    { if (!menu.IsVisible) hidden.TrySetResult(); }
                    menu.IsVisibleChanged += OnVisibleChanged;
                    try
                    {
                        menu.RaiseEvent(escape);
                        Assert.IsTrue(escape.Handled);
                        await hidden.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    }
                    finally { menu.IsVisibleChanged -= OnVisibleChanged; }
                    Assert.IsFalse(menu.IsVisible);
                    menu.Dispose(); menu.Dispose();
                    Assert.IsNull(System.Windows.PresentationSource.FromVisual(menu));
                }
            }
            finally { host.Close(); app.Shutdown(); }
        });
    }

    [TestMethod]
    public void ContextMenuUsesStableHiddenTargetAndSubmenusRemainInsideClickBoundary()
    {
        if (RunIsolated(nameof(ContextMenuUsesStableHiddenTargetAndSubmenusRemainInsideClickBoundary))) return;
        StaTest.Run(async dispatcher =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var stable = new Window { Left = -32000, Top = -32000, Width = 10, Height = 10, ShowActivated = false, ShowInTaskbar = false };
            var anchorHost = new Window { Left = -32100, Top = -32100, Width = 320, Height = 44, ShowActivated = false, ShowInTaskbar = false };
            // 透明、无捕获的测试菜单，只验证真实 Popup 的坐标和生命周期，不操纵桌面输入。
            var menu = new ContextMenu { Opacity = 0, StaysOpen = true };
            var parent = new MenuItem { Header = "Parent" };
            var child = new MenuItem { Header = "Child" };
            parent.Items.Add(child);
            menu.Items.Add(parent);
            try
            {
                LoadResources(app);
                stable.Show(); stable.Hide();
                anchorHost.Show();
                var screenPoint = anchorHost.PointToScreen(new Point(0, 0));
                ContextMenuHelper.OpenAtScreenAnchor(menu, stable, screenPoint);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreSame(stable, menu.PlacementTarget);
                Assert.AreEqual(PlacementMode.RelativePoint, menu.Placement);
                var roundTrip = stable.PointToScreen(new Point(menu.HorizontalOffset, menu.VerticalOffset));
                Assert.AreEqual(screenPoint.X, roundTrip.X, 1);
                Assert.AreEqual(screenPoint.Y, roundTrip.Y, 1);
                var position = menu.PointToScreen(new Point(0, 0));
                anchorHost.Top -= 20;
                anchorHost.Hide();
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsTrue(menu.IsOpen, "入口移动或隐藏不应关闭稳定宿主上的 Popup。");
                Assert.AreEqual(position, menu.PointToScreen(new Point(0, 0)));
                parent.IsSubmenuOpen = true;
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsTrue(child.IsVisible);
                var childSource = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(child);
                Assert.IsTrue(AFMediaBar.Classes.Interop.NativeMethods.GetWindowRect(childSource.Handle, out var childBounds));
                // 入场动画会把内容临时移到 HWND 外，物理窗口矩形才是实际可点击范围。
                var inside = new Point((childBounds.Left + childBounds.Right) / 2d, (childBounds.Top + childBounds.Bottom) / 2d);
                ContextMenuHelper.CloseIfOutside(menu, (int)inside.X, (int)inside.Y);
                Assert.IsTrue(menu.IsOpen, "子菜单属于整个菜单的内部区域。");
                ContextMenuHelper.CloseIfOutside(menu, int.MinValue, int.MinValue);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsFalse(menu.IsOpen);
                Assert.IsFalse(parent.IsSubmenuOpen);
                ContextMenuHelper.OpenAtScreenAnchor(menu, stable, null);
                Assert.AreEqual(PlacementMode.MousePoint, menu.Placement);
                Assert.AreEqual(0d, menu.HorizontalOffset);
                Assert.AreEqual(0d, menu.VerticalOffset);
            }
            catch (Exception exception) { Console.WriteLine(exception); throw; }
            finally
            {
                parent.IsSubmenuOpen = false;
                menu.IsOpen = false;
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                menu.PlacementTarget = null;
                anchorHost.Close(); stable.Close(); app.Shutdown();
            }
        });
    }

    private static void LoadResources(Application app)
    {
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        foreach (var resource in new[] { "MotionResources", "SettingsAppearanceResources" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative) });
    }

    private static bool RunIsolated(string name)
    {
        const string Variable = "AFMB_MENU_LIFETIME_ISOLATED";
        if (Environment.GetEnvironmentVariable(Variable) == "1") return false;
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.StandardOutputEncoding = System.Text.Encoding.UTF8;
        start.StandardErrorEncoding = System.Text.Encoding.UTF8;
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(MenuLifetimeTests).Assembly.Location);
        start.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(MenuLifetimeTests).FullName}.{name}");
        start.Environment[Variable] = "1";
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(60000)) { child.Kill(true); child.WaitForExit(); Assert.Fail("菜单生命周期验证超时。"); }
        Assert.AreEqual(0, child.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        return true;
    }
}
