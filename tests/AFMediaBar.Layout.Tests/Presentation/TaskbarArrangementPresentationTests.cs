// 在独立测试进程中验证实际 WPF 控件和按钮几何，不启动任务栏宿主或业务服务。
// 测试拥有子进程；隔离 WPF Application 的进程级生命周期，超时后结束测试子进程。
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.Web.WebView2.Wpf;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证真实 WPF 控件的按钮锚点及重复布局稳定性。</summary>
[TestClass]
[DoNotParallelize]
public sealed class TaskbarArrangementPresentationTests
{
    [TestMethod]
    public void FractionalWidthRepeatedLayoutKeepsButtonsStableAndResetsDirection()
    {
        const string IsolationVariable = "AFMB_ALIGNMENT_PRESENTATION_ISOLATED";
        if (Environment.GetEnvironmentVariable(IsolationVariable) != "1")
        {
            // WPF allows one Application per process and its shutdown state survives this STA.
            // Running only this method in a child test host keeps other WPF regressions independent.
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("vstest");
            startInfo.ArgumentList.Add(typeof(TaskbarArrangementPresentationTests).Assembly.Location);
            startInfo.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(TaskbarArrangementPresentationTests).FullName}.{nameof(FractionalWidthRepeatedLayoutKeepsButtonsStableAndResetsDirection)}");
            startInfo.Environment[IsolationVariable] = "1";
            startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
            using var child = Process.Start(startInfo)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(60000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                Assert.Fail("Isolated WPF presentation test timed out");
            }
            Assert.AreEqual(0, child.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            return;
        }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var original = SettingsManager.Current.Clone();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                app.Resources["AfBooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var resource in new[] { "MotionResources", "SettingsAppearanceResources", "SettingsDiagrams", "SettingsComponentResources" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative)
                    });
                var control = new TaskBarMediaControl();
                control.UpdateSongInfo(MediaSnapshot.Disconnected with
                {
                    IsConnected = true,
                    Title = "A short track",
                    Artist = "Artist",
                    Duration = 180,
                    CanPlayPause = true,
                    CanSkipPrevious = true,
                    CanSkipNext = true
                });
                var actions = (StackPanel)control.FindName("TaskbarHoverActions");
                var hover = (Grid)control.FindName("HoverRevealHost");
                var button = (Button)control.FindName("TaskbarPlayPauseButton");
                var artwork = (Border)control.FindName("SongImageBorder");
                foreach (var position in new[] { TaskbarBarPosition.Start, TaskbarBarPosition.Center, TaskbarBarPosition.End })
                {
                    SettingsManager.Current.Position = position;
                    SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                    {
                        Arrangement = position == TaskbarBarPosition.End ? TaskbarContentArrangement.Right : TaskbarContentArrangement.Left,
                        SpectrumVisible = false,
                        PerformanceVisible = false,
                        OutputDeviceVisible = false,
                        VolumeVisible = false
                    };
                    control.ApplyTaskbarExperienceSettings();
                    MediaBarTestAccess.GetField("_isTaskbarHoverVisible", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(control, true);
                    hover.Visibility = Visibility.Visible;
                    double? previous = null;
                    foreach (var width in new[] { 420.3, 520.7, 620.25 })
                    {
                        control.ApplyPrimaryLength(width);
                        hover.Width = ((FrameworkElement)control.FindName("SongInfoStackPanel")).Width;
                        control.Width = width;
                        control.Height = 44;
                        control.Measure(new Size(width, 44));
                        control.Arrange(new Rect(0, 0, width, 44));
                        control.UpdateLayout();
                        var origin = position switch
                        {
                            TaskbarBarPosition.Center => 1000 - width / 2,
                            TaskbarBarPosition.End => 2000 - width,
                            _ => 0
                        };
                        var point = button.TranslatePoint(new Point(button.ActualWidth / 2, 0), control).X + origin;
                        if (previous is { } expected)
                            Assert.AreEqual(expected, point, 1.1, $"{position}: button moved when content width changed");
                        previous = point;
                        for (var frame = 0; frame < 3; frame++)
                        {
                            control.ApplyPrimaryLength(width);
                            control.Measure(new Size(width, 44));
                            control.Arrange(new Rect(0, 0, width, 44));
                            control.UpdateLayout();
                            var repeated = button.TranslatePoint(new Point(button.ActualWidth / 2, 0), control).X + origin;
                            Assert.AreEqual(point, repeated, 0.01,
                                $"{position}: repeated geometry application moved the button at frame {frame}");
                        }
                        if (position == TaskbarBarPosition.End)
                            Assert.IsTrue(artwork.TranslatePoint(new Point(), control).X > width / 2);
                        Assert.IsTrue(actions.ActualWidth > 0);
                    }
                }
                foreach (var direction in new[] { TaskbarContentArrangement.Left, TaskbarContentArrangement.Right })
                    foreach (var alignment in new[] { TaskbarBarPosition.Start, TaskbarBarPosition.Center, TaskbarBarPosition.End })
                    {
                        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                        {
                            Arrangement = direction
                        };
                        SettingsManager.Current.Position = alignment;
                        control.ApplyTaskbarExperienceSettings();
                        MediaBarTestAccess.GetField("_isTaskbarHoverVisible", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .SetValue(control, true);
                        hover.Visibility = Visibility.Visible;
                        const double Width = 520.7;
                        double? anchor = null;
                        for (var frame = 0; frame < 24; frame++)
                        {
                            control.ApplyPrimaryLength(Width);
                            hover.Width = ((FrameworkElement)control.FindName("SongInfoStackPanel")).Width;
                            control.Width = Width;
                            control.Measure(new Size(Width, 44));
                            control.Arrange(new Rect(0, 0, Width, 44));
                            control.UpdateLayout();
                            var point = button.TranslatePoint(new Point(), control).X;
                            if (anchor is { } expected)
                                Assert.AreEqual(expected, point, 0.01, $"{direction}/{alignment}: repeated layout moved the button");
                            anchor = point;
                        }
                    }
                SettingsManager.Current.Position = TaskbarBarPosition.Center;
                SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                { Arrangement = TaskbarContentArrangement.Right, SpectrumVisible = true, PerformanceVisible = true };
                control.ApplyTaskbarExperienceSettings();
                hover.Visibility = Visibility.Visible;
                control.UseLayoutRounding = false;
                foreach (var placement in new[] { TaskbarBarPosition.Start, TaskbarBarPosition.Center, TaskbarBarPosition.End })
                    foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                    {
                        SettingsManager.Current.Position = placement;
                        var minimum = (int)Math.Ceiling(control.GetMinimumPrimaryLength(scale) * scale);
                        double? expected = null;
                        foreach (var width in new[] { minimum, minimum + 1, minimum + 2, minimum + 121 })
                        {
                            const long CenterTwice = 2001;
                            var anchorTwice = placement == TaskbarBarPosition.Center ? CenterTwice : 2000;
                            var left = AFMediaBar.Classes.Services.TaskbarPlacementPolicy.Position(anchorTwice, width, placement);
                            control.ApplyPlacementAnchor(left, anchorTwice, scale);
                            control.ApplyPrimaryLength(width / scale);
                            hover.Width = ((FrameworkElement)control.FindName("SongInfoStackPanel")).Width;
                            control.Width = width / scale;
                            control.Measure(new Size(width / scale, 44));
                            control.Arrange(new Rect(0, 0, width / scale, 44));
                            control.UpdateLayout();
                            var absolute = actions.TranslatePoint(new Point(), control).X * scale + left;
                            if (expected is { } anchor)
                                Assert.AreEqual(anchor, absolute, 0.01, $"DPI {scale}: odd/even width moved the actions");
                            expected = absolute;
                        }
                    }
                SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                { Arrangement = TaskbarContentArrangement.Left };
                SettingsManager.Current.Position = TaskbarBarPosition.Start;
                control.ApplyTaskbarExperienceSettings();
                AssertPerformanceLayout(control);
                VerticalTaskbarPresentationChecks.Verify(control);
                SmallTaskbarPresentationChecks.Verify(control);
                AssertSmallArtworkLayout(control);
                AssertFontSizeKeepsArtworkInset();
                AssertHoverControlsFitAtMinimumLength();
                AssertMetadataSurvivesOrientationChanges();
                AssertLyricsSurviveOrientationChanges();
                AssertLyricsTimelineChangesResizeRepeatedText();
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                MetadataMarqueePresentationChecks.Verify();
            }
            catch (Exception error) { failure = error; }
            finally
            {
                SettingsManager.Replace(original);
                app.Shutdown();
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "WPF layout did not finish");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void AssertSmallArtworkLayout(TaskBarMediaControl control)
    {
        var original = SettingsManager.Current;
        try
        {
            SettingsManager.Current = new AppSettings();
            control.UpdateSongInfo(MediaSnapshot.Disconnected with { IsConnected = true, Title = "Small artwork" });
            var artwork = (Border)control.FindName("SongImageBorder");
            var text = (FrameworkElement)control.FindName("SongInfoStackPanel");
            foreach (var mode in new[] { WindowMode.Taskbar, WindowMode.FloatingBar })
                foreach (var orientation in new[] { LayoutOrientation.Horizontal, LayoutOrientation.Vertical })
                {
                    void Apply(bool small, bool visible)
                    {
                        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                        { SmallArtwork = small, ArtworkVisible = visible };
                        control.ApplyLayout(mode, orientation, 100, 100, orientation == LayoutOrientation.Vertical ? 80 : 44);
                        control.ApplyPrimaryLength(600);
                    }

                    double TextStart() => orientation == LayoutOrientation.Vertical ? Canvas.GetTop(text) : Canvas.GetLeft(text);
                    Apply(false, true);
                    var normalSize = artwork.Width;
                    var normalTextStart = TextStart();
                    Apply(true, true);
                    Assert.AreEqual(Math.Round(normalSize * 0.8), artwork.Width, 0.01, $"{mode}/{orientation}: artwork must shrink at runtime.");
                    Assert.AreEqual(artwork.Width, artwork.Height, 0.01);
                    Assert.IsTrue(TextStart() < normalTextStart, "Smaller artwork must free space for the following text.");
                    if (orientation == LayoutOrientation.Vertical)
                        Assert.AreEqual((80 - artwork.Width) / 2, Canvas.GetLeft(artwork), 0.01);

                    Apply(false, false);
                    var hiddenTextStart = TextStart();
                    Apply(true, false);
                    Assert.AreEqual(hiddenTextStart, TextStart(), 0.01, "Hidden artwork must not add padding to the text.");
                }
        }
        finally { SettingsManager.Replace(original); }
    }

    private static void AssertMetadataSurvivesOrientationChanges()
    {
        var settings = new AppSettings
        {
            TaskbarExperience = new() { ContentLayout = TaskbarContentLayout.AdaptiveStack }
        };
        var snapshot = MediaSnapshot.Disconnected with
        {
            IsConnected = true,
            Title = "无歌词标题 Mixed 🎵",
            Artist = "Artist"
        };
        foreach (var mode in new[] { WindowMode.Taskbar, WindowMode.FloatingBar })
        {
            var control = new TaskBarMediaControl();
            control.ApplyPresentationSettings(settings);
            control.UpdateSongInfo(snapshot);
            var title = (TextBlock)control.FindName("SongTitle");
            var artist = (TextBlock)control.FindName("SongArtist");
            var metadata = (FrameworkElement)control.FindName("SongMetadataPanel");
            for (var cycle = 0; cycle < 2; cycle++)
            {
                control.ApplyLayout(mode, LayoutOrientation.Vertical, 100, 100, 48);
                control.ApplyPrimaryLength(520);
                control.Measure(new Size(48, 520));
                control.Arrange(new Rect(0, 0, 48, 520));
                control.ApplyLayout(mode, LayoutOrientation.Horizontal, 100, 100, 44);
                control.ApplyPrimaryLength(520);
                control.UpdateSongInfo(snapshot);
                control.Measure(new Size(520, 44));
                control.Arrange(new Rect(0, 0, 520, 44));
                control.UpdateLayout();
                Assert.AreEqual(Visibility.Visible, metadata.Visibility);
                Assert.AreEqual(snapshot.Title, title.Text);
                Assert.AreEqual(snapshot.Artist, artist.Text);
                foreach (var text in new[] { title, artist })
                {
                    var origin = text.TranslatePoint(new Point(), control);
                    Assert.IsTrue(origin.Y >= 0 && origin.Y + text.ActualHeight <= 44,
                        $"{mode}: {text.Name} is outside the horizontal bar after orientation change: {origin.Y}, height={text.ActualHeight}");
                }
            }
        }
    }

    private static void AssertFontSizeKeepsArtworkInset()
    {
        var settings = new AppSettings { Position = TaskbarBarPosition.End };
        settings.TaskbarExperience = new()
        {
            Arrangement = TaskbarContentArrangement.Left,
            LengthMode = TaskbarLengthMode.Fixed,
            FixedLengthDip = 180,
            SpectrumVisible = false,
            PerformanceVisible = false
        };
        var control = new TaskBarMediaControl { AllowRestConnectionTransition = false };
        try
        {
            control.ApplyPresentationSettings(settings);
            control.UpdateSongInfo(MediaSnapshot.Disconnected with { IsConnected = true, Title = "Title", Artist = "Artist" });
            foreach (var cross in new[] { 24d, 32d, 44d })
            {
                double? inset = null;
                foreach (var font in new[] { 100, 80, 125, 100 })
                {
                    settings.TaskbarExperience = settings.TaskbarExperience with { MediaFontSizePercent = font };
                    control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100, cross);
                    control.ApplyPrimaryLength(180);
                    var left = Canvas.GetLeft((FrameworkElement)control.FindName("SongImageBorder"));
                    if (inset is { } expected) Assert.AreEqual(expected, left, .01, $"Height {cross}, font {font}% moved artwork.");
                    inset = left;
                }
            }
        }
        finally { control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    }

    private static void AssertHoverControlsFitAtMinimumLength()
    {
        var settings = new AppSettings();
        var control = new TaskBarMediaControl { AllowRestConnectionTransition = false };
        control.DesiredSizeChanged += (_, _) => { };
        try
        {
            control.ApplyPresentationSettings(settings);
            control.UpdateSongInfo(MediaSnapshot.Disconnected with { IsConnected = true, Title = "Title", Artist = "Artist" });
            foreach (var position in Enum.GetValues<TaskbarBarPosition>())
                foreach (var arrangement in Enum.GetValues<TaskbarContentArrangement>())
                    foreach (var density in Enum.GetValues<TaskbarInformationDensity>())
                        foreach (var transport in new[] { false, true })
                            foreach (var dpi in new[] { 1d, 1.25, 1.5, 2 })
                                foreach (var anchorDelta in new[] { -16, 0, 16 })
                                {
                                    settings.Position = position;
                                    settings.TaskbarExperience = TaskbarExperienceSettings.Default with
                                    {
                                        Arrangement = arrangement,
                                        Density = density,
                                        SpectrumVisible = false,
                                        PerformanceVisible = false,
                                        HoverControls = TaskbarHoverControlsSettings.Default with { PlayPauseVisible = transport, PreviousNextVisible = transport }
                                    };
                                    control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100, 44);
                                    control.RefreshDesiredSize();
                                    var widthPx = (int)Math.Ceiling(control.GetMinimumPrimaryLength(dpi) * dpi);
                                    var anchor = position == TaskbarBarPosition.Center ? widthPx : position == TaskbarBarPosition.End ? 2L * widthPx : 0;
                                    // 几何刷新时保留上一帧锚点，按钮仍须位于当前文字区。
                                    control.ApplyPlacementAnchor(0, anchor + anchorDelta, dpi);
                                    control.ApplyPrimaryLength(widthPx / dpi);
                                    var reveal = (Grid)control.FindName("HoverRevealHost");
                                    var layer = (Border)control.FindName("TaskbarHoverLayer");
                                    reveal.Visibility = Visibility.Visible;
                                    layer.Visibility = Visibility.Visible;
                                    reveal.Width = layer.Width;
                                    control.Width = widthPx / dpi;
                                    control.Height = 44;
                                    control.Measure(new Size(control.Width, 44));
                                    control.Arrange(new Rect(0, 0, control.Width, 44));
                                    control.UpdateLayout();
                                    foreach (var name in new[] { "TaskbarPreviousButton", "TaskbarPlayPauseButton", "TaskbarNextButton", "TaskbarDeviceButton", "TaskbarVolumeButton" })
                                    {
                                        var button = (Button)control.FindName(name);
                                        if (button.Visibility != Visibility.Visible) continue;
                                        var left = button.TranslatePoint(new Point(), layer).X;
                                        Assert.IsTrue(left >= -.01 && left + button.ActualWidth <= layer.ActualWidth + .01,
                                            $"{position}/{arrangement}/{density}/{transport}/DPI {dpi}: {name} outside hover layer ({left} + {button.ActualWidth} > {layer.ActualWidth}).");
                                    }
                                }
        }
        finally { control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    }

    private static void AssertLyricsTimelineChangesResizeRepeatedText()
    {
        var settings = new AppSettings
        {
            TwoLineLyricsEnabled = false,
            LyricsLongLineScrollEnabled = true,
            TaskbarExperience = new() { LengthMode = TaskbarLengthMode.FollowContent, HoverLayerEnabled = false }
        };
        var control = new TaskBarMediaControl { AllowRestConnectionTransition = false };
        try
        {
            control.ApplyPresentationSettings(settings);
            control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100, 44);
            var text = new string('长', 60);
            var snapshot = MediaSnapshot.Disconnected with
            {
                IsConnected = true,
                Duration = 20,
                Position = 2,
                Lyrics = new LyricsResult("test", new LyricDocument([
                    new LyricLine(0, 5, text) { Words = [new(0, 5, text)] },
                    new LyricLine(5, 10, text)
                ], LyricsSyncType.MixedSynced, "test"))
            };
            var widths = new List<double>();
            control.DesiredSizeChanged += (_, args) => widths.Add(args.Request.Width);
            control.UpdateSongInfo(snapshot);
            var cappedWidth = widths[^1];
            control.UpdateSongInfo(snapshot with { Position = 7 });
            var naturalWidth = widths[^1];
            Assert.IsTrue(naturalWidth > cappedWidth + 100, "相同歌词失去逐字时间轴后必须恢复自然宽度，否则无法滚动的歌词会被截断。");

            // 模拟下一次定时器投影，验证没有新媒体快照触发的额外尺寸请求时也能重新封顶。
            MediaBarTestAccess.GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(control, snapshot);
            MediaBarTestAccess.GetMethod("UpdateWebLyricsPresentation", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, [false, false]);
            Assert.AreEqual(cappedWidth, widths[^1], .01, "逐字时间轴恢复后应立即重新封顶。");
        }
        finally { control.DisposePresentation(); }
    }

