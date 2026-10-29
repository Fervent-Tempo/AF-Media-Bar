// 在隔离进程中验证真实封面 Popup 的换树、提示来源与强制收回；不操纵系统鼠标或 Explorer。
// 测试拥有窗口和 Application，结束时关闭窗口并恢复设置；反射只用于控制动画入口和检查 WPF 缓存。
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止封面收回后遗留失效的提示来源、Popup 或延迟回调。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ArtworkZoomLifecycleTests
{
    [TestMethod]
    public void FinalDisposalReclaimsArtworkAndRejectsPresentationReactivation() => RunIsolated(nameof(FinalDisposalReclaimsArtworkAndRejectsPresentationReactivation), async (control, dispatcher) =>
    {
        Invoke(control, "ShowArtworkHoverZoom");
        Assert.IsTrue(GetPopup(control).IsOpen);
        control.DisposePresentation();
        control.DisposePresentation();
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Invoke(control, "ShowArtworkHoverZoom");
        Invoke(control, "ShowArtworkHoverPreview", true);
        Invoke(control, "UpdateWebLyricsPresentation", false, false);
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        AssertRestored(control);
        Assert.IsFalse(((Popup)MediaBarTestAccess.GetField("_artworkHoverPreview", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(control)!).IsOpen);
        Assert.IsNull(MediaBarTestAccess.GetField("_lyricsWebRenderer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(control));
        foreach (var field in new[] { "_lyricsWebTimer", "_lyricsReleaseTimer", "_lyricsRetryTimer" })
            Assert.IsFalse(((DispatcherTimer)MediaBarTestAccess.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(control)!).IsEnabled);
    });

    [TestMethod]
    public void TooltipCachedSourceRemainsValidAfterArtworkReturnsFromPopup() => RunIsolated(nameof(TooltipCachedSourceRemainsValidAfterArtworkReturnsFromPopup), async (control, dispatcher) =>
    {
        var artwork = (Border)control.FindName("SongImageBorder");
        var tooltipOwner = FindTooltipOwner(artwork);
        Assert.IsNotNull(tooltipOwner, "有媒体时封面必须仍有滚轮提示。");
        Invoke(control, "ShowArtworkHoverZoom");
        var popup = GetPopup(control);
        Assert.IsTrue(popup.IsOpen);
        Assert.AreSame(((Grid)popup.Child), artwork.Parent);
        var cachedSource = (HwndSource)PresentationSource.FromVisual(tooltipOwner)!;
        Invoke(control, "HideArtworkHoverZoom", true);
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        // WPF 使用缓存的来源作坐标转换；直接验证真实转换，不依赖其几何缓存的内部字段。
        var pointUtil = typeof(Visual).Assembly.GetType("MS.Internal.PointUtil", true)!;
        pointUtil.GetMethod("ScreenToClient", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [new Point(), cachedSource]);
        Assert.IsFalse(cachedSource.IsDisposed, "提示缓存不能指向放大 Popup 的已销毁窗口。");
        Assert.IsNotNull(FindTooltipOwner(artwork), "收回后提示必须仍可用。");
        AssertRestored(control);
    });

    [TestMethod]
    public void SuspensionPruningAndUnloadReclaimArtworkAndRejectLateOpening() => RunIsolated(nameof(SuspensionPruningAndUnloadReclaimArtworkAndRejectLateOpening), async (control, dispatcher) =>
    {
        var preview = (Popup)MediaBarTestAccess.GetField("_artworkHoverPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        preview.Child.Opacity = 0;
        Invoke(control, "ShowArtworkHoverPreview", true);
        Assert.IsTrue(preview.IsOpen);
        control.ApplyHostVisibilitySuspension(true);
        Assert.IsFalse(preview.IsOpen, "挂起必须同时关闭封面预览。");
        control.ApplyHostVisibilitySuspension(false);
        Invoke(control, "ShowArtworkHoverZoom");
        Assert.IsTrue(GetPopup(control).IsOpen);
        control.ApplyHostVisibilitySuspension(true);
        AssertRestored(control);
        Invoke(control, "ShowArtworkHoverZoom");
        Assert.IsFalse(GetPopup(control).IsOpen, "挂起期间不能重新打开封面 Popup。");
        control.ApplyHostVisibilitySuspension(false);
        Invoke(control, "ShowArtworkHoverZoom");
        Assert.IsTrue(GetPopup(control).IsOpen);
        control.ApplyBackgroundPruneLevel(MemoryPruneLevel.DisplayOff);
        AssertRestored(control);
        control.ApplyBackgroundPruneLevel(MemoryPruneLevel.None);
        Invoke(control, "ShowArtworkHoverZoom");
        Assert.IsTrue(GetPopup(control).IsOpen);
        Invoke(control, "ReclaimArtworkZoom", false);
        var window = Window.GetWindow(control);
        window.Content = null;
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.IsFalse(control.IsLoaded);
        Invoke(control, "ShowArtworkHoverZoom");
        AssertRestored(control);
    });

    [TestMethod]
    public void AlreadyClosedPopupStillReturnsArtworkToMainTree() => RunIsolated(nameof(AlreadyClosedPopupStillReturnsArtworkToMainTree), async (control, dispatcher) =>
    {
        Invoke(control, "ShowArtworkHoverZoom");
        var popup = GetPopup(control);
        Assert.IsTrue(popup.IsOpen);
        popup.IsOpen = false;
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Invoke(control, "HideArtworkHoverZoom", true);
        AssertRestored(control);
        Invoke(control, "ShowArtworkHoverZoom");
        Assert.IsTrue(popup.IsOpen, "强制收回之后必须还能再次放大。");
        Invoke(control, "HideArtworkHoverZoom", true);
        AssertRestored(control);
    });

    [TestMethod]
    public void PopupMousePressReachesHostOnceWithOriginalArtworkSource() => RunIsolated(nameof(PopupMousePressReachesHostOnceWithOriginalArtworkSource), (control, dispatcher) =>
    {
        Invoke(control, "ShowArtworkHoverZoom");
        Assert.IsTrue(GetPopup(control).IsOpen);
        var artwork = (Border)control.FindName("SongImageBorder");
        var forwarded = 0;
        var ordinary = 0;
        control.PreviewMouseLeftButtonDown += (_, _) => ordinary++;
        control.ArtworkPointerPressed += (_, args) =>
        {
            forwarded++;
            Assert.AreEqual(MouseButton.Left, args.ChangedButton);
            Assert.AreSame(artwork, args.OriginalSource);
            control.CollapseArtworkZoomForDrag();
        };
        artwork.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        });
        Assert.AreEqual(1, forwarded, "Popup 内的按下必须只交给宿主一次。");
        Assert.AreEqual(0, ordinary, "独立 Popup 不会走主控件的常规按下路由。");
        AssertRestored(control);
        return Task.CompletedTask;
    });

    [TestMethod]
    public void PopupClickAndPlaybackRefreshKeepZoomWithoutReparenting() => RunIsolated(nameof(PopupClickAndPlaybackRefreshKeepZoomWithoutReparenting), (control, dispatcher) =>
    {
        MediaBarTestAccess.GetField("_webLyricsGraphicsDisabled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, true);
        var window = Window.GetWindow(control)!;
        SettingsManager.Current.Interaction = SettingsManager.Current.Interaction with { ArtworkHoverMode = ArtworkHoverMode.Zoom };
        var snapshot = MediaSnapshot.Disconnected with
        {
            IsConnected = true,
            IsPlaying = true,
            CanPlayPause = true,
            Title = "Track",
            Artwork = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8)
        };
        foreach (var mode in new[] { WindowMode.Taskbar, WindowMode.FloatingBar })
            foreach (var orientation in new[] { LayoutOrientation.Horizontal, LayoutOrientation.Vertical })
            {
                control.ApplyLayout(mode, orientation, 100, 100, 48);
                control.ApplyPrimaryLength(500);
                window.Width = orientation == LayoutOrientation.Vertical ? 48 : 500;
                window.Height = orientation == LayoutOrientation.Vertical ? 500 : 44;
                control.UpdateSongInfo(snapshot);
                window.UpdateLayout();
                Invoke(control, "AnimateArtworkHover", true, true);
                var popup = GetPopup(control);
                var artwork = (Border)control.FindName("SongImageBorder");
                var scale = (ScaleTransform)control.FindName("SongImageScale");
                Assert.IsFalse(control.CaptureArtworkPointer(control), "主树的文字或空白不能被放大层抢走输入。");
                var presses = 0;
                var releases = 0;
                var moves = 0;
                var clicks = 0;
                MouseEventArgs? requestedMove = null;
                void Press(object sender, MouseButtonEventArgs args) { presses++; Assert.IsTrue(control.CaptureArtworkPointer(artwork)); }
                void Move(object sender, MouseEventArgs args) { if (ReferenceEquals(args, requestedMove)) moves++; }
                void Release(object sender, MouseButtonEventArgs args) { releases++; control.ReleaseArtworkPointerCapture(); }
                void Click(object? sender, EventArgs args)
                {
                    clicks++;
                    snapshot = snapshot with { IsPlaying = !snapshot.IsPlaying };
                    control.UpdateSongInfo(snapshot);
                }
                control.ArtworkPointerPressed += Press;
                control.ArtworkPointerMoved += Move;
                control.ArtworkPointerReleased += Release;
                control.TogglePlayPauseRequested += Click;
                try
                {
                    for (var click = 0; click < 2; click++)
                    {
                        artwork.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                        Assert.IsTrue(popup.IsOpen);
                        Assert.AreSame(popup.Child, artwork.Parent);
                        Assert.AreSame(popup.Child, Mouse.Captured);
                        requestedMove = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.PreviewMouseMoveEvent };
                        artwork.RaiseEvent(requestedMove);
                        artwork.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
                        artwork.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
                        Assert.IsTrue(popup.IsOpen, "普通点击和播放快照不能收回封面。");
                        Assert.AreSame(popup.Child, artwork.Parent);
                        Assert.AreEqual(2d, scale.ScaleX, .001);
                        Assert.AreEqual(click + 1, clicks);
                    }
                    Assert.AreEqual(2, presses);
                    Assert.AreEqual(2, releases);
                    Assert.AreEqual(2, moves);
                    Assert.IsTrue(control.CaptureArtworkPointer(artwork));
                    control.CollapseArtworkZoomForDrag();
                    Assert.IsNull(Mouse.Captured, "真正拖动收回后不能留下 Popup 捕获。");
                    AssertRestored(control);
                }
                finally
                {
                    control.ArtworkPointerPressed -= Press;
                    control.ArtworkPointerMoved -= Move;
                    control.ArtworkPointerReleased -= Release;
                    control.TogglePlayPauseRequested -= Click;
                    Invoke(control, "HideArtworkHoverZoom", true);
                }
            }
        return Task.CompletedTask;
    });

    [TestMethod]
    public void ContentResizeKeepsZoomTreeAndFollowsTheArtworkSlot() => RunIsolated(nameof(ContentResizeKeepsZoomTreeAndFollowsTheArtworkSlot), (control, dispatcher) =>
    {
        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
        {
            Arrangement = TaskbarContentArrangement.Right,
            SpectrumVisible = false,
            PerformanceVisible = false,
            InputDeviceVisible = false,
            OutputDeviceVisible = false,
            VolumeVisible = false
        };
        control.ApplyTaskbarExperienceSettings();
        Invoke(control, "ShowArtworkHoverZoom");
        var popup = GetPopup(control);
        var host = (Grid)popup.Child;
        var artwork = (Border)control.FindName("SongImageBorder");
        var scale = (ScaleTransform)control.FindName("SongImageScale");
        scale.ScaleX = scale.ScaleY = 1.5;
        // 不操纵系统鼠标；本回归只检查布局，不由实际指针决定是否收起。
        foreach (var field in new[] { "_artworkZoomHoverSyncTimer", "_wheelTooltipTimer" })
            ((DispatcherTimer)MediaBarTestAccess.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!).Stop();
        var generation = MediaBarTestAccess.GetField("_artworkZoomGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var openedGeneration = generation.GetValue(control);
        var hover = (Grid)control.FindName("HoverRevealHost");
        MediaBarTestAccess.GetField("_isTaskbarHoverVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, true);
        hover.Visibility = Visibility.Visible;
        hover.IsHitTestVisible = true;
        foreach (var width in new[] { 640d, 380d, 520d })
        {
            control.ApplyPrimaryLength(width);
            control.Width = width;
            control.Measure(new Size(width, 44));
            control.Arrange(new Rect(0, 0, width, 44));
            control.UpdateLayout();
            Assert.IsTrue(popup.IsOpen);
            Assert.AreSame(host, artwork.Parent, "内容变长、变短不能重新换树。");
            Assert.AreEqual(openedGeneration, generation.GetValue(control), "尺寸更新不能重新开始放大。");
            Assert.AreEqual(1.5, scale.ScaleX);
            Assert.AreEqual(Visibility.Visible, hover.Visibility, "尺寸变化不能隐藏悬停控制层。");
            Assert.IsTrue(hover.IsHitTestVisible);
            var substitute = (Border)MediaBarTestAccess.GetField("_artworkZoomSubstitute", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
            Assert.AreEqual(Canvas.GetLeft(artwork), Canvas.GetLeft(substitute), 0.01, "替身必须跟随最新封面位置。");
            var expected = substitute.PointToScreen(new Point(substitute.ActualWidth / 2, substitute.ActualHeight));
            var actual = artwork.PointToScreen(new Point(artwork.ActualWidth / 2, artwork.ActualHeight));
            Assert.AreEqual(expected.X, actual.X, 1.1, "独立 Popup 必须跟随主树封面锚点。");
            Assert.AreEqual(expected.Y, actual.Y, 1.1);
        }
        control.ApplyPlacementMotionSuspension(true);
        AssertRestored(control);
        control.ApplyPrimaryLength(580);
        Invoke(control, "ShowArtworkHoverZoom");
        AssertRestored(control);
        control.ApplyPlacementMotionSuspension(false);
        Invoke(control, "ShowArtworkHoverZoom");
        Assert.IsTrue(popup.IsOpen, "位置恢复结束后必须还能重新展开。");
        Invoke(control, "HideArtworkHoverZoom", true);
        return Task.CompletedTask;
    });

    [TestMethod]
    public void HostSuspensionSettlesConnectionTransitionsAndRejectsHiddenTransitions() => RunIsolated(nameof(HostSuspensionSettlesConnectionTransitionsAndRejectsHiddenTransitions), (control, dispatcher) =>
    {
        control.AllowRestConnectionTransition = true;
        control.UpdateSongInfo(MediaSnapshot.Disconnected);
        control.ApplyHostVisibilitySuspension(true);
        Assert.IsFalse(control.IsRestConnectionTransitionActive, "宿主挂起必须结束连接过渡，不能留下半截裁剪。");
        Assert.IsNull(((FrameworkElement)control.FindName("SongInfoStackPanel")).Clip);
        var timer = (DispatcherTimer?)MediaBarTestAccess.GetField("_restTransitionTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control);
        Assert.IsFalse(timer?.IsEnabled ?? false, "隐藏期间不能继续推进连接动画。");
        control.UpdateSongInfo(MediaSnapshot.Disconnected with { IsConnected = true, Title = "Hidden connection" });
        Assert.IsFalse(control.IsRestConnectionTransitionActive, "隐藏期间的新快照必须直接落到最新状态。");
        control.ApplyHostVisibilitySuspension(false);
        Assert.AreEqual("Hidden connection", ((System.Windows.Controls.TextBlock)control.FindName("SongTitle")).Text);
        AssertRestored(control);
        return Task.CompletedTask;
    });

    [TestMethod]
    public void MetadataEntranceDoesNotReplaceConnectionClipOrSurviveHostSuspension() => RunIsolated(nameof(MetadataEntranceDoesNotReplaceConnectionClipOrSurviveHostSuspension), (control, dispatcher) =>
    {
        control.UpdateSongInfo(MediaSnapshot.Disconnected);
        control.AllowRestConnectionTransition = true;
        var connected = MediaSnapshot.Disconnected with { IsConnected = true, Title = "Connecting" };
        control.UpdateSongInfo(connected);
        control.UpdateSongInfo(connected with { Title = "New metadata while connecting" });
        if (control.IsRestConnectionTransitionActive)
            Assert.IsFalse(((FrameworkElement)control.FindName("SongInfoStackPanel")).HasAnimatedProperties,
                "连接裁剪期间不能再让曲目入场动画控制同一文字表面。");
        control.ApplyHostVisibilitySuspension(true);
        control.UpdateSongInfo(connected with { Title = "Latest hidden metadata" });
        Assert.IsFalse(((FrameworkElement)control.FindName("SongInfoStackPanel")).HasAnimatedProperties);
        return Task.CompletedTask;
    });

    [TestMethod]
    public void StaleHoverCloseCannotHideTheNewReveal() => RunIsolated(nameof(StaleHoverCloseCannotHideTheNewReveal), (control, dispatcher) =>
    {
        var host = (FrameworkElement)control.FindName("HoverRevealHost");
        host.Visibility = Visibility.Visible;
        host.Width = 200;
        MediaBarTestAccess.GetField("_isTaskbarHoverVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, true);
        var generation = (int)MediaBarTestAccess.GetField("_hoverRevealGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        Invoke(control, "RetargetHoverRevealWidth", 240d);
        Invoke(control, "CompleteTaskbarHoverLayerHide", generation);
        Assert.AreEqual(Visibility.Visible, host.Visibility);
        Assert.AreEqual(240d, host.Width);
        return Task.CompletedTask;
    });

    [TestMethod]
    public void FullPanelEntryAnchorHoldsStillWhileTheZoomPushesTheLayout() => RunIsolated(nameof(FullPanelEntryAnchorHoldsStillWhileTheZoomPushesTheLayout), (control, dispatcher) =>
    {
        SettingsManager.Current.Interaction = SettingsManager.Current.Interaction with { ArtworkHoverMode = ArtworkHoverMode.Zoom };
        var rest = FullPanelEntryCenter(control);
        // 立即路径的悬停放大：Popup 打开、推挤同步落到目标量，无需等待动画。
        // Immediate-path hover zoom: the Popup opens and the push lands on its target synchronously, no animation wait.
        Invoke(control, "AnimateArtworkHover", true, true);
        Assert.IsTrue(GetPopup(control).IsOpen, "放大必须真的进入 Popup 态，推挤才会挂上。");
        var pushOffset = (double)MediaBarTestAccess.GetField("_artworkZoomPushOffset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        Assert.IsTrue(pushOffset > 0, "放大必须真的推挤文字区，否则锚点断言是空转。");
        // 反证：旧实现读取的正是这个被推走的元素——两者此时必须不同，锚点不变才有意义。
        // Counter-proof: the old implementation read exactly this pushed element — the two must differ here,
        // otherwise an unchanged anchor proves nothing.
        var songInfo = (FrameworkElement)control.FindName("SongInfoStackPanel");
        var pushedPanelCenter = songInfo.PointToScreen(new Point(songInfo.ActualWidth / 2, songInfo.ActualHeight / 2));
        Assert.AreNotEqual(rest, pushedPanelCenter, "文字区中心必须已被推挤带离原位。");
        Assert.AreEqual(rest, FullPanelEntryCenter(control), "放大推挤不得带动完整面板入口锚点（外侧拉杆）。");
        Invoke(control, "HideArtworkHoverZoom", true);
        AssertRestored(control);
        return Task.CompletedTask;
    });

    [TestMethod]
    public void ZoomPushTakesTheSignOfTheResolvedExpansionDirection() => RunIsolated(nameof(ZoomPushTakesTheSignOfTheResolvedExpansionDirection), (control, dispatcher) =>
    {
        SettingsManager.Current.Interaction = SettingsManager.Current.Interaction with { ArtworkHoverMode = ArtworkHoverMode.Zoom };

        // 从左侧展开（默认语义）：封面在主轴首端、内容在尾端，推挤取正，把内容带离放大的封面。
        // Left-side expansion (the default): the artwork sits at the leading end and the content behind it, so the
        // push takes the positive side, carrying the content away from the enlarged cover.
        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { Arrangement = TaskbarContentArrangement.Left };
        control.ApplyTaskbarExperienceSettings();
        Invoke(control, "AnimateArtworkHover", true, true);
        Assert.IsTrue(GetPopup(control).IsOpen, "放大必须真的进入 Popup 态，推挤才会挂上。");
        var pushOffset = (double)MediaBarTestAccess.GetField("_artworkZoomPushOffset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        Assert.IsTrue(pushOffset > 0, "推挤量必须是无符号的正量。");
        AssertZoomPush(control, +pushOffset, "从左侧展开");
        Invoke(control, "HideArtworkHoverZoom", true);
        AssertRestored(control);

        // 从右侧展开：Arrange 的 fromRight 把封面镜像到尾端、内容全部排在它另一侧，推挤必须取负——
        // 曾恒写正值，把内容推向放大的封面（issue #236）。量不变，只翻方向。
        // Right-side expansion: Arrange's fromRight mirrors the artwork to the trailing end with all content on its
        // other side, so the push must take the negative side — it once always wrote a positive value, driving the
        // content under the enlarged cover (issue #236). The amount stays; only the direction flips.
        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { Arrangement = TaskbarContentArrangement.Right };
        control.ApplyTaskbarExperienceSettings();
        Invoke(control, "AnimateArtworkHover", true, true);
        Assert.IsTrue(GetPopup(control).IsOpen, "右侧展开下放大也必须真的进入 Popup 态。");
        AssertZoomPush(control, -pushOffset, "从右侧展开");
        Invoke(control, "HideArtworkHoverZoom", true);
        AssertRestored(control);
        return Task.CompletedTask;
    });

    private static Point FullPanelEntryCenter(TaskBarMediaControl control) =>
        (Point)MediaBarTestAccess.GetMethod("GetFullPanelEntryScreenCenter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, null)!;

    /// <summary>断言所有推挤变换都落到目标量（含符号）：内容避让方向必须与展开排布一致。</summary>
    private static void AssertZoomPush(TaskBarMediaControl control, double target, string context)
    {
        var transforms = (List<TranslateTransform>)MediaBarTestAccess.GetField("_artworkZoomPushTransforms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        Assert.IsTrue(transforms.Count > 0, $"{context}：放大必须挂上推挤变换，否则符号断言是空转。");
        foreach (var transform in transforms)
            Assert.AreEqual(target, transform.X, 0.001, $"{context}：推挤必须沿内容避让方向落到目标量。");
    }

    private static void RunIsolated(string name, Func<TaskBarMediaControl, Dispatcher, Task> verify)
    {
        const string IsolationVariable = "AFMB_ARTWORK_ZOOM_ISOLATED";
        if (Environment.GetEnvironmentVariable(IsolationVariable) != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(ArtworkZoomLifecycleTests).Assembly.Location);
            start.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(ArtworkZoomLifecycleTests).FullName}.{name}");
            start.Environment[IsolationVariable] = "1";
            start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(60000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                Assert.Fail("封面放大生命周期回归超时。");
            }
            Assert.AreEqual(0, child.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            return;
        }

        StaTest.Run(async dispatcher =>
        {
            var original = SettingsManager.Current;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Window? window = null;
            try
            {
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var resource in new[] { "MotionResources", "SettingsAppearanceResources" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative)
                    });
                SettingsManager.Current = new AppSettings();
                var control = new TaskBarMediaControl { AllowRestConnectionTransition = false };
                control.UpdateSongInfo(MediaSnapshot.Disconnected with
                {
                    IsConnected = true,
                    IsPlaying = true,
                    Artwork = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8)
                });
                control.ApplyPrimaryLength(500);
                ((Grid)GetPopup(control).Child).Opacity = 0;
                window = new Window
                {
                    Content = control,
                    Width = 500,
                    Height = 44,
                    Left = -20000,
                    Top = -20000,
                    Opacity = 0,
                    ShowActivated = false,
                    ShowInTaskbar = false
                };
                window.Show();
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsTrue(control.IsLoaded);
                await verify(control, dispatcher);
            }
            finally
            {
                window?.Close();
                SettingsManager.Current = original;
                app.Shutdown();
            }
        });
    }

    private static FrameworkElement? FindTooltipOwner(FrameworkElement artwork)
    {
        for (DependencyObject? current = artwork; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement element && element.ToolTip is not null)
                return element;
        return null;
    }

    private static Popup GetPopup(TaskBarMediaControl control) =>
        (Popup)MediaBarTestAccess.GetField("_artworkHoverZoomPopup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;

    private static void Invoke(TaskBarMediaControl control, string method, params object[] arguments) =>
        MediaBarTestAccess.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, arguments);

    private static void AssertRestored(TaskBarMediaControl control)
    {
        Assert.IsFalse(GetPopup(control).IsOpen);
        Assert.AreSame(control.FindName("MainCanvas"), ((Border)control.FindName("SongImageBorder")).Parent);
        Assert.IsTrue(((Border)control.FindName("MainBorder")).ClipToBounds);
        foreach (var field in new[] { "_artworkZoomHoverSyncTimer", "_artworkZoomReclaimFallbackTimer" })
            Assert.IsFalse(((DispatcherTimer)MediaBarTestAccess.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!).IsEnabled);
    }
}
