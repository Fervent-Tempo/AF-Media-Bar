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
using AFMediaBar.Components;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Media.Smtc;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
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
        var eventNames = new HashSet<string> { "Loaded", "Unloaded", "Click", "SelectionChanged", "Checked", "Unchecked", "SizeChanged", "MouseLeftButtonDown", "KeyDown", "IsVisibleChanged", "PreviewKeyDown", "LostKeyboardFocus" };
        foreach (var name in new[] { "DisplayModes", "ScreenAndPlacement", "Appearance", "ApplicationAppearance", "Interaction", "Lyrics", "ComponentsSettings", "WidgetsSettings", "ExtraFeatures", "Application", "DeviceManagement", "Experimental", "ReleaseHighlights", "About" })
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
        }
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
