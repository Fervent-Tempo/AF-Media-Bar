// 已选性能指标的排序行；命令和持久化由所属页面 ViewModel 协调。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>显示名与排序边界随页面语言和当前选择重建。</summary>
public sealed record PerformanceMetricSettingItem(MetricKind Metric, string DisplayName, bool CanMoveUp, bool CanMoveDown);
