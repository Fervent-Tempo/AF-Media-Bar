// 单个呈现会话的有界历史；调用方传入单调时间，隐藏后清空，不持有计时器或采样资源。
namespace AFMediaBar.Classes.Services;

/// <summary>维护一分钟趋势与相对标尺；缺失样本保留为断点。</summary>
public sealed class MetricHistory
{
    public const double WindowSeconds = 60;
    public const int MaximumSamples = 128;
    private readonly List<(double Time, double? Value)> _samples = [];
    public IReadOnlyList<(double Time, double? Value)> Samples => _samples;

    public void Add(double time, double? value)
    {
        if (!double.IsFinite(time)) return;
        if (_samples.Count > 0 && time < _samples[^1].Time) _samples.Clear();
        _samples.Add((time, value is double number && double.IsFinite(number) && number >= 0 ? number : null));
        _samples.RemoveAll(sample => sample.Time < time - WindowSeconds);
        if (_samples.Count > MaximumSamples) _samples.RemoveRange(0, _samples.Count - MaximumSamples);
    }

    public double GetMaximum(bool percentage) => percentage ? 100 : Math.Max(1, _samples.Select(sample => sample.Value ?? 0).DefaultIfEmpty(0).Max());
    public void Clear() => _samples.Clear();
}
