// 真实控件遍历六种频谱样式：视觉分支、内容宽度、渲染、计时器生命周期与主题降级。
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests.Presentation;

[TestClass]
[DoNotParallelize]
public sealed class SpectrumStylePresentationChecks
{
    [TestMethod]
    public void EveryStyleBuildsItsOwnVisualsAndStopsWhenHidden()
    {
        StaTest.Run(async _ =>
        {
            var previous = SettingsManager.Current;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            TaskBarMediaControl? control = null;
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
                SettingsManager.Current = new AppSettings
                {
                    TaskbarExperience = new()
                    {
                        SpectrumVisible = true,
                        PerformanceVisible = false,
                        MediaFontSizePercent = 100
                    }
                };
                control = new TaskBarMediaControl { AllowRestConnectionTransition = false };
                control.UpdateSongInfo(MediaSnapshot.Disconnected with { IsConnected = true, Title = "Spectrum" });

                foreach (var orientation in new[] { LayoutOrientation.Horizontal, LayoutOrientation.Vertical })
                {
                    var horizontal = orientation == LayoutOrientation.Horizontal;
                    control.ApplyLayout(WindowMode.Taskbar, orientation, 100, 100, horizontal ? 44 : 48);
                    control.ApplyPrimaryLength(600);
                    if (!horizontal)
                    {
                        control.Width = 48;
                        control.Height = 600;
                    }

                    foreach (var style in Enum.GetValues<SpectrumStyle>())
                        foreach (var requestedCount in new[] { 9, 14, 24 })
                        {
                            var bandCount = horizontal ? requestedCount : Math.Min(16, requestedCount);
                            SettingsManager.Current.SpectrumComponent = SettingsManager.Current.SpectrumComponent with
                            {
                                Style = style,
                                BandCount = requestedCount,
                                ContentHeightDip = 26,
                                SensitivityPercent = 100,
                                ShowPeakMarkers = true
                            };
                            control.ApplyTaskbarExperienceSettings();
                            var timer = FrameTimer(control);
                            Assert.IsFalse(timer?.IsEnabled ?? false, $"{style}/{orientation} 重建后应停表。");

                            var bands = Enumerable.Range(0, bandCount)
                                .Select(index => (float)(.12 + .76 * Math.Abs(Math.Sin(index * .55)))).ToArray();
                            control.ApplySpectrum(bands);
                            Layout(control, horizontal);
                            var spectrum = (Canvas)control.FindName("TaskbarSpectrum");
                            Assert.AreEqual(SpectrumPresentationPolicy.CalculateContentWidthDip(bandCount), spectrum.Width, 0.01,
                                $"{style}/{orientation}/{bandCount} 的内容宽度应来自统一策略。");
                            AssertStyle(spectrum, style, bandCount, spectrum.Height);
                            var bitmap = new RenderTargetBitmap(horizontal ? 600 : 48, horizontal ? 44 : 600,
                                96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(control);
                        }
                }

                // 动效开启时面积样式应留下计时器；重建/卸载/隐藏必须把它停掉。
                SettingsManager.Current.SpectrumComponent = SettingsManager.Current.SpectrumComponent with
                { Style = SpectrumStyle.Area, BandCount = 14, ContentHeightDip = 26 };
                control.ApplyTaskbarExperienceSettings();
                control.ApplySpectrum(Enumerable.Repeat(.5f, 14).ToArray());
                var running = FrameTimer(control);
                Assert.IsTrue(running?.IsEnabled ?? false, "面积样式在动效开启时应运行逐帧计时器。");

                SettingsManager.Current.SpectrumComponent = SettingsManager.Current.SpectrumComponent with { Style = SpectrumStyle.Bars };
                control.ApplyTaskbarExperienceSettings();
                Assert.IsFalse(FrameTimer(control)?.IsEnabled ?? false, "样式重建后应停止旧计时器。");

                SettingsManager.Current.SpectrumComponent = SettingsManager.Current.SpectrumComponent with { Style = SpectrumStyle.Area };
                control.ApplyTaskbarExperienceSettings();
                control.ApplySpectrum(Enumerable.Repeat(.5f, 14).ToArray());
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Assert.IsFalse(FrameTimer(control)?.IsEnabled ?? false, "Unloaded 后应停止逐帧计时器。");

                control.ApplySpectrum(ReadOnlySpan<float>.Empty);
                Assert.IsFalse(FrameTimer(control)?.IsEnabled ?? false, "隐藏/清空后不能留下逐帧计时器。");
            }
            finally
            {
                control?.ApplyPresentationSettings(null!);
                SettingsManager.Current = previous;
                app.Shutdown();
            }
        });
    }

