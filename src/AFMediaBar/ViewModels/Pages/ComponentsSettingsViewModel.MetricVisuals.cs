// 两种表面独立编辑样式，使用现有上下文与持久化通知。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

public partial class ComponentsSettingsViewModel
{
    public MetricVisualStyle BarSystemMemoryVisual
    {
        get => _configuration.Current.PerformanceComponent.Visuals.SystemMemory;
        set => SetMetricVisual(MetricKind.SystemMemory, value, false);
    }
    public MetricVisualStyle BarSystemCpuVisual
    {
        get => _configuration.Current.PerformanceComponent.Visuals.SystemCpu;
        set => SetMetricVisual(MetricKind.SystemCpu, value, false);
    }
    public MetricVisualStyle BarSystemGpuVisual
    {
        get => _configuration.Current.PerformanceComponent.Visuals.SystemGpu;
        set => SetMetricVisual(MetricKind.SystemGpu, value, false);
    }
    public MetricVisualStyle BarProcessMemoryVisual
    {
        get => _configuration.Current.PerformanceComponent.Visuals.ProcessMemory;
        set => SetMetricVisual(MetricKind.ProcessMemory, value, false);
    }
    public MetricVisualStyle BarSystemNetworkVisual
    {
        get => _configuration.Current.PerformanceComponent.Visuals.SystemNetwork;
        set => SetMetricVisual(MetricKind.SystemNetwork, value, false);
    }
    public MetricVisualStyle PanelSystemMemoryVisual
    {
        get => _configuration.Current.TaskbarExperience.FullPanel.PerformanceVisuals.SystemMemory;
        set => SetMetricVisual(MetricKind.SystemMemory, value, true);
    }
    public MetricVisualStyle PanelSystemCpuVisual
    {
        get => _configuration.Current.TaskbarExperience.FullPanel.PerformanceVisuals.SystemCpu;
        set => SetMetricVisual(MetricKind.SystemCpu, value, true);
    }
    public MetricVisualStyle PanelSystemGpuVisual
    {
        get => _configuration.Current.TaskbarExperience.FullPanel.PerformanceVisuals.SystemGpu;
        set => SetMetricVisual(MetricKind.SystemGpu, value, true);
    }
    public MetricVisualStyle PanelProcessMemoryVisual
    {
        get => _configuration.Current.TaskbarExperience.FullPanel.PerformanceVisuals.ProcessMemory;
        set => SetMetricVisual(MetricKind.ProcessMemory, value, true);
    }
    public MetricVisualStyle PanelSystemNetworkVisual
    {
        get => _configuration.Current.TaskbarExperience.FullPanel.PerformanceVisuals.SystemNetwork;
        set => SetMetricVisual(MetricKind.SystemNetwork, value, true);
    }
    private void SetMetricVisual(MetricKind metric, MetricVisualStyle style, bool panel)
    {
        if (_disposed || !_configuration.IsActive) return;
        if (panel)
        {
            var experience = _configuration.Current.TaskbarExperience;
            _configuration.SetTaskbarExperience(experience with
            {
                FullPanel = experience.FullPanel with { PerformanceVisuals = experience.FullPanel.PerformanceVisuals.With(metric, style) }
            });
        }
        else
        {
            var current = _configuration.Current.PerformanceComponent;
            _configuration.SetPerformance(current with { Visuals = current.Visuals.With(metric, style) });
        }
        OnPropertyChanged(string.Empty);
    }
}