    private static void AssertLyricsSurviveOrientationChanges()
    {
        var lyrics = new LyricsResult("test", new LyricDocument([
            new LyricLine(2, 10, "当前歌词 Mixed 🎵") { Words = [new(2, 5, "当前"), new(5, 10, "歌词 Mixed 🎵")] },
            new LyricLine(10, 20, "Next line")], LyricsSyncType.SyllableSynced, "test"));
        foreach (var mode in new[] { WindowMode.Taskbar, WindowMode.FloatingBar })
        {
            var settings = new AppSettings();
            var control = new TaskBarMediaControl();
            control.ApplyPresentationSettings(settings);
            var webView = new WebView2CompositionControl { MinWidth = 1, MinHeight = 1 };
            using var renderer = new LyricsWebViewRenderer(webView);
            // 仅提供浏览器就绪状态以检查真实呈现分支和 WPF 容器；不把此检查当成 WebView2 实机验收。
            typeof(LyricsWebViewRenderer).GetField("_documentReady", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, true);
            MediaBarTestAccess.GetField("_lyricsWebRenderer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, renderer);
            MediaBarTestAccess.GetField("_lyricsHostLoaded", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, true);
            ((Border)control.FindName("LyricsWebViewHost")).Child = webView;
            try
            {
                foreach (var twoLines in new[] { false, true })
                    foreach (var position in new[] { 3d, 10d, 0d, -1d })
                    {
                        settings.TwoLineLyricsEnabled = twoLines;
                        var snapshot = MediaSnapshot.Disconnected with
                        {
                            IsConnected = true,
                            Title = "Fallback title",
                            Artist = "Artist",
                            Lyrics = position < 0 ? null : lyrics,
                            Position = Math.Max(0, position),
                            Duration = 20
                        };
                        foreach (var orientation in new[] { LayoutOrientation.Vertical, LayoutOrientation.Horizontal })
                        {
                            control.ApplyLayout(mode, orientation, 100, 100, orientation == LayoutOrientation.Vertical ? 48 : 44);
                            control.UpdateSongInfo(snapshot);
                            control.ApplyPrimaryLength(520);
                            var size = orientation == LayoutOrientation.Vertical ? new Size(48, 520) : new Size(520, 44);
                            control.Measure(size);
                            control.Arrange(new Rect(new Point(), size));
                            control.UpdateLayout();
                            var panel = (FrameworkElement)control.FindName("SongLyricsPanel");
                            var origin = panel.TranslatePoint(new Point(), control);
                            Assert.IsTrue(panel.ActualWidth > 0 && panel.ActualHeight > 0);
                            Assert.IsTrue(origin.X >= 0 && origin.Y >= 0 && origin.X + panel.ActualWidth <= size.Width + .01 &&
                                origin.Y + panel.ActualHeight <= size.Height + .01, $"{mode}/{orientation}: lyric viewport escaped the bar.");
                            var frame = LyricsPresentationProjector.Project(snapshot, settings, DateTimeOffset.UtcNow);
                            Assert.AreEqual(orientation == LayoutOrientation.Vertical || frame.IsVisible ? 1d : 0d, panel.Opacity);
                            if (orientation == LayoutOrientation.Horizontal)
                            {
                                Assert.AreEqual(frame.IsVisible ? Visibility.Collapsed : Visibility.Visible,
                                    ((FrameworkElement)control.FindName("SongMetadataPanel")).Visibility);
                                if (frame.IsVisible)
                                {
                                    var effective = (LyricsPresentationFrame)MediaBarTestAccess.GetField("_lastEffectiveLyricsFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
                                    Assert.AreEqual(frame.Current, effective.Current);
                                    Assert.AreEqual(twoLines && position == 3 ? "Next line" : string.Empty, effective.Next);
                                    if (position == 3)
                                        Assert.IsTrue(effective.WordScanProgress is >= 0 and <= 1);
                                }
                            }
                        }
                    }
            }
            finally { control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        }
    }

    private static void AssertPerformanceLayout(TaskBarMediaControl control)
    {
        SettingsManager.Current.PerformanceComponent = new PerformanceComponentSettings(
            [MetricKind.SystemMemory, MetricKind.SystemCpu, MetricKind.SystemNetwork], 500, true)
        {
            DisplayMode = PerformanceDisplayMode.Parallel,
            MetricOrder = [MetricKind.SystemNetwork, MetricKind.SystemCpu, MetricKind.SystemMemory]
        };
        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { PerformanceVisible = true };
        control.ApplyTaskbarExperienceSettings();
        control.ApplyPerformanceSnapshot(new(40, 60, null, null, 1024, 2048), 0, true);
        var items = (StackPanel)control.FindName("TaskbarPerformanceItems");
        var surface = (Border)control.FindName("TaskbarPerformanceSurface");
        var cells = items.Children.Cast<TextBlock>().ToArray();
        Assert.AreEqual(3, cells.Length);
        Assert.IsTrue(cells.All(item => item.Visibility == Visibility.Visible));
        Assert.IsTrue(cells[0].Text.StartsWith("↑ ") && cells[0].Text.Contains("\n↓ "));
        Assert.AreEqual(TextAlignment.Left, cells[0].TextAlignment,
            "网速两行必须左对齐：宽度按最大占位固定，各自居中的话短行的箭头会随速率漂移。");
        Assert.AreEqual(TextAlignment.Center, cells[1].TextAlignment, "其余单行指标保持居中。");
        Assert.IsTrue(cells[1].Text.StartsWith("CPU "));
        Assert.IsTrue(cells[2].Text.StartsWith("MEM "));
        var parallelWidth = surface.Width;
        control.ApplyPerformanceSnapshot(new(80, 100, null, null, 0, 0), 2, true);
        Assert.AreEqual(parallelWidth, surface.Width, "Changing ordinary readings must not resize the widget.");

        SettingsManager.Current.PerformanceComponent = SettingsManager.Current.PerformanceComponent with { DisplayMode = PerformanceDisplayMode.Cycle };
        control.ApplyTaskbarExperienceSettings();
        control.ApplyPerformanceSnapshot(new(40, 60, null, null, 1024, 2048), 0, true);
        var cycleWidth = surface.Width;
        control.ApplyPerformanceSnapshot(new(40, 60, null, null, 1024, 2048), 1, true);
        Assert.AreEqual(cycleWidth, surface.Width, "Cycling reserves the widest selected metric.");
        Assert.AreEqual(1, items.Children.Cast<TextBlock>().Count(item => item.Visibility == Visibility.Visible));
        Assert.IsTrue(items.Children.Cast<TextBlock>().Single(item => item.Visibility == Visibility.Visible).Text.StartsWith("CPU "));
        Assert.IsTrue(cycleWidth < parallelWidth);

        SettingsManager.Current.PerformanceComponent = new([MetricKind.SystemNetwork], 500, true);
        control.ApplyTaskbarExperienceSettings();
        Assert.AreEqual(1, items.Children.Count);
        control.ApplyPerformanceSnapshot(default, 0, true);
        Assert.IsTrue(((TextBlock)items.Children[0]).Text.Contains("—"));
    }
}
