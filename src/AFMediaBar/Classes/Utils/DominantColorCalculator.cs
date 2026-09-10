using System.Windows.Media;

namespace AFMediaBar.Classes.Utils;

/// <summary>
/// 根据 BGRA 像素数据计算封面主色，不依赖缓存、网络或应用资源。
/// Calculates artwork dominant colors from BGRA pixels without cache, network, or application-resource dependencies.
/// </summary>
public static class DominantColorCalculator
{
    /// <summary>
    /// 计算给定 BGRA 像素缓冲区的主色。
    /// Calculates dominant colors for the supplied BGRA pixel buffer.
    /// </summary>
    /// <param name="pixels">按行连续排列的 BGRA32 像素数据。/ Row-major BGRA32 pixel data.</param>
    /// <param name="width">像素宽度。/ Pixel width.</param>
    /// <param name="height">像素高度。/ Pixel height.</param>
    /// <param name="colorCount">需要的颜色数量。/ Number of colors requested.</param>
    /// <param name="maxIterations">K-means 最大迭代次数。/ Maximum K-means iterations.</param>
    /// <param name="darkTheme">是否按深色主题提升暗色并降低饱和度。/ Whether to lift dark colors and reduce saturation for dark theme.</param>
    /// <returns>计算出的不透明颜色；输入无效时返回空集合。单色模式没有有效样本时保留历史直方图回退色。/ Opaque colors, or an empty collection for invalid input. Single-color mode preserves the historical histogram fallback when no valid sample exists.</returns>
    public static IReadOnlyList<Color> Calculate(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int colorCount,
        int maxIterations = 15,
        bool darkTheme = false)
    {
        if (width <= 0 || height <= 0 || colorCount <= 0 || maxIterations < 0 ||
            pixels.Length < (long)width * height * 4)
        {
            return [];
        }

        var pixelCount = checked(width * height);
        var rng = new Random();
        // Small test-sized images need all samples; normal artwork keeps the historical ~10% sampling cost.
        var sampleAllPixels = pixelCount <= 100;
        var samples = new List<int[]>();
        for (var pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
        {
            var offset = pixelIndex * 4;
            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            var a = pixels[offset + 3];

            if (a < 128 || (!sampleAllPixels && rng.Next(10) != 0))
            {
                continue;
            }

            samples.Add([r, g, b]);
        }

        // The former inline histogram returned the center of bin zero when no opaque/sampled
        // pixel existed. Preserve that single-color fallback while multi-color mode stays empty.
        if (samples.Count == 0 && colorCount > 1)
        {
            return [];
        }

        List<Color> result;
        if (colorCount == 1)
        {
            result = [FindHistogramPeak(samples)];
        }
        else
        {
            var clusterCount = Math.Min(colorCount, samples.Count);
            var centroids = samples
                .OrderBy(_ => rng.Next())
                .Take(clusterCount)
                .Select(p => new double[] { p[0], p[1], p[2] })
                .ToList();

            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                var clusters = Enumerable.Range(0, clusterCount)
                    .Select(_ => new List<int[]>())
                    .ToList();

                foreach (var pixel in samples)
                {
                    var best = 0;
                    var bestDistance = double.MaxValue;
                    for (var index = 0; index < clusterCount; index++)
                    {
                        var dr = pixel[0] - centroids[index][0];
                        var dg = pixel[1] - centroids[index][1];
                        var db = pixel[2] - centroids[index][2];
                        var distance = dr * dr + dg * dg + db * db;
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = index;
                        }
                    }

                    clusters[best].Add(pixel);
                }

                var converged = true;
                for (var index = 0; index < clusterCount; index++)
                {
                    if (clusters[index].Count == 0)
                    {
                        continue;
                    }

                    var newR = clusters[index].Average(p => p[0]);
                    var newG = clusters[index].Average(p => p[1]);
                    var newB = clusters[index].Average(p => p[2]);
                    var dr = newR - centroids[index][0];
                    var dg = newG - centroids[index][1];
                    var db = newB - centroids[index][2];
                    if (dr * dr + dg * dg + db * db > 1.0)
                    {
                        converged = false;
                    }

                    centroids[index][0] = newR;
                    centroids[index][1] = newG;
                    centroids[index][2] = newB;
                }

                if (converged)
                {
                    break;
                }
            }

            result = [.. centroids.Select(c => Color.FromArgb(255, (byte)c[0], (byte)c[1], (byte)c[2]))];
        }

        return [.. result.Select(color => AdjustForTheme(color, darkTheme))];
    }

    private static Color FindHistogramPeak(IReadOnlyList<int[]> samples)
    {
        const int quantBits = 4;
        const int bins = 1 << quantBits;
        var histogram = new int[bins * bins * bins];

        foreach (var pixel in samples)
        {
            var r = pixel[0] / 255f;
            var g = pixel[1] / 255f;
            var b = pixel[2] / 255f;
            var max = MathF.Max(r, MathF.Max(g, b));
            var min = MathF.Min(r, MathF.Min(g, b));
            var chroma = max - min;
            var lightness = (max + min) / 2f;
            if (chroma < 0.15f || lightness < 0.15f || lightness > 0.85f)
            {
                continue;
            }

            var weight = chroma * chroma;
            var ri = pixel[0] >> (8 - quantBits);
            var gi = pixel[1] >> (8 - quantBits);
            var bi = pixel[2] >> (8 - quantBits);
            histogram[ri * bins * bins + gi * bins + bi] += (int)(weight * 100);
        }

        var peakIndex = 0;
        for (var index = 1; index < histogram.Length; index++)
        {
            if (histogram[index] > histogram[peakIndex])
            {
                peakIndex = index;
            }
        }

        var peakR = peakIndex / (bins * bins);
        var peakG = (peakIndex / bins) % bins;
        var peakB = peakIndex % bins;
        var halfBin = 1 << (8 - quantBits - 1);
        return Color.FromArgb(
            255,
            (byte)((peakR << (8 - quantBits)) + halfBin),
            (byte)((peakG << (8 - quantBits)) + halfBin),
            (byte)((peakB << (8 - quantBits)) + halfBin));
    }

    private static Color AdjustForTheme(Color color, bool darkTheme)
    {
        var r = ToLinear(color.R);
        var g = ToLinear(color.G);
        var b = ToLinear(color.B);
        if (darkTheme)
        {
            var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            var targetLuminance = Math.Max(luminance, 0.75);
            var scale = targetLuminance / Math.Max(0.0001, luminance);
            r *= scale;
            g *= scale;
            b *= scale;
        }

        const double desaturation = 0.35;
        var newLuminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        r += (newLuminance - r) * desaturation;
        g += (newLuminance - g) * desaturation;
        b += (newLuminance - b) * desaturation;
        return Color.FromArgb(color.A, ToGamma(r), ToGamma(g), ToGamma(b));
    }

    private static double ToLinear(byte value) => Math.Pow(value / 255.0, 2.2);

    private static byte ToGamma(double value) =>
        (byte)Math.Clamp(Math.Pow(value, 1.0 / 2.2) * 255.0, 0, 255);
}
