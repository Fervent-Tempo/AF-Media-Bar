// 宿主拥有的有界性能快照缓存；保留原采样时间，不持有窗口、订阅或计时器。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

/// <summary>让短暂关闭后重新创建的指标视图恢复趋势；单调时间由调用方传入。</summary>
internal sealed class MetricSnapshotHistory
{
    private readonly List<(double Time, SystemMetricsSnapshot Snapshot)> _samples = [];
    private double? _suspendedAt;
    public IReadOnlyList<(double Time, SystemMetricsSnapshot Snapshot)> Samples => _samples;

    /// <summary>停止采样；重复调用保留第一次的时间，不延长保留期。</summary>
    public void Suspend(double time) => _suspendedAt ??= time;

    /// <summary>从第一次停止采样起是否已超过保留期；时钟回退按过期处理。</summary>
    public bool IsExpired(double time) => _suspendedAt is double paused &&
        (time < paused || time - paused >= MetricHistory.RetentionSeconds);

    /// <summary>恢复采样并丢弃窗口外样本，返回停止期是否已过期（此时样本已清空）。</summary>
    public bool Resume(double time)
    {
        var expired = IsExpired(time);
        if (expired) _samples.Clear();
        _samples.RemoveAll(sample => sample.Time < time - MetricHistory.WindowSeconds);
        _suspendedAt = null;
        return expired;
    }

    /// <summary>只追加样本并维护窗口与容量，不改变停止状态；采样只会在 <see cref="Resume"/> 之后到达。</summary>
    public void Add(double time, SystemMetricsSnapshot snapshot)
    {
        if (!double.IsFinite(time)) return;
        if (_samples.Count > 0 && time < _samples[^1].Time) _samples.Clear();
        _samples.Add((time, snapshot));
        _samples.RemoveAll(sample => sample.Time < time - MetricHistory.WindowSeconds);
        if (_samples.Count > MetricHistory.MaximumSamples) _samples.RemoveRange(0, _samples.Count - MetricHistory.MaximumSamples);
    }
}