    [TestMethod]
    public void HighContrastAndLowPerformanceRemoveDecorations()
    {
        StaTest.Run(async _ =>
        {
            var bands = Enumerable.Repeat(.5f, 14).ToArray();
            var highContrast = SpectrumVisualThemePolicy.Resolve(highContrast: true, useDecorativeEffects: false);
            var lowPerformance = SpectrumVisualThemePolicy.Resolve(highContrast: false, useDecorativeEffects: false);

            var areaCanvas = new Canvas();
            var area = new SpectrumVisualSet(areaCanvas, SpectrumStyle.Area, 14, 26, Brushes.White, highContrast, 100,
                showPeakMarkers: true);
            area.ApplyLevels(bands, animate: false);
            area.ApplyPeaks(bands);
            var areaPath = areaCanvas.Children.OfType<Path>().Single();
            Assert.IsNull(areaPath.OpacityMask, "高对比度面积曲线不能使用透明度遮罩。");

            var waveformCanvas = new Canvas();
            var waveform = new SpectrumVisualSet(waveformCanvas, SpectrumStyle.Waveform, 14, 26, Brushes.White, lowPerformance, 100,
                showPeakMarkers: true);
            waveform.ApplyLevels(bands, animate: false);
            waveform.ApplyPeaks(bands);
            Assert.IsNull(waveformCanvas.Children.OfType<Path>().Single().OpacityMask,
                "低性能波形不能使用透明度遮罩。");

            var barsCanvas = new Canvas();
            var bars = new SpectrumVisualSet(barsCanvas, SpectrumStyle.Bars, 14, 26, Brushes.White, highContrast, 100,
                showPeakMarkers: true);
            bars.ApplyLevels(bands, animate: false);
            bars.ApplyPeaks(bands);
            var barVisuals = barsCanvas.Children.OfType<Border>().Where(border => border.RenderTransform is ScaleTransform).ToArray();
            Assert.AreEqual(14, barVisuals.Length);
            Assert.IsTrue(barVisuals.All(bar => Math.Abs(bar.Opacity - 1) < 0.0001), "高对比度柱体必须全不透明。");
            Assert.IsTrue(barsCanvas.Children.OfType<Border>()
                .Where(border => border.RenderTransform is TranslateTransform)
                .All(cap => cap.Opacity == 0), "高对比度不能显示峰值帽。");

            var mutedCanvas = new Canvas();
            var mutedBars = new SpectrumVisualSet(mutedCanvas, SpectrumStyle.Bars, 14, 26, Brushes.White,
                SpectrumVisualThemePolicy.Resolve(false, true), 100, showPeakMarkers: false);
            mutedBars.ApplyLevels(bands, animate: false);
            mutedBars.ApplyPeaks(bands);
            Assert.IsTrue(mutedCanvas.Children.OfType<Border>()
                .Where(border => border.RenderTransform is TranslateTransform)
                .All(cap => cap.Opacity == 0), "默认关闭时不能显示峰值帽。");

            var pixelsCanvas = new Canvas();
            var pixels = new SpectrumVisualSet(pixelsCanvas, SpectrumStyle.PixelBars, 14, 26, Brushes.White, highContrast, 100,
                showPeakMarkers: true);
            pixels.ApplyLevels(new float[14], animate: false);
            var pixelBorders = pixelsCanvas.Children.OfType<Border>().ToArray();
            var dotCount = SpectrumPresentationPolicy.ResolvePixelDotCount(26);
            for (var band = 0; band < 14; band++)
            {
                for (var dot = 0; dot < dotCount; dot++)
                    Assert.AreEqual(dot == 0 ? 1 : 0, pixelBorders[band * (dotCount + 1) + dot].Opacity,
                        "高对比度下只保留底部最小值，未点亮像素必须隐藏。");
                Assert.AreEqual(0, pixelBorders[band * (dotCount + 1) + dotCount].Opacity, "高对比度不能显示像素峰值块。");
            }
        });
    }

    private static void Layout(TaskBarMediaControl control, bool horizontal)
    {
        var width = horizontal ? 600 : 48;
        var height = horizontal ? 44 : 600;
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
        control.UpdateLayout();
    }

    private static void AssertStyle(Canvas spectrum, SpectrumStyle style, int bandCount, double height)
    {
        var children = spectrum.Children.Cast<UIElement>().ToArray();
        switch (style)
        {
            case SpectrumStyle.Bars:
                Assert.AreEqual(bandCount, children.OfType<Border>().Count(border => border.RenderTransform is ScaleTransform),
                    "柱状图柱数");
                Assert.AreEqual(bandCount, children.OfType<Border>().Count(border => border.RenderTransform is TranslateTransform),
                    "柱状图峰值帽数");
                break;
            case SpectrumStyle.MirroredBars:
                Assert.AreEqual(bandCount, children.OfType<Border>().Count(border => border.RenderTransform is ScaleTransform),
                    "对称柱状图柱数");
                Assert.AreEqual(bandCount * 2, children.OfType<Border>().Count(border => border.RenderTransform is TranslateTransform),
                    "对称柱状图上下峰值帽数");
                break;
            case SpectrumStyle.PixelBars:
                var blocks = SpectrumPresentationPolicy.ResolvePixelDotCount(height) * bandCount + bandCount;
                Assert.AreEqual(blocks, children.OfType<Border>().Count(), "像素柱状图方块与峰值块数");
                break;
            case SpectrumStyle.Waveform:
            case SpectrumStyle.Area:
                var path = children.OfType<Path>().Single();
                Assert.IsNotNull(path.Data, $"{style} 应有几何数据。");
                var bounds = path.Data.Bounds;
                Assert.IsTrue(bounds.Left >= -0.01 && bounds.Right <= spectrum.Width + 0.01, $"{style} 横向越界");
                Assert.IsTrue(bounds.Top >= -0.01 && bounds.Bottom <= spectrum.Height + 0.01, $"{style} 纵向越界");
                break;
            case SpectrumStyle.Dots:
                Assert.AreEqual(bandCount * 2, children.OfType<Ellipse>().Count(), "光点与峰值点数");
                break;
            default:
                Assert.Fail($"未知样式 {style}");
                break;
        }
    }

    private static DispatcherTimer? FrameTimer(TaskBarMediaControl control) =>
        (DispatcherTimer?)typeof(TaskBarMediaControl)
            .GetField("_spectrumFrameTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control);
}
