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
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止封面收回后遗留失效的提示来源、Popup 或延迟回调。</summary>
[TestClass]
[TestCategory("Native")]
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
        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { PerformanceVisible = true };
        var snapshot = MediaSnapshot.Disconnected with
        {
            IsConnected = true,
            IsPlaying = true,
            CanPlayPause = true,
            Title = "Track",
            Artwork = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8)
        };
        // 性能组件在轮换与并排下呈现的元素集不同：轮换只显示当前指标的动态表面，并排显示全部已开启指标。
        // 两种显示模式 × 任务栏/浮动栏 × 横向/竖向共八种呈现都必须随封面推挤，只覆盖一种模式会漏掉另一套元素。
        foreach (var displayMode in new[] { PerformanceDisplayMode.Cycle, PerformanceDisplayMode.Parallel })
        {
            SettingsManager.Current.PerformanceComponent = SettingsManager.Current.PerformanceComponent with
            {
                DisplayMode = displayMode,
                Metrics = [MetricKind.SystemMemory, MetricKind.SystemCpu]
            };
            foreach (var mode in new[] { WindowMode.Taskbar, WindowMode.FloatingBar })
                foreach (var orientation in new[] { LayoutOrientation.Horizontal, LayoutOrientation.Vertical })
                {
                    control.ApplyLayout(mode, orientation, 100, 100, 48);
                    control.ApplyPrimaryLength(500);
                    var vertical = orientation == LayoutOrientation.Vertical;
                    window.Width = vertical ? 48 : 500;
                    window.Height = vertical ? 500 : 44;
                    control.UpdateSongInfo(snapshot);
                    window.UpdateLayout();

                    var context = $"{displayMode}/{mode}/{orientation}";
                    // 先确认该呈现真的以对应显示模式展示了性能组件，否则跟随断言没有覆盖对象。
                    var performanceSurfaces = VisibleMetricSurfaces(control);
                    Assert.AreEqual(displayMode == PerformanceDisplayMode.Parallel ? 2 : 1, performanceSurfaces.Count,
                        $"{context}：性能组件必须以该显示模式呈现。");
                    // 静置层里除纯装饰层与承载封面/文字的主画布外，所有可见的直系表面都必须随封面推挤。
                    var following = VisiblePushFollowingElements(control);
                    Assert.IsTrue(following.Count >= 2, $"{context}：随推挤的直系表面至少包含频谱与性能组件。");
                    var moveHandle = (FrameworkElement)MediaBarTestAccess.GetField("_barMoveHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
                    Assert.IsFalse(following.Contains(moveHandle), $"{context}：栏外手柄不参与封面推挤。");
                    foreach (var surface in performanceSurfaces)
                        Assert.IsTrue(following.Contains(surface), $"{context}/{ElementLabel(surface)}：性能表面必须属于随推挤的直系表面。");
                    var interactionSurface = (Grid)control.FindName("InteractionSurface")!;
                    var before = following.ToDictionary(element => element, element => element.TranslatePoint(new Point(), interactionSurface));

                    Invoke(control, "AnimateArtworkHover", true, true);
                    var popup = GetPopup(control);
                    Assert.IsTrue(popup.IsOpen, $"{context}：必须真的进入原地放大。");
                    var pushTransforms = (List<TranslateTransform>?)MediaBarTestAccess
                        .GetField("_artworkZoomPushTransforms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control);
                    Assert.IsNotNull(pushTransforms, $"{context}：放大必须建立推挤变换。");
                    var transforms = pushTransforms!;
                    Assert.IsTrue(transforms.Count > 0, $"{context}：推挤变换清单不能为空。");
                    var push = transforms[0];
                    var expectedX = vertical ? 0 : push.X;
                    var expectedY = vertical ? push.Y : 0;
                    Assert.IsTrue(Math.Abs(expectedX) + Math.Abs(expectedY) > .5, $"{context}：放大必须真的产生推挤量。");
                    foreach (var element in following)
                    {
                        var pushElement = PushTransformOf(element);
                        Assert.IsNotNull(pushElement, $"{context}/{ElementLabel(element)}：必须登记进推挤变换清单。");
                        Assert.IsTrue(transforms.Contains(pushElement!), $"{context}/{ElementLabel(element)}：必须登记进推挤变换清单。");
                        var moved = element.TranslatePoint(new Point(), interactionSurface);
                        Assert.AreEqual(expectedX, moved.X - before[element].X, .001,
                            $"{context}/{ElementLabel(element)}：实际呈现必须随封面沿横轴避让。");
                        Assert.AreEqual(expectedY, moved.Y - before[element].Y, .001,
                            $"{context}/{ElementLabel(element)}：实际呈现必须随封面沿纵轴避让。");
                    }

                    // 轮换可能在放大存续期间切到下一个指标：当前未显示的指标表面也必须预先挂好推挤，
                    // 否则切过去的那一个会停在原位——这正是"性能组件不跟随"的原始缺陷路径。
                    foreach (var surface in MetricSurfaces(control))
                    {
                        var pushElement = PushTransformOf(surface);
                        Assert.IsNotNull(pushElement, $"{context}/{ElementLabel(surface)}：所有指标表面（含未显示）都必须登记进推挤变换清单。");
                        Assert.AreEqual(expectedX, pushElement!.X, .001, $"{context}/{ElementLabel(surface)}：未显示的指标表面也必须预置横轴推挤。");
                        Assert.AreEqual(expectedY, pushElement.Y, .001, $"{context}/{ElementLabel(surface)}：未显示的指标表面也必须预置纵轴推挤。");
                    }

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
                        // 收回必须清零推挤、丢弃变换清单并让跟随表面回到原位，否则下一次激活会从半推状态开始。
                        Assert.IsNull(MediaBarTestAccess
                            .GetField("_artworkZoomPushTransforms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control),
                            $"{context}：收回必须丢弃推挤变换清单。");
                        foreach (var element in following)
                        {
                            var pushTransform = PushTransformOf(element);
                            Assert.IsNotNull(pushTransform, $"{context}/{ElementLabel(element)}：收回后推挤变换不能丢失。");
                            Assert.AreEqual(0d, pushTransform!.X, .001, $"{context}/{ElementLabel(element)}：收回后推挤必须清零。");
                            Assert.AreEqual(0d, pushTransform.Y, .001, $"{context}/{ElementLabel(element)}：收回后推挤必须清零。");
                            var restored = element.TranslatePoint(new Point(), interactionSurface);
                            Assert.AreEqual(before[element].X, restored.X, .001, $"{context}/{ElementLabel(element)}：收回后必须回到原位。");
                            Assert.AreEqual(before[element].Y, restored.Y, .001, $"{context}/{ElementLabel(element)}：收回后必须回到原位。");
                        }
                        foreach (var surface in MetricSurfaces(control))
                        {
                            var pushElement = PushTransformOf(surface);
                            Assert.IsNotNull(pushElement, $"{context}/{ElementLabel(surface)}：收回后推挤变换不能丢失。");
                            Assert.AreEqual(0d, pushElement!.X, .001, $"{context}/{ElementLabel(surface)}：收回后推挤必须清零。");
                            Assert.AreEqual(0d, pushElement.Y, .001, $"{context}/{ElementLabel(surface)}：收回后推挤必须清零。");
                        }
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

    /// <summary>当前呈现下可见的性能指标悬停表面：轮换只显示当前指标，并排显示全部已开启指标。</summary>
    private static IReadOnlyList<Border> VisibleMetricSurfaces(TaskBarMediaControl control) =>
        MetricSurfaces(control).Where(surface => surface.Visibility == Visibility.Visible).ToArray();

    /// <summary>全部性能指标悬停表面（含当前未显示的指标）：轮换可能在放大存续期间切到下一个指标。</summary>
    private static IReadOnlyList<Border> MetricSurfaces(TaskBarMediaControl control)
    {
        var surfaces = (Dictionary<MetricKind, Border>)MediaBarTestAccess
            .GetField("_metricSurfaces", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        return surfaces.Values.ToArray();
    }

    /// <summary>
    /// 静置层里必须随封面推挤的可见直系表面：除纯装饰层（背景图、材质、描边）与承载封面/文字的主画布外，
    /// 所有可见子元素都是内容侧组件；XAML 里没有、运行期才创建的表面（性能指标、拖动条手柄）同样必须登记。
    /// </summary>
    private static List<FrameworkElement> VisiblePushFollowingElements(TaskBarMediaControl control)
    {
        var interactionSurface = (Grid)control.FindName("InteractionSurface")!;
        return interactionSurface.Children.OfType<FrameworkElement>()
            .Where(child => child.Visibility == Visibility.Visible)
            .Where(child => child.Name is not ("BackgroundImage" or "TaskbarMaterialSurface" or "CanvasStroke" or "TopBorder" or "MainCanvas"))
            .ToList();
    }

    /// <summary>取元素的推挤支：普通元素是裸 TranslateTransform，已有其它变换的元素被包进 TransformGroup，推挤支在最后。</summary>
    private static TranslateTransform? PushTransformOf(FrameworkElement element) => element.RenderTransform switch
    {
        TranslateTransform translate => translate,
        TransformGroup group => group.Children.OfType<TranslateTransform>().LastOrDefault(),
        _ => null
    };

    private static string ElementLabel(FrameworkElement element) => string.IsNullOrEmpty(element.Name) ? element.GetType().Name : element.Name;

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

    [TestMethod]
    public void BriefMediaLossRetainsMetricHistoryAndExpiredOrDisposedHistoryClears() => RunIsolated(nameof(BriefMediaLossRetainsMetricHistoryAndExpiredOrDisposedHistoryClears), (control, dispatcher) =>
    {
        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
        { PerformanceVisible = true, ComponentLayouts = new() { Idle = [] } };
        control.ApplyTaskbarExperienceSettings();
        var playing = MediaSnapshot.Disconnected with { IsConnected = true, IsPlaying = true, Title = "History" };
        control.UpdateSongInfo(playing);
        var presenters = (Dictionary<MetricKind, MetricPresenter>)MediaBarTestAccess.GetField("_metricPresenters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        var chart = presenters[MetricKind.SystemMemory].Charts[0];
        var history = (MetricHistory)typeof(MetricChart).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart)!;
        var sample = new SystemMetricsSnapshot(40, 50, 20, 200, 1024, 2048);
        control.ApplyPerformanceSnapshot(sample, 0, false);
        control.ApplyPerformanceSnapshot(sample, 0, false);
        Assert.AreEqual(2, history.Samples.Count);

        control.UpdateSongInfo(MediaSnapshot.Disconnected);
        control.ApplyPerformanceSnapshot(sample, 0, false);
        Assert.AreEqual(2, history.Samples.Count, "短暂无媒体保留已有样本，但不追加隐藏期间的样本。");
        control.UpdateSongInfo(playing);
        control.ApplyPerformanceSnapshot(sample, 0, false);
        Assert.AreEqual(3, history.Samples.Count);

        control.ApplyHostVisibilitySuspension(true);
        control.ApplyPerformanceSnapshot(sample, 0, false);
        Assert.AreEqual(3, history.Samples.Count);
        control.ApplyHostVisibilitySuspension(false);
        control.ApplyPerformanceSnapshot(sample, 0, false);
        Assert.AreEqual(4, history.Samples.Count, "宿主短暂隐藏也不能清空历史。");

        // 显示关闭/睡眠剪枝只暂停采样，保留期从进入剪枝起算；短暂恢复同样不能清空历史。
        control.ApplyBackgroundPruneLevel(MemoryPruneLevel.DisplayOff);
        control.ApplyPerformanceSnapshot(sample, 0, false);
        Assert.AreEqual(4, history.Samples.Count, "显示关闭剪枝期间不能追加样本。");
        control.ApplyBackgroundPruneLevel(MemoryPruneLevel.None);
        control.ApplyPerformanceSnapshot(sample, 0, false);
        Assert.AreEqual(5, history.Samples.Count, "剪枝短暂恢复仍在保留期内，不能清空历史。");

        control.UpdateSongInfo(MediaSnapshot.Disconnected);
        MediaBarTestAccess.GetField("_performanceHistoryHiddenAt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(control, Stopwatch.GetTimestamp() - Stopwatch.Frequency * 31);
        control.UpdateSongInfo(playing);
        control.ApplyPerformanceSnapshot(sample, 0, false);
        Assert.AreEqual(1, history.Samples.Count, "隐藏超过保留期后恢复不能继续沿用旧历史。");
        control.DisposePresentation();
        Assert.AreEqual(0, history.Samples.Count);
        return Task.CompletedTask;
    });

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
