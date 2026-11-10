// 竖向文字推进量与紧凑频谱预算；不读取设置、不持有窗口或采样资源。
using System.Globalization;
using System.Text;

namespace AFMediaBar.Classes.Services;

/// <summary>竖向任务栏的纯呈现度量。</summary>
public static class VerticalTaskbarPresentationPolicy
{
    public const double InsetDip = 2;
    public const double PrimaryFontSizeDip = 18;
    public const double SecondaryFontSizeDip = 14;

    public static int SpectrumBandCount(double widthDip, int requested) =>
        Math.Clamp((int)Math.Floor(Math.Max(1, widthDip - 8) / 6), 1, Math.Max(1, requested));

    /// <summary>直立字素逐字推进，侧转的连续西文按同一字体测量。</summary>
    public static double MeasureText(string text, double fontSize, double spacingPercent, Func<string, double> measureSideways)
    {
        var advance = 0d;
        var spacing = fontSize * Math.Max(0, spacingPercent) / 100;
        foreach (var run in TextRuns(text))
            advance += (run.IsSideways ? measureSideways(run.Text) : fontSize) + run.GraphemeCount * spacing;
        return Math.Ceiling(advance + 2 * InsetDip);
    }

    internal static IEnumerable<(string Text, bool IsSideways, int GraphemeCount)> TextRuns(string text)
    {
        var start = 0;
        var sidewaysCount = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var grapheme = elements.GetTextElement();
            if (Rune.TryGetRuneAt(grapheme, 0, out var rune) && rune.Value < 0x2E80 &&
                rune.Value is not (0x2026 or >= 0x2600 and <= 0x27BF) &&
                !grapheme.Contains('\uFE0F') && !grapheme.Contains('\u20E3'))
            {
                if (sidewaysCount == 0) start = elements.ElementIndex;
                sidewaysCount++;
                continue;
            }

            if (sidewaysCount > 0)
                yield return (text[start..elements.ElementIndex], true, sidewaysCount);
            sidewaysCount = 0;
            yield return (grapheme, false, 1);
        }

        if (sidewaysCount > 0)
            yield return (text[start..], true, sidewaysCount);
    }

    public static float[] AggregateSpectrum(ReadOnlySpan<float> source, int count)
    {
        var result = new float[Math.Max(1, count)];
        for (var i = 0; i < result.Length; i++)
        {
            var start = i * source.Length / result.Length;
            var end = (i + 1) * source.Length / result.Length;
            for (var j = start; j < end; j++)
                result[i] = Math.Max(result[i], float.IsFinite(source[j]) ? Math.Clamp(source[j], 0, 1) : 0);
        }
        return result;
    }
}
