// 在隔离进程验证设置页资源加载、写入边界与异步清理；不启动真实播放器或音频服务。
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using AFMediaBar.Resources;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Components;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Media.Smtc;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Views.Windows;
using System.Windows.Controls.Primitives;
using AFMediaBar.ViewModels.Pages;
using AFMediaBar.Views.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;
using TextBox = System.Windows.Controls.TextBox;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsVisualRuntimeTests
{
    [TestMethod]
    public Task SettingsPagesLoadAndNumericDraftsDoNotOverwriteSettings() =>
        RunIsolatedAsync(nameof(SettingsPagesLoadAndNumericDraftsDoNotOverwriteSettings), app =>
        {
            VerifyNumericBinding(app);
            VerifyPageMarkup(app);
            VerifyCategoryAndPreviewRuntime();
            VerifyGeneratedRowSearch();
            VerifyMetricStylePicker();
            VerifyFullPanelMinimumSize();
            _ = new ExperimentalPage(null!);
            DeveloperToolsMarkupChecks.Verify(app, FindRepository());
        });

    [TestMethod]
    public Task QuickLaunchDoubleClickAndSuspensionCancelPendingActions() =>
        RunIsolatedAsync(nameof(QuickLaunchDoubleClickAndSuspensionCancelPendingActions), _ => QuickLaunchVisualChecks.Verify());

    [TestMethod]
    public Task BackdropRefreshCoalescesAndClosingOrDisposalRejectsQueuedWork() =>
        RunIsolatedAsync(nameof(BackdropRefreshCoalescesAndClosingOrDisposalRejectsQueuedWork), WindowBackdropRefreshChecks.VerifyLiveSelection);

    [TestMethod]
    public Task OutsideClickDismissesInactiveWindowAndKeepsOwnedPopupClicks() =>
        RunIsolatedAsync(nameof(OutsideClickDismissesInactiveWindowAndKeepsOwnedPopupClicks), app =>
        {
            // 不安装全局 Hook 或注入真实鼠标；使用实际 HWND 检查按下快照及其 UI 投递顺序。
            using var input = new NativeMouseInputMonitor(null!);
            var work = SystemParameters.WorkArea;
            var surface = new Border { Background = Brushes.DodgerBlue };
            var window = new Window { Content = surface, Left = work.Left + 80, Top = work.Top + 80,
                Width = 180, Height = 100, ShowInTaskbar = false, ShowActivated = false, Topmost = true };
            var popup = new Popup { PlacementTarget = surface, Placement = PlacementMode.Bottom, StaysOpen = true,
                Child = new Border { Width = 100, Height = 40, Background = Brushes.DeepPink } };
            var dismissals = 0;
            using var dismissal = new WindowOutsideClickDismissal(window, input, () => dismissals++);
            dismissal.RegisterPopup(popup);
            void Pump() => app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            void Press(Point point) => input.PublishButtonPress((int)point.X, (int)point.Y);
            try
            {
                window.Show();
                Pump();
                Assert.IsFalse(window.IsActive, "The reproduction must start without activating the panel.");
                var inside = surface.PointToScreen(new Point(20, 20));
                var outside = new Point((work.Right - 5) * VisualTreeHelper.GetDpi(window).DpiScaleX,
                    (work.Top + 5) * VisualTreeHelper.GetDpi(window).DpiScaleY);
                Press(outside);
                dismissal.Start();
                Pump();
                Assert.AreEqual(0, dismissals, "The click that preceded presentation must not close the new panel.");
                Press(inside);
                Pump();
                Assert.AreEqual(0, dismissals);

                popup.IsOpen = true;
                Pump();
                var popupPoint = popup.Child.PointToScreen(new Point(20, 20));
                Press(popupPoint);
                popup.IsOpen = false;
                Pump();
                Assert.AreEqual(0, dismissals, "Selecting a Popup item may close its HWND before the global press reaches the UI.");

                Press(outside);
                dismissal.Stop();
                dismissal.Start();
                Pump();
                Assert.AreEqual(0, dismissals, "A late callback from the previous visible session must be rejected.");
                popup.IsOpen = true;
                Pump();
                Press(outside);
                Pump();
                Assert.AreEqual(1, dismissals, "An external click must dismiss a window that never became active.");
                popup.IsOpen = false;
                dismissal.Start();
                Press(outside);
                window.Close();
                Pump();
                Assert.AreEqual(1, dismissals, "Closing must unsubscribe and reject already queued clicks.");
            }
            finally { popup.IsOpen = false; window.Close(); }
        });

    [TestMethod]
    public Task DeferredPageActivationRejectsAnUnloadedVisit() =>
        RunIsolatedAsync(nameof(DeferredPageActivationRejectsAnUnloadedVisit), app =>
        {
            var page = new Border();
            var window = new Window { Content = page, Left = -32000, Top = -32000, Width = 1, Height = 1,
                ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                var oldVisit = SettingsPageActivation.WaitForPresentationAsync(page);
                Assert.IsFalse(oldVisit.IsCompleted, "Data activation must yield to rendering and input.");
                window.Content = null;
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                window.Content = page;
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                var currentVisit = SettingsPageActivation.WaitForPresentationAsync(page);
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsFalse(oldVisit.GetAwaiter().GetResult(), "Reloading a cached page must not revive the old visit.");
                Assert.IsTrue(currentVisit.GetAwaiter().GetResult());
                using var cancelled = new CancellationTokenSource();
                var closingVisit = SettingsPageActivation.WaitForPresentationAsync(page, cancelled.Token);
                cancelled.Cancel();
                window.Close();
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsFalse(closingVisit.GetAwaiter().GetResult());
            }
            finally { window.Close(); }
        });

    [TestMethod]
    public Task PreferredSourceRefreshKeepsOfflineSelectionWithoutRewritingIdentity() =>
        RunIsolatedAsync(nameof(PreferredSourceRefreshKeepsOfflineSelectionWithoutRewritingIdentity), _ => VerifyPreferredSourceIdentity());

    [TestMethod]
    public Task QuickLaunchStatusLineClearsItselfAfterTheCountdown() =>
        RunIsolatedAsync(nameof(QuickLaunchStatusLineClearsItselfAfterTheCountdown), _ => VerifyQuickLaunchStatusCountdown());

    private static async Task RunIsolatedAsync(string testName, Action<Application> verify)
    {
        // Application 的关闭状态属于进程，避免污染后续 STA 测试。
        if (Environment.GetEnvironmentVariable("AFMB_SETTINGS_VISUAL_TEST_CHILD") != "1")
        {
            await RunVisualCheckInSeparateProcessAsync(testName);
            return;
        }
        SettingsMotionTests.RunSta(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
                app.Resources.MergedDictionaries.Add(new ControlsDictionary());
                var markup = XDocument.Load(Path.Combine(FindRepository(), "src", "AFMediaBar", "App.xaml"));
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                var sources = markup.Descendants(presentation + "ResourceDictionary.MergedDictionaries").First()
                    .Elements(presentation + "ResourceDictionary").Select(element => (string)element.Attribute("Source")!).ToArray();
                foreach (var source in sources)
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
                app.Resources["AfAccentBrush"] = Brushes.DodgerBlue;
                app.Resources["AfOnAccentBrush"] = Brushes.White;
                app.Resources["AfAccentTintBrush"] = Brushes.LightBlue;
                verify(app);
            }
            finally { app.Shutdown(); }
        }, TimeSpan.FromSeconds(30));
    }

    private static async Task RunVisualCheckInSeparateProcessAsync(string testName)
    {
        var results = Path.Combine(Path.GetTempPath(), "AFMB-SettingsVisual-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(SettingsVisualRuntimeTests).Assembly.Location);
        start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(SettingsVisualRuntimeTests).FullName + "." + testName);
        start.ArgumentList.Add("/Logger:trx;LogFileName=settings-visual.trx");
        start.ArgumentList.Add("/ResultsDirectory:" + results);
        start.Environment["AFMB_SETTINGS_VISUAL_TEST_CHILD"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the isolated WPF check.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                await output;
                await errors;
                Assert.Fail("The isolated WPF check timed out.");
            }
            Assert.AreEqual(0, process.ExitCode, (await output) + (await errors));
            var report = XDocument.Load(Path.Combine(results, "settings-visual.trx"));
            var counters = report.Descendants().Single(element => element.Name.LocalName == "Counters");
            Assert.AreEqual("1", counters.Attribute("executed")?.Value, "The child must actually execute the visual check.");
            Assert.AreEqual("1", counters.Attribute("passed")?.Value);
        }
        finally { if (Directory.Exists(results)) Directory.Delete(results, recursive: true); }
    }

    private static void VerifyMetricStylePicker()
    {
        var picker = new MetricStylePicker();
        var combo = picker.Children.OfType<ComboBox>().Single();
        var preview = picker.Children.OfType<MetricPresenter>().Single();
        double? previousLeft = null;
        double? previousWidth = null;
        foreach (var style in Enum.GetValues<MetricVisualStyle>())
        {
            picker.VisualStyle = style;
            picker.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            picker.Arrange(new Rect(new Point(), picker.DesiredSize));
            picker.UpdateLayout();
            var left = combo.TranslatePoint(new Point(), picker).X;
            if (previousLeft is double expected) Assert.AreEqual(expected, left, 0.01, "切换样式不能移动选择框。");
            if (previousWidth is double width) Assert.AreEqual(width, picker.ActualWidth, 0.01);
            Assert.IsTrue(preview.TranslatePoint(new Point(), picker).X < left, "预览应位于选择框左侧。");
            previousLeft = left;
            previousWidth = picker.ActualWidth;
        }
    }

    private static void VerifyFullPanelMinimumSize()
    {
        var document = XDocument.Load(Path.Combine(FindRepository(), "src", "AFMediaBar", "Views", "Windows", "TaskbarFullPanelWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        document.Root!.Attribute(x + "Class")!.Remove();
        var events = new HashSet<string> { "Deactivated", "PreviewKeyDown", "MouseLeftButtonUp", "Click", "PreviewMouseLeftButtonDown", "PreviewMouseLeftButtonUp", "LostMouseCapture", "DropDownOpened", "SelectionChanged", "PreviewMouseWheel" };
        foreach (var element in document.Root.DescendantsAndSelf())
            foreach (var attribute in element.Attributes().ToArray())
                if (events.Contains(attribute.Name.LocalName)) attribute.Remove();
        var markup = Regex.Replace(document.ToString(), "clr-namespace:AFMediaBar[.A-Za-z]+", match => match.Value + ";assembly=AFMediaBar");
        var window = (Window)XamlReader.Parse(markup);
        try
        {
            window.ApplyTemplate();
            var minimumWidth = AFMediaBar.Classes.Services.Layout.FullPanelScalePolicy.ContentWidthDip * TaskbarFullPanelSettings.MinimumScalePercent / 100d
                + AFMediaBar.Classes.Services.Layout.FullPanelScalePolicy.ShadowInsetDip * 2;
            Assert.IsTrue(window.MinWidth <= minimumWidth, "隐式窗口样式的最小宽度不能撑大已缩小的面板。");
            Assert.AreEqual(0d, window.MinHeight, "内容高度应能随缩放和区段折叠收缩。");
            var section = (Border)window.FindName("PerformanceSection");
            var cards = ((Grid)section.Child).Children.OfType<MetricPresenter>().ToArray();
            Assert.AreEqual(5, cards.Length, "网速应合并成一张双向卡片。");
            foreach (var scale in new[] { .75, 1d, 2d })
                foreach (var style in Enum.GetValues<MetricVisualStyle>())
                {
                    section.LayoutTransform = new ScaleTransform(scale, scale);
                    foreach (var card in cards)
                    {
                        card.VisualStyle = style;
                        card.ApplySnapshot(new(100, 99, 88, 9999999, 1024 * 1024 * 1024d, 99999999999));
                    }
                    section.Measure(new Size(400 * scale, double.PositiveInfinity));
                    section.Arrange(new Rect(new Point(), section.DesiredSize)); section.UpdateLayout();
                    Assert.IsTrue(cards.All(card => card.ActualWidth > 0 && card.ActualHeight >= 60));
                    var network = cards.Single(card => card.Metric == MetricKind.SystemNetwork);
                    Assert.AreEqual(2, network.Charts.Count);
                    Assert.IsTrue(network.ToolTip.ToString()!.Contains("↑") && network.ToolTip.ToString()!.Contains("↓"));
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(section.ActualWidth * scale),
                        (int)Math.Ceiling(section.ActualHeight * scale), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(section);
                }
        }
        finally { window.Close(); }
    }

    private static void VerifyPageMarkup(Application app)
    {
        foreach (var key in Translations.Keys) app.Resources["Loc." + key] = Translations.Get(key);
        app.Resources["AfBooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
        app.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
        app.Resources["AppTextFontFamily"] = new FontFamily("Segoe UI");
        app.Resources["AppTextFontWeight"] = FontWeights.Normal;
        app.Resources["AppTextMediumFontWeight"] = FontWeights.Medium;
        app.Resources["AppTextStrongFontWeight"] = FontWeights.SemiBold;
        var root = FindRepository();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var eventNames = new HashSet<string> { "Loaded", "Unloaded", "Click", "SelectionChanged", "Checked", "Unchecked", "SizeChanged", "MouseLeftButtonDown", "KeyDown", "IsVisibleChanged", "PreviewKeyDown", "LostKeyboardFocus", "PreviewMouseWheel", "MouseEnter", "MouseLeave", "PreviewGotKeyboardFocus", "IsKeyboardFocusWithinChanged" };
        foreach (var name in new[] { "QuickStart", "DisplayModes", "ScreenAndPlacement", "Appearance", "ApplicationAppearance", "RestState", "Interaction", "Lyrics", "ComponentsSettings", "FullPanel", "WidgetsSettings", "Spectrum", "Performance", "BluetoothBattery", "ExtraFeatures", "TrackNotifications", "Application", "Configuration", "Diagnostics", "MemoryTools", "DeviceManagement", "Experimental", "ReleaseHighlights", "About", "SettingsCategory" })
        {
            var document = XDocument.Load(Path.Combine(root, "src", "AFMediaBar", "Views", "Pages", name + "Page.xaml"));
            document.Root!.Attribute(x + "Class")!.Remove();
            foreach (var element in document.Root.DescendantsAndSelf())
            {
                foreach (var attribute in element.Attributes().ToArray())
                {
                    if (eventNames.Contains(attribute.Name.LocalName)) attribute.Remove();
                }
            }
            // Business dependencies and event handlers are excluded: this checks markup, styles, and runtime literals.
            var markup = Regex.Replace(document.ToString(), "clr-namespace:AFMediaBar[.A-Za-z]+",
                match => match.Value + ";assembly=AFMediaBar");
            var page = (Page)XamlReader.Parse(markup);
            page.Measure(new Size(800, 550));
            page.ApplyTemplate();
            Assert.IsNotNull(page.Content, name + " page must load its content.");
            if (page.Content is SettingsPageLayout layout)
            {
                layout.ApplyTemplate();
                Assert.IsInstanceOfType<Panel>(layout.ScrollViewer?.Content,
                    name + " must expose its groups directly to the pinned group strip.");
                if (name == "QuickStart") VerifyQuickStartScrolling(page, layout);
            }
        }

        // 新对话框不在页面清单里，按同一规则单独解析：资源键或遗漏的事件属性只会在运行时才炸。
        VerifyPlayerSupportDialogMarkup(root, eventNames);
    }

    // 这些视觉树按配置在运行时创建，页面标记检查无法覆盖频谱形状、原生图标和分类卡片资源。
    private static void VerifyCategoryAndPreviewRuntime()
    {
        var previous = SettingsManager.Current;
        var window = new Window { Width = 480, Height = 600, Left = -32000, Top = -32000,
            ShowActivated = false, ShowInTaskbar = false };
        try
        {
            var category = new SettingsCategoryPage();
            foreach (var section in SettingsNavigationCatalog.Sections.Where(section => section.Pages.Count > 1))
            {
                category.ShowSection(section, SettingsMode.Taskbar);
                category.Measure(new Size(800, 550));
                var cards = ((Panel)((SettingsPageLayout)category.Content).Content).Children.OfType<SettingsNavigationButton>().ToArray();
                CollectionAssert.AreEqual(section.Pages.ToArray(), cards.Select(card => card.Destination).ToArray());
            }
            SettingsManager.Current = new AppSettings
            {
                TaskbarExperience = TaskbarExperienceSettings.Default with { ComponentLayouts = new()
                {
                    Resident = [new(TaskbarRestComponent.Artwork, true), new(TaskbarRestComponent.MediaText, true),
                        new(TaskbarRestComponent.Spectrum, true), new(TaskbarRestComponent.SystemMemory, true)],
                    Hover = [new(TaskbarRestComponent.OutputDevice, true), new(TaskbarRestComponent.InputDevice, true), new(TaskbarRestComponent.Volume, true)]
                } }
            };
            using var context = new SettingsPageContext();
            using var localization = new LocalizationService();
            using var model = new SettingsPreviewViewModel(new LegacySettingsConfiguration(context), localization);
            var scene = new SettingsPreviewScene { DataContext = model };
            window.Content = scene;
            window.Show();
            foreach (var orientation in new[] { LayoutOrientation.Horizontal, LayoutOrientation.Vertical })
                foreach (var style in Enum.GetValues<SpectrumStyle>())
                {
                    context.Initialize(SettingsContext.Initial with { Orientation = orientation });
                    SettingsManager.Current.SpectrumComponent = SettingsManager.Current.SpectrumComponent with { Style = style };
                    model.ShowIdle = !model.ShowIdle;
                    model.ShowIdle = false;
                    window.UpdateLayout();
                    Assert.IsNotNull(scene.Content);
                    Assert.IsTrue(scene.DesiredSize.Width > 0 && scene.DesiredSize.Height > 0);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(480, 600, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(scene);
                }
            foreach (var mode in new[] { SettingsPreviewMode.QuickControls, SettingsPreviewMode.FullPanel })
            {
                model.Mode = mode;
                window.UpdateLayout();
                Assert.IsNotNull(scene.Content);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(480, 600, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(scene);
            }
        }
        finally { window.Close(); SettingsManager.Current = previous; }
    }

    /// <summary>解析播放器支持对话框标记；内容与页面同样只保留纯标记、样式和运行时资源。</summary>
    private static void VerifyPlayerSupportDialogMarkup(string root, HashSet<string> eventNames)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(Path.Combine(root, "src", "AFMediaBar", "Views", "Pages", "PlayerSupportDialog.xaml"));
        document.Root!.Attribute(x + "Class")!.Remove();
        foreach (var element in document.Root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes().ToArray())
            {
                if (eventNames.Contains(attribute.Name.LocalName)) attribute.Remove();
            }
        }
        var markup = Regex.Replace(document.ToString(), "clr-namespace:AFMediaBar[.A-Za-z]+",
            match => match.Value + ";assembly=AFMediaBar");
        var control = (UserControl)XamlReader.Parse(markup);
        control.Measure(new Size(560, 480));
        control.ApplyTemplate();
        Assert.IsNotNull(control.Content, "播放器支持对话框必须能加载内容。");
    }

    private static void VerifyGeneratedRowSearch()
    {
        var list = new ItemsControl { ItemsSource = Enumerable.Range(0, 18).Select(index => new { Id = "dynamic:" + index, Title = "Entry " + index }).ToArray() };
        list.ItemTemplate = (DataTemplate)XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:components="clr-namespace:AFMediaBar.Components;assembly=AFMediaBar">
                <components:SettingsRow Title="{Binding Title}" OptionId="{Binding Id}" Height="64"><CheckBox/></components:SettingsRow>
            </DataTemplate>
            """);
        var group = new SettingsGroup { GroupId = "generated", Header = "Generated", Content = list };
        var body = new StackPanel(); body.Children.Add(group);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var strip = new SettingsGroupStrip { Target = scroll };
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(strip); Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var window = new Window { Content = grid, Width = 600, Height = 450, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
        void Pump()
        {
            var frame = new DispatcherFrame();
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        try
        {
            window.Show(); Pump(); scroll.ScrollToTop(); Pump();
            strip.RevealGroup("generated", "dynamic:17"); Pump();
            Assert.IsTrue(scroll.VerticalOffset > 0, "搜索必须定位模板生成的目标行，而不是退回分组顶部。");
        }
        finally { window.Close(); }
    }

    private static void VerifyQuickStartScrolling(Page page, SettingsPageLayout layout)
    {
        var navigation = new NavigationView { IsPaneOpen = false, Transition = Wpf.Ui.Animations.Transition.None };
        var window = new Window { Content = navigation, Width = 1200, Height = 420, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
        void Pump()
        {
            var frame = new DispatcherFrame();
            page.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        try
        {
            window.Show(); navigation.ReplaceContent(page); Pump();
            var scroll = layout.ScrollViewer!;
            Assert.IsTrue(scroll.ScrollableHeight > 0, "快速开始必须在导航宿主内获得有限视口。");
            scroll.ScrollToTop(); Pump();
            ((FrameworkElement)scroll.Content).RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
            Pump();
            Assert.IsTrue(scroll.VerticalOffset > 0, "内容空白处的滚轮应交给页面滚动。");
        }
        finally { window.Close(); }
    }

    private static void VerifyPreferredSourceIdentity()
    {
        var previous = SettingsManager.Current;
        using var builder = new MediaSnapshotBuilder(null!);
        var registry = new MediaSourceRegistry([]);
        var catalog = new MediaSessionCatalog();
        using var selection = new MediaSessionSelectionService(catalog, registry);
        using var media = new MediaSessionService(registry, catalog, selection, builder, [], null!,
            new MemoryPruneCoordinator(null!, null!, null!, []), null!);
        using var localization = new LocalizationService();
        using var quickLaunch = new QuickLaunchService();
        try
        {
            SettingsManager.Current = new AppSettings
            {
                MediaSelection = new(MediaSelectionMode.PreferredApplication, "offline-player")
            };
            var model = new ExtraFeaturesViewModel(registry, media, new NoMonitors(), localization, quickLaunch);
            Assert.IsTrue(model.PreferredSourceOptions.Any(option => option.SourceId == "offline-player"));
            Assert.AreEqual("offline-player", model.PreferredSourceId);
            typeof(MediaSessionService).GetField("_lastDiscoveredSources", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(media, new MediaSourceDescriptor[] { new("OFFLINE-PLAYER", "Player", null, null) });
            typeof(ExtraFeaturesViewModel).GetMethod("RefreshSources", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(model, null);
            Assert.AreEqual("OFFLINE-PLAYER", model.PreferredSourceId);
            Assert.AreEqual("offline-player", SettingsManager.Current.MediaSelection.PreferredSourceId,
                "候选来源刷新不能重写用户保存的来源身份。");
        }
        finally { SettingsManager.Current = previous; }
    }

    /// <summary>
    /// 快速启动状态行是瞬态反馈：写上去之后必须在倒计时结束时自行清空，不能一直挂在那儿。
    /// The quick-launch status line is transient feedback: it must clear itself once the countdown elapses
    /// instead of staying there for good.
    /// </summary>
    private static void VerifyQuickLaunchStatusCountdown()
    {
        var previous = SettingsManager.Current;
        using var builder = new MediaSnapshotBuilder(null!);
        var registry = new MediaSourceRegistry([]);
        var catalog = new MediaSessionCatalog();
        using var selection = new MediaSessionSelectionService(catalog, registry);
        using var media = new MediaSessionService(registry, catalog, selection, builder, [], null!,
            new MemoryPruneCoordinator(null!, null!, null!, []), null!);
        using var localization = new LocalizationService();
        using var quickLaunch = new QuickLaunchService();
        try
        {
            SettingsManager.Current = new AppSettings();
            var model = new ExtraFeaturesViewModel(registry, media, new NoMonitors(), localization, quickLaunch);
            // 走真实入口写状态行：AddDetectedSource → AddQuickLaunch → SetStatus（“已添加到快速启动”）。
            // Write the status line through the real entry: AddDetectedSource -> AddQuickLaunch -> SetStatus ("added to quick launch").
            typeof(ExtraFeaturesViewModel).GetMethod("AddDetectedSource", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, [new MediaSourceSettingItem(
                    new MediaSourceDescriptor("test-player", "Test Player", QuickLaunchTargetKind.Executable, "C:\\test-player.exe"), false)]);
            Assert.IsFalse(string.IsNullOrEmpty(model.StatusText), "添加之后状态行必须立即显示反馈。");
            // 缩短倒计时并驱动调度器到计时器触发：状态行必须自行清空。
            // Shorten the countdown and pump the dispatcher until the timer fires: the line must clear itself.
            var timer = (DispatcherTimer)typeof(ExtraFeaturesViewModel).GetField("_statusTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(model)!;
            timer.Interval = TimeSpan.FromMilliseconds(20);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (model.StatusText.Length > 0 && DateTime.UtcNow < deadline)
                Pump(TimeSpan.FromMilliseconds(20));
            Assert.AreEqual(string.Empty, model.StatusText, "状态行必须在倒计时结束后自动清空。");
            Assert.IsFalse(timer.IsEnabled, "清空之后计时器不能继续空跑。");
        }
        finally { SettingsManager.Current = previous; }
    }

    /// <summary>驱动调度器运行一段时间，让排队中的定时器与回调跑完。/ Runs the dispatcher for a while so queued timers and callbacks get to run.</summary>
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    private sealed class NoMonitors : IDisplayMonitorService
    {
        public event EventHandler? MonitorsChanged { add { } remove { } }
        public IReadOnlyList<DisplayMonitorInfo> GetMonitors() => [];
        public void Refresh() { }
        public DisplayMonitorInfo? ResolveFixedMonitor(string? deviceId) => null;
        public DisplayMonitorInfo? ResolveNotificationMonitor(NotificationTargetMode mode, string? fixedDeviceId) => null;
        public bool IsForegroundWindowFullscreen() => false;
    }

    private static string FindRepository()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "AFMediaBar"))) root = root.Parent;
        return root?.FullName ?? throw new DirectoryNotFoundException("Settings markup was not found.");
    }

    private static void VerifyNumericBinding(Application app)
    {
        var fixture = new NumericFixture();
        var editor = new SettingsNumericEditor
        {
            Style = (Style)app.Resources[typeof(SettingsNumericEditor)],
            Minimum = 129.25d,
            Maximum = 4096d,
            Step = 1d,
            IsSnapToTickEnabled = false
        };
        BindingOperations.SetBinding(editor, SettingsNumericEditor.ValueProperty,
            new Binding(nameof(NumericFixture.Value)) { Source = fixture, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        editor.ApplyTemplate();
        var input = (TextBox)editor.Template.FindName("PART_Input", editor);
        Assert.AreEqual(0, fixture.Writes, "Applying a template must not write a coerced slider default.");
        input.Text = "200";
        Assert.AreEqual(300d, fixture.Value, "An unfinished draft must not change settings.");
        Assert.IsTrue(editor.TryCommit());
        Assert.AreEqual(200d, fixture.Value);
        Assert.AreEqual(1, fixture.Writes);
        Assert.AreEqual("200", input.Text);
        input.Text = "NaN";
        Assert.IsFalse(editor.TryCommit());
        Assert.IsTrue(editor.HasInputError);
        Assert.AreEqual(200d, fixture.Value);
        Assert.AreEqual(1, fixture.Writes);
        input.Text = "200";
        editor.Maximum = 180d;
        Assert.IsFalse(editor.TryCommit(), "A stale draft must use the latest taskbar limit.");
        Assert.AreEqual(1, fixture.Writes);
        fixture.Value = 160d;
        Assert.AreEqual(160d, editor.Value);
        Assert.AreEqual("160", input.Text, "External resets refresh the displayed value.");
        Assert.IsFalse(editor.HasInputError, "An external reset also clears stale validation feedback.");

        var percentEditor = new SettingsNumericEditor
        {
            Style = (Style)app.Resources[typeof(SettingsNumericEditor)],
            Minimum = 50d,
            Maximum = 95d,
            Step = 1d
        };
        BindingOperations.SetBinding(percentEditor, SettingsNumericEditor.ValueProperty,
            new Binding(nameof(NumericFixture.Percent)) { Source = fixture, Mode = BindingMode.TwoWay });
        percentEditor.ApplyTemplate();
        var percentInput = (TextBox)percentEditor.Template.FindName("PART_Input", percentEditor);
        Assert.AreEqual(75, fixture.Percent, "Loading an integer percentage must not write a slider default.");
        percentInput.Text = "NaN";
        Assert.IsFalse(percentEditor.TryCommit());
        Assert.AreEqual(75, fixture.Percent, "Invalid percentage input must preserve the saved integer.");
        percentInput.Text = "90";
        Assert.IsTrue(percentEditor.TryCommit());
        Assert.AreEqual(90, fixture.Percent, "A committed percentage must update the integer binding.");
    }

    private sealed class NumericFixture : INotifyPropertyChanged
    {
        private double _value = 300d;
        public int Percent { get; set; } = 75;
        public int Writes { get; private set; }
        public double Value
        {
            get => _value;
            set { _value = value; Writes++; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [TestMethod]
    public void SettingsSymbolsAndDiagramKindsAreValidRuntimeLiterals()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "AFMediaBar"))) root = root.Parent;
        Assert.IsNotNull(root);
        var sources = Directory.EnumerateFiles(Path.Combine(root.FullName, "src", "AFMediaBar", "Views"), "*.xaml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root.FullName, "src", "AFMediaBar", "Resources"), "Settings*.xaml"));
        var symbolType = typeof(SymbolIcon).GetProperty(nameof(SymbolIcon.Symbol))!.PropertyType;
        foreach (var file in sources)
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, "Symbol=\"([A-Za-z0-9]+)\"|\\{ui:SymbolIcon ([A-Za-z0-9]+)\\}"))
            {
                var literal = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                Assert.IsTrue(Enum.IsDefined(symbolType, Enum.Parse(symbolType, literal)), file + ": " + literal);
            }
            foreach (Match match in Regex.Matches(source, "<components:SettingsDiagram[^>]*Kind=\"([A-Za-z0-9]+)\""))
                Assert.IsTrue(Enum.IsDefined(Enum.Parse<SettingsDiagramKind>(match.Groups[1].Value)), file);
        }
    }
}
