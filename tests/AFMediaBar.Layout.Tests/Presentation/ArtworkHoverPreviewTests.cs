// 渲染真实预览内容，检查宽、竖和极端比例封面的四边及连续快照的尺寸回退；不启动媒体服务。
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止封面预览退回方形裁切或在切换封面后保留旧宽度。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ArtworkHoverPreviewTests
{
    [TestMethod]
    public void PreviewPreservesArtworkEdgesAndResizesAcrossSnapshots()
    {
        const string IsolationVariable = "AFMB_ARTWORK_PREVIEW_ISOLATED";
        if (Environment.GetEnvironmentVariable(IsolationVariable) != "1")
        {
            // WPF Application 每进程只能创建一次，沿用呈现回归的子进程隔离方式。
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("vstest");
            startInfo.ArgumentList.Add(typeof(ArtworkHoverPreviewTests).Assembly.Location);
            startInfo.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(ArtworkHoverPreviewTests).FullName}.{nameof(PreviewPreservesArtworkEdgesAndResizesAcrossSnapshots)}");
            startInfo.Environment[IsolationVariable] = "1";
            startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
            using var child = Process.Start(startInfo)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(60000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                Assert.Fail("封面预览回归超时。");
            }
            Assert.AreEqual(0, child.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            return;
        }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var original = SettingsManager.Current;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            TaskBarMediaControl? control = null;
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
                control = new TaskBarMediaControl();
                var popup = (Popup)MediaBarTestAccess.GetField("_artworkHoverPreview", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(control)!;
                var root = (Border)popup.Child;
                var refresh = MediaBarTestAccess.GetMethod("RefreshArtworkHoverPreviewContent", BindingFlags.Instance | BindingFlags.NonPublic)!;
                VerifyPlaybackHints(control, refresh);
                VerifyEnlargedHints(control, refresh);
                VerifyTooltipAvoidance();
                VerifyNoteColor(control);
                VerifyTaskbarBackgroundLayers(control);
                foreach (var (width, height, expectedWidth) in new[]
                {
                    (160, 90, 120d * 16 / 9), (90, 160, 72d), (64, 64, 120d),
                    (400, 40, 216d), (40, 400, 72d)
                })
                {
                    control.UpdateSongInfo(MediaSnapshot.Disconnected with
                    {
                        IsConnected = true,
                        IsPlaying = true,
                        Title = "Artwork test",
                        Artwork = CreateMarkedArtwork(width, height)
                    });
                    refresh.Invoke(control, null);
                    Assert.AreEqual(expectedWidth, root.Width, 0.01, $"{width}:{height} 预览宽度错误。");
                    AssertArtworkEdgesVisible(root, width, height);
                }
                foreach (var artwork in new ImageSource?[]
                {
                    new DrawingImage(new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(0, 0, 200, 50)))),
                    null
                })
                {
                    control.UpdateSongInfo(MediaSnapshot.Disconnected with { IsConnected = true, Artwork = artwork });
                    refresh.Invoke(control, null);
                    Assert.AreEqual(120d, root.Width, 0.01, "未知尺寸或无封面未恢复方形框。");
                    var image = ((Grid)root.Child).Children.OfType<Image>().Single();
                    Assert.AreEqual(Stretch.Uniform, image.Stretch);
                    Assert.AreSame(artwork, image.Source);
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                control?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                SettingsManager.Current = original;
                app.Shutdown();
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "封面预览渲染未结束。");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifyTaskbarBackgroundLayers(TaskBarMediaControl control)
    {
        var original = SettingsManager.Current.Appearance;
        var surface = (Border)control.FindName("TaskbarMaterialSurface");
        var texture = (Image)control.FindName("BackgroundImage");
        var appearance = original with
        {
            TaskbarBackgroundMaterial = TaskbarBackgroundMaterial.Frosted,
            ShowTaskbarBackgroundWhenIdle = false,
            ShowTaskbarBackgroundTint = true,
            ShowTaskbarBackgroundBorder = true,
            ShowTaskbarBackgroundTexture = true
        };
        var connected = MediaSnapshot.Disconnected with
        {
            IsConnected = true, IsPlaying = true, Artwork = CreateMarkedArtwork(160, 90)
        };
        try
        {
            SettingsManager.Current.Appearance = appearance;
            control.UpdateSongInfo(MediaSnapshot.Disconnected);
            Assert.AreEqual(0, ((SolidColorBrush)surface.Background).Color.A);
            Assert.AreEqual(0, ((SolidColorBrush)surface.BorderBrush).Color.A);
            control.UpdateSongInfo(connected);
            Assert.IsTrue(((SolidColorBrush)surface.Background).Color.A > 0, "重新连接后应恢复底色。");
            Assert.IsTrue(((SolidColorBrush)surface.BorderBrush).Color.A > 0);
            control.UpdateSongInfo(connected with { IsPlaying = false });
            Assert.IsTrue(((SolidColorBrush)surface.Background).Color.A > 0, "暂停不能视为无媒体。");
            control.UpdateSongInfo(MediaSnapshot.Disconnected);
            var scrim = (Border)control.FindName("SongImagePauseScrim");
            Assert.AreEqual(0d, scrim.Background.Opacity, "暂停后断开媒体必须清除封面遮罩，不能在小音符后留下底色。");
            Assert.AreEqual(0, ((SolidColorBrush)surface.Background).Color.A);
            Assert.AreEqual(0, ((SolidColorBrush)surface.BorderBrush).Color.A);
            control.UpdateSongInfo(connected);
            SettingsManager.Current.Appearance = appearance with { ShowTaskbarBackgroundTexture = false };
            control.ApplyAppearanceSettings();
            control.UpdateSongInfo(connected with { Artwork = CreateMarkedArtwork(90, 160) });
            Assert.AreEqual(Visibility.Collapsed, texture.Visibility, "更换封面不能重新启用已关闭的纹理。");
            SettingsManager.Current.Appearance = appearance with
            {
                ShowTaskbarBackgroundTint = false, ShowTaskbarBackgroundBorder = false
            };
            control.ApplyAppearanceSettings();
            Assert.AreEqual(0, ((SolidColorBrush)surface.Background).Color.A);
            Assert.AreEqual(0, ((SolidColorBrush)surface.BorderBrush).Color.A);
            Assert.AreEqual(SystemParameters.HighContrast ? Visibility.Collapsed : Visibility.Visible, texture.Visibility);
            SettingsManager.Current.Appearance = appearance with { ShowTaskbarBackgroundWhenIdle = true };
            control.UpdateSongInfo(MediaSnapshot.Disconnected);
            Assert.IsTrue(((SolidColorBrush)surface.Background).Color.A > 0, "开启无媒体背景后应保留底色。");
            SettingsManager.Current.Appearance = appearance;
            control.ApplyAppearanceSettings();
            Assert.AreEqual(0, ((SolidColorBrush)surface.Background).Color.A);
            Assert.AreEqual(Visibility.Collapsed, texture.Visibility);
            Assert.AreEqual(Visibility.Visible, ((SymbolIcon)control.FindName("SongImagePlaceholder")).Visibility);
        }
        finally
        {
            SettingsManager.Current.Appearance = original;
            control.ApplyAppearanceSettings();
        }
    }

    private static void VerifyNoteColor(TaskBarMediaControl control)
    {
        var original = SettingsManager.Current.Appearance;
        var placeholder = (SymbolIcon)control.FindName("SongImagePlaceholder");
        var custom = Color.FromRgb(0x42, 0xCB, 0xA6);
        try
        {
            SettingsManager.Current.Appearance = original with { IdleNoteColor = "#42CBA6" };
            control.UpdateSongInfo(MediaSnapshot.Disconnected);
            Assert.AreEqual(SymbolRegular.MusicNote220, placeholder.Symbol);
            Assert.AreEqual(SystemParameters.HighContrast ? SystemColors.WindowTextColor : custom, ((SolidColorBrush)placeholder.Foreground).Color);

            control.UpdateSongInfo(MediaSnapshot.Disconnected with { IsConnected = true, IsPlaying = true, Artwork = CreateMarkedArtwork(160, 90) });
            var artworkBrush = placeholder.Foreground;
            SettingsManager.Current.Appearance = original with { IdleNoteColor = "#FF8A8A" };
            control.ApplyAppearanceSettings();
            Assert.AreSame(artworkBrush, placeholder.Foreground, "自定义音符色不能覆盖媒体封面图标的取色。");

            control.UpdateSongInfo(MediaSnapshot.Disconnected with { IsConnected = true });
            Assert.AreEqual(SystemParameters.HighContrast ? SystemColors.WindowTextColor : Color.FromRgb(0xFF, 0x8A, 0x8A), ((SolidColorBrush)placeholder.Foreground).Color);

            SettingsManager.Current.Appearance = original with { IdleNoteColor = null };
            control.ApplyAppearanceSettings();
            var accent = Application.Current.TryFindResource("AfAccentBrush") as SolidColorBrush ?? SystemColors.HighlightBrush;
            Assert.AreEqual(SystemParameters.HighContrast ? SystemColors.WindowTextColor : accent.Color, ((SolidColorBrush)placeholder.Foreground).Color);
        }
        finally
        {
            SettingsManager.Current.Appearance = original;
            control.ApplyAppearanceSettings();
        }
    }

    private static void VerifyPlaybackHints(TaskBarMediaControl control, MediaBarTestAccess.Member refreshPreview)
    {
        var placeholder = (SymbolIcon)control.FindName("SongImagePlaceholder");
        var hintIcon = (SymbolIcon)control.FindName("SongImageHoverHintIcon");
        var hint = (Border)control.FindName("SongImageHoverHint");
        var animateHover = MediaBarTestAccess.GetMethod("AnimateArtworkHover", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previewIcon = (SymbolIcon)MediaBarTestAccess.GetField("_artworkHoverPreviewIcon", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(control)!;
        var artwork = CreateMarkedArtwork(160, 90);
        foreach (var playing in new[] { false, true, false })
        {
            control.UpdateSongInfo(MediaSnapshot.Disconnected with
            {
                IsConnected = true,
                IsPlaying = playing,
                CanPlayPause = true,
                Artwork = artwork
            });
            animateHover.Invoke(control, [false, true]);
            Assert.AreEqual(playing ? Visibility.Collapsed : Visibility.Visible, placeholder.Visibility);
            if (!playing)
                Assert.AreEqual(SymbolRegular.Play24, placeholder.Symbol, "暂停时常驻图标应表达继续播放。");
            Assert.AreEqual(0d, hint.Opacity);

            animateHover.Invoke(control, [true, true]);
            Assert.AreEqual(playing ? 1d : 0d, hint.Opacity, "暂停时悬停应保持原播放图标，不再叠加另一种样式。");
            Assert.AreEqual(playing ? Visibility.Collapsed : Visibility.Visible, placeholder.Visibility);
            Assert.AreEqual(SymbolRegular.Pause24, hintIcon.Symbol);
            Assert.IsTrue(hintIcon.Filled);
            Assert.AreEqual(placeholder.FontSize, hintIcon.FontSize);
            Assert.AreSame(placeholder.Foreground, hintIcon.Foreground, "悬停图标应复用封面主题色。");

            refreshPreview.Invoke(control, null);
            Assert.AreEqual(playing ? SymbolRegular.Pause24 : SymbolRegular.Play24, previewIcon.Symbol);
            Assert.AreEqual(24d, previewIcon.FontSize);
            Assert.AreSame(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : placeholder.Foreground, previewIcon.Foreground, "大图提示应跟随封面氛围色。");
            var previewImage = (Image)MediaBarTestAccess.GetField("_artworkHoverPreviewImage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
            Assert.AreEqual(1d, previewImage.Opacity, "暂停不能淡化大图预览。");
            animateHover.Invoke(control, [false, true]);
        }
    }

    private static void VerifyEnlargedHints(TaskBarMediaControl control, MediaBarTestAccess.Member refreshPreview)
    {
        var interaction = SettingsManager.Current.Interaction;
        var artwork = (Border)control.FindName("SongImageBorder");
        var canvas = (Canvas)control.FindName("MainCanvas");
        var hint = (Border)control.FindName("SongImageHoverHint");
        var icon = (SymbolIcon)control.FindName("SongImageHoverHintIcon");
        var placeholder = (SymbolIcon)control.FindName("SongImagePlaceholder");
        var scrim = (Border)control.FindName("SongImagePauseScrim");
        var scale = (ScaleTransform)control.FindName("SongImageScale");
        var host = (Grid)MediaBarTestAccess.GetField("_artworkHoverZoomHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        var popup = (Popup)MediaBarTestAccess.GetField("_artworkHoverPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        var animate = MediaBarTestAccess.GetMethod("AnimateArtworkHover", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var reclaim = MediaBarTestAccess.GetMethod("ReclaimArtworkZoom", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var disabledField = MediaBarTestAccess.GetField("_webLyricsGraphicsDisabled", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var graphicsDisabled = disabledField.GetValue(control);
        disabledField.SetValue(control, true);
        var window = new Window
        {
            Content = control,
            Width = 500,
            Height = 44,
            Left = 400,
            Top = 300,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent
        };
        try
        {
            window.Show();
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(30));
            SettingsManager.Current.Interaction = interaction with { ArtworkHoverMode = ArtworkHoverMode.Zoom };
            foreach (var orientation in new[] { LayoutOrientation.Horizontal, LayoutOrientation.Vertical })
            {
                control.ApplyLayout(WindowMode.FloatingBar, orientation, 100, 100, 48);
                control.ApplyPrimaryLength(500);
                window.Width = orientation == LayoutOrientation.Vertical ? 48 : 500;
                window.Height = orientation == LayoutOrientation.Vertical ? 500 : 44;
                window.UpdateLayout();
                foreach (var playing in new[] { false, true, false })
                {
                    var snapshot = MediaSnapshot.Disconnected with
                    { IsConnected = true, IsPlaying = playing, CanPlayPause = true, Artwork = CreateMarkedArtwork(64, 64) };
                    control.UpdateSongInfo(snapshot);
                    window.UpdateLayout();
                    animate.Invoke(control, [true, true]);
                    Assert.AreEqual(24d, icon.FontSize);
                    var color = placeholder.Foreground;
                    var updatedColor = new SolidColorBrush(Colors.Coral);
                    placeholder.Foreground = updatedColor;
                    Assert.AreSame(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : updatedColor, icon.Foreground, "放大提示应实时跟随封面取色。");
                    refreshPreview.Invoke(control, null);
                    var previewIcon = (SymbolIcon)MediaBarTestAccess.GetField("_artworkHoverPreviewIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
                    Assert.AreSame(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : updatedColor, previewIcon.Foreground);
                    placeholder.Foreground = color;
                    Assert.AreEqual(playing ? SymbolRegular.Pause24 : SymbolRegular.Play24, icon.Symbol);
                    Assert.AreEqual(1d, hint.Opacity);
                    Assert.AreEqual(0, ((SolidColorBrush)hint.Background).Color.A);
                    Assert.AreEqual(0d, scrim.Background.Opacity);
                    Assert.AreEqual(Visibility.Collapsed, placeholder.Visibility);
                    var counterScale = (ScaleTransform)icon.RenderTransform;
                    foreach (var factor in new[] { 1d, 1.6, 2d })
                    {
                        scale.ScaleX = scale.ScaleY = factor;
                        Assert.AreEqual(24d, icon.FontSize * factor * counterScale.ScaleX, .001);
                    }
                    scale.ScaleX = scale.ScaleY = 1.6;
                    control.UpdateSongInfo(snapshot with { Title = "Updated track", Artwork = CreateMarkedArtwork(80, 80), IsPlaying = !playing });
                    Assert.AreEqual(1.6, scale.ScaleX, .001, "快照刷新不能重启动画。");
                    Assert.AreEqual(playing ? SymbolRegular.Play24 : SymbolRegular.Pause24, icon.Symbol);
                    animate.Invoke(control, [true, true]);
                    CaptureArtwork(host, $"zoom-{orientation}-{playing}");
                    refreshPreview.Invoke(control, null);
                    CaptureArtwork((FrameworkElement)popup.Child, $"preview-{orientation}-{playing}");
                    control.UpdateSongInfo(snapshot with { CanPlayPause = false });
                    animate.Invoke(control, [true, true]);
                    Assert.AreEqual(0d, hint.Opacity);
                    SettingsManager.Current.Interaction = SettingsManager.Current.Interaction with { ArtworkClickAction = PlayerClickAction.ActivateSource };
                    control.UpdateSongInfo(snapshot);
                    animate.Invoke(control, [true, true]);
                    Assert.AreEqual(0d, hint.Opacity);
                    refreshPreview.Invoke(control, null);
                    Assert.AreEqual(Visibility.Collapsed, ((Grid)((Border)popup.Child).Child).Children.OfType<Border>().Single().Visibility);
                    SettingsManager.Current.Interaction = SettingsManager.Current.Interaction with { ArtworkClickAction = PlayerClickAction.TogglePlayPause };
                    control.UpdateSongInfo(snapshot);
                    AssertOpenTooltipAvoidsArtwork(control, host);
                    reclaim.Invoke(control, [true]);
                    reclaim.Invoke(control, [true]);
                    Assert.AreSame(canvas, artwork.Parent);
                    Assert.AreEqual(1d, scale.ScaleX);
                    Assert.AreEqual(playing ? 0d : .6, scrim.Background.Opacity);
                    Assert.AreEqual(playing ? Visibility.Collapsed : Visibility.Visible, placeholder.Visibility);
                    Assert.AreEqual(placeholder.FontSize, icon.FontSize);
                    Assert.AreEqual(0d, hint.Opacity);
                    Assert.AreSame(placeholder.Foreground, icon.Foreground);
                }
                SettingsManager.Current.Interaction = SettingsManager.Current.Interaction with { ArtworkHoverMode = ArtworkHoverMode.Preview };
                animate.Invoke(control, [true, true]);
                Assert.IsTrue(popup.IsOpen);
                AssertOpenTooltipAvoidsArtwork(control, (FrameworkElement)popup.Child);
                animate.Invoke(control, [false, true]);
                SettingsManager.Current.Interaction = SettingsManager.Current.Interaction with { ArtworkHoverMode = ArtworkHoverMode.Zoom };
            }
        }
        finally
        {
            reclaim.Invoke(control, [true]);
            SettingsManager.Current.Interaction = interaction;
            window.Close();
            window.Content = null;
            disabledField.SetValue(control, graphicsDisabled);
            control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100);
        }
    }

    private static void AssertOpenTooltipAvoidsArtwork(TaskBarMediaControl control, FrameworkElement artwork)
    {
        var tooltip = (ToolTip)MediaBarTestAccess.GetField("_artworkWheelTooltip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        tooltip.Content = "普通滚轮：上一首 / 下一首";
        // 本夹具显式打开弹层以检查几何，不让真实桌面指针的封面和滚轮轮询收回它。
        var timers = new[] { "_artworkZoomHoverSyncTimer", "_wheelTooltipTimer" }
            .Select(field => (System.Windows.Threading.DispatcherTimer)MediaBarTestAccess.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!)
            .Select(timer => (Timer: timer, WasRunning: timer.IsEnabled)).ToArray();
        foreach (var (timer, _) in timers) timer.Stop();
        try
        {
            tooltip.IsOpen = true;
            var started = Stopwatch.StartNew();
            do
            {
                tooltip.UpdateLayout();
                control.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(10));
            } while ((PresentationSource.FromVisual(tooltip) is null || tooltip.ActualWidth <= 0 || tooltip.ActualHeight <= 0) &&
                     started.Elapsed < TimeSpan.FromSeconds(2));
            Assert.IsTrue(tooltip.IsOpen, "几何断言前提示不能被关闭。");
            Assert.IsNotNull(PresentationSource.FromVisual(tooltip), "提示未在期限内连接到呈现源。");
            Assert.IsNotNull(PresentationSource.FromVisual(artwork), "封面未连接到呈现源。");
            var hint = new Rect(tooltip.PointToScreen(new Point()), tooltip.PointToScreen(new Point(tooltip.ActualWidth, tooltip.ActualHeight)));
            var image = new Rect(artwork.PointToScreen(new Point()), artwork.PointToScreen(new Point(artwork.ActualWidth, artwork.ActualHeight)));
            Assert.IsFalse(hint.IntersectsWith(image), $"提示遮挡了封面：tooltip={hint}, artwork={image}");
        }
        finally
        {
            tooltip.IsOpen = false;
            foreach (var (timer, wasRunning) in timers)
                if (wasRunning) timer.Start();
        }
    }

    private static void VerifyTooltipAvoidance()
    {
        foreach (var dpi in new[] { 1d, 1.5, 2d })
            foreach (var vertical in new[] { false, true })
                foreach (var onLeft in new[] { false, true })
                    foreach (var edge in new[] { new Point(0, 0), new Point(900, 0), new Point(0, 680), new Point(900, 680) })
                    {
                        var work = new Rect(-1000 * dpi, 0, 1000 * dpi, 800 * dpi);
                        var blocked = new Rect((edge.X - 1000) * dpi, edge.Y * dpi, 100 * dpi, 120 * dpi);
                        var size = new Size(210 * dpi, 40 * dpi);
                        var position = TaskBarMediaControl.ResolveArtworkTooltipPosition(size, blocked, work, vertical, onLeft, 8 * dpi);
                        var bounds = new Rect(position, size);
                        Assert.IsTrue(work.Contains(bounds));
                        Assert.IsFalse(bounds.IntersectsWith(blocked), "滚轮提示不能遮挡放大封面。");
                    }
    }

    private static void CaptureArtwork(FrameworkElement root, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AFMB_ARTWORK_HINT_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        root.Measure(new Size(root.Width, root.Height));
        root.Arrange(new Rect(0, 0, root.Width, root.Height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.Width), (int)Math.Ceiling(root.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }

    private static BitmapSource CreateMarkedArtwork(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var color = y < 8 ? Colors.Blue : y >= height - 8 ? Colors.Yellow
                    : x < 8 ? Colors.Red : x >= width - 8 ? Colors.Lime : Colors.White;
                var offset = (y * width + x) * 4;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void AssertArtworkEdgesVisible(Border root, int sourceWidth, int sourceHeight)
    {
        root.Measure(new Size(root.Width, root.Height));
        root.Arrange(new Rect(0, 0, root.Width, root.Height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.Width), (int)Math.Ceiling(root.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        foreach (var color in new[] { Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow })
        {
            var found = false;
            for (var offset = 0; offset < pixels.Length; offset += 4)
                if (Math.Abs(pixels[offset] - color.B) < 30 && Math.Abs(pixels[offset + 1] - color.G) < 30
                    && Math.Abs(pixels[offset + 2] - color.R) < 30 && pixels[offset + 3] > 225)
                {
                    found = true;
                    break;
                }
            Assert.IsTrue(found, $"{sourceWidth}:{sourceHeight} 封面缺少 {color} 边缘标记，存在裁切。");
        }
    }
}
