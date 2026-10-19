// 渲染实际封面进度，验证非方形尺寸、边缘覆盖和非法进度回退；不创建任务栏或媒体服务。
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AFMediaBar.Components;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>封面边缘进度在不同尺寸下保持相同比例，中心不被填充。</summary>
[TestClass]
public sealed class ArtworkProgressTests
{
    [TestMethod]
    public void RemovedHoverProgressDoesNotReserveSpace()
    {
        var controls = TaskbarHoverControlsSettings.Default;
        Assert.AreEqual(
            TaskbarExperiencePolicy.CalculateHoverLayerWidth(controls with { ProgressVisible = false }, false, TaskbarInformationDensity.Balanced),
            TaskbarExperiencePolicy.CalculateHoverLayerWidth(controls with { ProgressVisible = true }, true, TaskbarInformationDensity.Balanced));
    }

    [TestMethod]
    public void ProgressFollowsPerimeterAcrossSizesAndHandlesInvalidValues()
    {
        StaTest.Run(_ =>
        {
            var control = new ArtworkProgress { Stroke = Brushes.Magenta };
            foreach (var size in new[] { new Size(36, 36), new Size(72, 36), new Size(36, 72), new Size(144, 72) })
            {
                var full = CountProgressPixels(control, size, 1);
                var quarter = CountProgressPixels(control, size, 0.25);
                Assert.IsTrue(full > 0);
                Assert.IsTrue(quarter / (double)full is > 0.15 and < 0.35, "进度比例应沿周长计算，不能只按宽或高计算。");
                Assert.IsTrue(full < size.Width * size.Height / 2, "封面中心应保持透明。");
                Assert.AreEqual(full, CountProgressPixels(control, size, 2));
                Assert.AreEqual(0, CountProgressPixels(control, size, -1));
                Assert.AreEqual(0, CountProgressPixels(control, size, double.NaN));
            }
            return Task.CompletedTask;
        });
    }

    private static int CountProgressPixels(ArtworkProgress control, Size size, double progress)
    {
        control.Progress = progress;
        control.Measure(size);
        control.Arrange(new Rect(size));
        control.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(control);
        var pixels = new byte[(int)(size.Width * size.Height) * 4];
        bitmap.CopyPixels(pixels, (int)size.Width * 4, 0);
        return Enumerable.Range(0, pixels.Length / 4).Count(index => pixels[index * 4 + 2] > 80 && pixels[index * 4] > 80);
    }
}
