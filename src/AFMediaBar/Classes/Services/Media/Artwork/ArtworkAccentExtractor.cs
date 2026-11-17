using System.Windows.Media;
using System.Windows.Media.Imaging;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 直接从封面图像提取强调色，不经过缩略图缓存，也不依赖任何静态的"最近一次结果"。
/// Extracts an accent colour straight from an artwork image, without the thumbnail cache or any static "latest result".
///
/// 采样口径沿用媒体卡片：先把封面缩到 ≤64 px，再用直方图峰值取主色。<c>darkTheme</c> 交给
/// <see cref="DominantColorCalculator"/>——深色主题下提亮暗色并略作去饱和，浅色主题下保留封面的原始浓淡。
/// Sampling follows the media card: shrink the cover to ≤64 px, then take the histogram peak. The <c>darkTheme</c> flag goes to
/// <see cref="DominantColorCalculator"/>: a dark theme lifts dark colours and slightly desaturates them, a light theme keeps the cover's own depth.
/// </summary>
internal static class ArtworkAccentExtractor
{
    /// <summary>取色时的最大采样边长：64 px 足够得到主色分布，缓冲也只有 16 KB。/ Maximum sampling edge: 64 px is enough for the dominant-colour distribution and keeps the buffer at 16 KB.</summary>
    private const int MaximumSampleSize = 64;

    /// <summary>
    /// 提取封面主色；没有封面、封面全透明或取色失败时返回 null，由调用方回退到应用强调色。
    /// Extracts the cover's dominant colour; returns null without artwork, for a fully transparent cover, or when extraction fails,
    /// leaving the caller to fall back to the application accent.
    /// </summary>
    public static Color? Extract(ImageSource? artwork, bool darkTheme)
    {
        if (artwork is not BitmapSource bitmap)
            return null;

        try
        {
            var sampled = Sample(bitmap, MaximumSampleSize);
            var formatted = new FormatConvertedBitmap(sampled, PixelFormats.Bgra32, null, 0);
            var width = formatted.PixelWidth;
            var height = formatted.PixelHeight;
            if (width <= 0 || height <= 0)
                return null;

            var stride = width * 4;
            var pixels = new byte[height * stride];
            formatted.CopyPixels(pixels, stride, 0);
            var colors = DominantColorCalculator.Calculate(pixels, width, height, 1, darkTheme: darkTheme);
            return colors.Count > 0 ? colors[0] : null;
        }
        catch (Exception)
        {
            // 封面只是可选装饰：任何解码或像素访问异常都不应让媒体快照或强调色发布失败。
            // Artwork is optional decoration: a decoding or pixel-access failure must never fail the media snapshot or the accent publish.
            return null;
        }
    }

    /// <summary>取色前把封面缩到不超过给定边长；已经足够小时直接返回原图。/ Scales the cover down to the given edge length, returning the original when it is already small enough.</summary>
    private static BitmapSource Sample(BitmapSource source, int maximumSize)
    {
        var longestEdge = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longestEdge <= maximumSize)
            return source;

        var scale = maximumSize / (double)longestEdge;
        var transformed = new TransformedBitmap();
        transformed.BeginInit();
        transformed.Source = source;
        transformed.Transform = new ScaleTransform(scale, scale);
        transformed.EndInit();
        transformed.Freeze();
        return transformed;
    }
}
