// 单行统一表达指标开关、样式及排序状态；写入由所属页面协调，无独立订阅。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>关闭的指标也保留在列表；属性刷新不替换行实例。</summary>
public sealed class PerformanceMetricSettingItem : ObservableObject
{
    private readonly ComponentsSettingsViewModel _owner;
    private int _index;
    private int _count;
    internal PerformanceMetricSettingItem(MetricKind metric, ComponentsSettingsViewModel owner)
    {
        Metric = metric;
        _owner = owner;
    }
    public MetricKind Metric { get; }
    public string SearchId => $"metric:bar:{Metric}";
    public string DisplayName => Metric switch
    {
        MetricKind.SystemMemory => Translations.Get("Media.Metric.SystemMemory.Title"),
        MetricKind.ProcessMemory => Translations.Get("Media.Metric.ProcessMemory.Title"),
        MetricKind.SystemNetwork => Translations.Get("Media.Metric.SystemNetwork.Title"),
        MetricKind.SystemGpu => "GPU",
        _ => "CPU"
    };
    public bool IsSelected { get => _owner.HasMetric(Metric); set => _owner.SetMetric(Metric, value); }
    public bool CanToggle => _owner.CanUncheck(Metric);
    public bool CanMoveUp => _index > 0;
    public bool CanMoveDown => _index < _count - 1;
    public MetricVisualStyle VisualStyle
    {
        get => _owner.GetBarMetricVisual(Metric);
        set => _owner.SetMetricVisual(Metric, value, false);
    }
    internal void Refresh(int index, int count)
    {
        _index = index;
        _count = count;
        OnPropertyChanged(string.Empty);
    }
}
