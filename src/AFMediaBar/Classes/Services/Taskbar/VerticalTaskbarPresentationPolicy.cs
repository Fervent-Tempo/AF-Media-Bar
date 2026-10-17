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
        var count = 0;
        var sideways = new StringBuilder();
        void Flush()
        {
            if (sideways.Length == 0) return;
            advance += measureSideways(sideways.ToString());
            sideways.Clear();
        }
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            count++;
            var rune = Rune.GetRuneAt(element, 0).Value;
            if (rune < 0x2E80 && rune != 0x2026)
                sideways.Append(element);
            else
            {
                Flush();
                advance += fontSize;
            }
        }
        Flush();
        return Math.Ceiling(advance + count * fontSize * Math.Max(0, spacingPercent) / 100 + 4);
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
