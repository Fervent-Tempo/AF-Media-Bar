// 验证无有效封面主色不会生成伪色或沿用上一首；测试拥有临时内存位图并恢复静态缓存状态。
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AFMediaBar.Classes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>透明封面、空直方图与主色缓存切换的回归验证。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ArtworkDominantColorTests
{
    [DataTestMethod]
    [DataRow(1, false)] [DataRow(1, true)] [DataRow(3, false)] [DataRow(3, true)]
    public void TransparentPixelsHaveNoDominantColor(int count, bool dark)
    {
        Assert.AreEqual(0, DominantColorCalculator.Calculate(new byte[16], 2, 2, count, darkTheme: dark).Count);
        Assert.AreEqual(0, DominantColorCalculator.Calculate([], 0, 0, count).Count);
    }

    [DataTestMethod]
    [DataRow((byte)0)] [DataRow((byte)128)] [DataRow((byte)255)]
    public void EmptyWeightedHistogramHasNoInventedPeak(byte gray)
    {
        Assert.AreEqual(0, DominantColorCalculator.Calculate([gray, gray, gray, 255], 1, 1, 1).Count);
        Assert.AreEqual(1, DominantColorCalculator.Calculate([0, 0, 255, 255], 1, 1, 1).Count);
    }

    [TestMethod]
    public void TransparentArtworkClearsPreviousColorAndAllowsThemeFallback()
    {
        StaTest.Run(_ =>
        {
            var cache = (LruCache<int, BitmapImage>)typeof(ArtworkLoader)
                .GetField("ThumbnailCache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var current = typeof(BitmapHelper).GetField("_currentDominantColors", BindingFlags.NonPublic | BindingFlags.Static)!;
            var previous = current.GetValue(null);
            try
            {
                cache.Set(101, Image([0, 0, 255, 255]));
                cache.Set(102, Image([255, 0, 0, 0]));
                Assert.AreEqual(1, BitmapHelper.GetDominantColors(1, 101).Count);
                Assert.AreEqual(0, BitmapHelper.GetDominantColors(1, 102).Count);
                Assert.AreEqual(0, BitmapHelper.SavedDominantColors.Count);
                Assert.AreEqual(0, BitmapHelper.GetDominantColors(1, 102).Count, "Cached absence must stay empty.");
                Assert.AreEqual(2, BitmapHelper.GetDominantColors(1, 0).Count, "Missing artwork still has a safe theme fallback.");
            }
            finally { BitmapHelper.ClearCache(); ArtworkLoader.ClearCache(); current.SetValue(null, previous); }
            return Task.CompletedTask;
        });
    }

    private static BitmapImage Image(byte[] pixels)
    {
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream); stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }
}
