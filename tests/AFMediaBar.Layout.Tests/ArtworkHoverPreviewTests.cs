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
                var popup = (Popup)typeof(TaskBarMediaControl).GetField("_artworkHoverPreview", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(control)!;
                var root = (Border)popup.Child;
                var refresh = typeof(TaskBarMediaControl).GetMethod("RefreshArtworkHoverPreviewContent", BindingFlags.Instance | BindingFlags.NonPublic)!;
                VerifyPlaybackHints(control, refresh);
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

    private static void VerifyPlaybackHints(TaskBarMediaControl control, MethodInfo refreshPreview)
    {
        var placeholder = (SymbolIcon)control.FindName("SongImagePlaceholder");
        var hintIcon = (SymbolIcon)control.FindName("SongImageHoverHintIcon");
        var hint = (Border)control.FindName("SongImageHoverHint");
        var animateHover = typeof(TaskBarMediaControl).GetMethod("AnimateArtworkHover", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previewIcon = (SymbolIcon)typeof(TaskBarMediaControl).GetField("_artworkHoverPreviewIcon", BindingFlags.Instance | BindingFlags.NonPublic)!
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
            Assert.AreSame(placeholder.Foreground, previewIcon.Foreground);
            animateHover.Invoke(control, [false, true]);
        }
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
