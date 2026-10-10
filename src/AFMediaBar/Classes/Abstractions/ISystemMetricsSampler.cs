// 性能监视器的采样边界；平台实现拥有计数器，监视器串行调用并在停止时清空需求。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>按需求采样性能指标并重置跨周期状态。</summary>
public interface ISystemMetricsSampler
{
    void SetDemand(IReadOnlyCollection<MetricKind> metrics);
    SystemMetricsSnapshot Sample();
    void Reset();
}
