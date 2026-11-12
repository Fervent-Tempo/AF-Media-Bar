// 验证顶层菜单窗口的显示、动效结束和关闭路径；不连接 Explorer、媒体或音频服务。
// Verifies the top-level menu window's presentation, animation completion, and dismissal paths without Explorer, media, or audio.
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Views.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WpfMenuItem = Wpf.Ui.Controls.MenuItem;

namespace AFMediaBar.Layout.Tests;

/// <summary>顶层菜单窗口的交互与释放回归。</summary>
[TestClass]
[TestCategory("Native")]
[DoNotParallelize]
public sealed class MenuWindowTests
{
    [TestMethod]
    public void ItemClickDismissesMenuWindow()
    {
        if (RunIsolated(nameof(ItemClickDismissesMenuWindow))) return;
        StaTest.Run(async dispatcher =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var icons = new AppIconService();
            var adapter = CreateAdapter();
            using var appearance = new WindowAppearanceService(adapter, icons);
            AFMenuWindow? window = null;
            try
            {
                LoadResources(app);
                var clicked = false;
                var panel = new StackPanel();
                var item = new WpfMenuItem { Header = "Item" };
                item.Click += (_, _) => clicked = true;
                panel.Children.Add(item);
                window = CreateWindow(appearance);
                var dismissed = TrackDismissal(window);
                window.ShowMenu(panel, new Point(20, 20));
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsTrue(window.IsVisible);

                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
                var reason = await dismissed.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.AreEqual(MenuDismissReason.ItemInvoked, reason);
                Assert.IsTrue(clicked);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsFalse(window.IsVisible);
            }
            finally { window?.Dispose(); app.Shutdown(); }
        });
    }

    [TestMethod]
    public void EscapeDismissesMenuWindow()
    {
        if (RunIsolated(nameof(EscapeDismissesMenuWindow))) return;
        StaTest.Run(async dispatcher =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var icons = new AppIconService();
            var adapter = CreateAdapter();
            using var appearance = new WindowAppearanceService(adapter, icons);
            AFMenuWindow? window = null;
            try
            {
                LoadResources(app);
                var panel = new StackPanel();
                panel.Children.Add(new WpfMenuItem { Header = "Item" });
                window = CreateWindow(appearance);
                var dismissed = TrackDismissal(window);
                window.ShowMenu(panel, new Point(20, 20));
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var source = PresentationSource.FromVisual(window);
                Assert.IsNotNull(source);
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                var reason = await dismissed.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.AreEqual(MenuDismissReason.Escape, reason);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsFalse(window.IsVisible);
            }
            finally { window?.Dispose(); app.Shutdown(); }
        });
    }

    [TestMethod]
    public void SubmenuItemClickDismissesMenuWindow()
    {
        if (RunIsolated(nameof(SubmenuItemClickDismissesMenuWindow))) return;
        StaTest.Run(async dispatcher =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var icons = new AppIconService();
            var adapter = CreateAdapter();
            using var appearance = new WindowAppearanceService(adapter, icons);
            AFMenuWindow? window = null;
            try
            {
                LoadResources(app);
                var panel = new StackPanel();
                var header = new WpfMenuItem { Header = "Parent" };
                var child = new WpfMenuItem { Header = "Child" };
                header.Items.Add(child);
                panel.Children.Add(header);
                window = CreateWindow(appearance);
                var dismissed = TrackDismissal(window);
                window.ShowMenu(panel, new Point(20, 20));
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                header.IsSubmenuOpen = true;
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsTrue(window.IsVisible, "Opening a submenu must keep the top-level menu visible.");
                Assert.IsTrue(header.IsSubmenuOpen, "The submenu must stay open.");
                Assert.IsTrue(child.IsVisible, "The submenu item must be visible.");

                child.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, child));
                var reason = await dismissed.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.AreEqual(MenuDismissReason.ItemInvoked, reason);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsFalse(window.IsVisible);
            }
            finally { window?.Dispose(); app.Shutdown(); }
        });
    }

    [TestMethod]
    public void SubmenuHeaderClickOpensSubmenu()
    {
        if (RunIsolated(nameof(SubmenuHeaderClickOpensSubmenu))) return;
        StaTest.Run(async dispatcher =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var icons = new AppIconService();
            var adapter = CreateAdapter();
            using var appearance = new WindowAppearanceService(adapter, icons);
            AFMenuWindow? window = null;
            try
            {
                LoadResources(app);
                var panel = new StackPanel();
                var header = new WpfMenuItem { Header = "Parent" };
                var child = new WpfMenuItem { Header = "Child" };
                header.Items.Add(child);
                panel.Children.Add(header);
                window = CreateWindow(appearance);
                window.ShowMenu(panel, new Point(20, 20));
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

                header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = Mouse.PreviewMouseUpEvent
                });
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsTrue(window.IsVisible, "Clicking a submenu header must keep the top-level menu visible.");
                Assert.IsTrue(header.IsSubmenuOpen, "Clicking a submenu header must open its submenu.");
                Assert.IsTrue(child.IsVisible, "The submenu item must be visible after the header click.");
            }
            finally { window?.Dispose(); app.Shutdown(); }
        });
    }

    [TestMethod]
    public void SubmenuHeaderHoverOpensSubmenu()
    {
        if (RunIsolated(nameof(SubmenuHeaderHoverOpensSubmenu))) return;
        StaTest.Run(async dispatcher =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var icons = new AppIconService();
            var adapter = CreateAdapter();
            using var appearance = new WindowAppearanceService(adapter, icons);
            AFMenuWindow? window = null;
            try
            {
                LoadResources(app);
                var panel = new StackPanel();
                var header = new WpfMenuItem { Header = "Parent" };
                var child = new WpfMenuItem { Header = "Child" };
                header.Items.Add(child);
                panel.Children.Add(header);
                window = CreateWindow(appearance);
                window.ShowMenu(panel, new Point(20, 20));
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

                // 合成的 MouseEnter 不更新 IsMouseOver；捕获把指针状态固定在该项，避免依赖真实鼠标位置。
                // Synthetic MouseEnter does not update IsMouseOver; capture holds the pointer state on the item so the test does not depend on the real cursor.
                Assert.IsTrue(Mouse.Capture(header),
                    "The submenu header must accept mouse capture for the hover check.");
                try
                {
                    header.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                    {
                        RoutedEvent = UIElement.MouseEnterEvent
                    });
                    await Task.Delay(350);
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.IsTrue(header.IsSubmenuOpen, "Hovering a submenu header must open its submenu.");
                    Assert.IsTrue(child.IsVisible, "The submenu item must be visible after hover.");
                }
                finally
                {
                    Mouse.Capture(null);
                }
            }
            finally { window?.Dispose(); app.Shutdown(); }
        });
    }

    private static NativeWindowBackdropAdapter CreateAdapter() => new(
        22621,
        (_, _, _) => true,
        (_, _) => true,
        (_, _, _) => true);

    private static AFMenuWindow CreateWindow(WindowAppearanceService appearance) => new(appearance)
    {
        Topmost = false,
        ShowActivated = false,
        ShowInTaskbar = false
    };

    private static TaskCompletionSource<MenuDismissReason> TrackDismissal(AFMenuWindow window)
    {
        var completion = new TaskCompletionSource<MenuDismissReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Dismissed += (_, args) => completion.TrySetResult(args.Reason);
        return completion;
    }

    private static void LoadResources(Application app)
    {
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        foreach (var resource in new[] { "MotionResources", "SettingsAppearanceResources" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative)
            });
        app.Resources["AppMenuBackgroundBrush"] = Brushes.DimGray;
    }

    private static bool RunIsolated(string name)
    {
        const string Variable = "AFMB_MENU_WINDOW_ISOLATED";
        if (Environment.GetEnvironmentVariable(Variable) == "1") return false;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.StandardOutputEncoding = System.Text.Encoding.UTF8;
        start.StandardErrorEncoding = System.Text.Encoding.UTF8;
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(MenuWindowTests).Assembly.Location);
        start.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(MenuWindowTests).FullName}.{name}");
        start.Environment[Variable] = "1";
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(60000))
        {
            child.Kill(true);
            child.WaitForExit();
            Assert.Fail("菜单窗口验证超时。");
        }
        Assert.AreEqual(0, child.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        return true;
    }
}
