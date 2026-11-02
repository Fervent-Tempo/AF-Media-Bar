// 呈现共享性能快照；指标卡片在主布局中独立排列，不创建采样器或计时器。
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private readonly Dictionary<MetricKind, MetricPresenter> _metricPresenters = [];
    private readonly Dictionary<MetricKind, Border> _metricSurfaces = [];
    private IReadOnlyList<MetricKind> _performanceMetrics = [];
    private PerformanceComponentSettings _performanceSettings;
    private int _performanceCycleIndex;
    private double _performanceWidth = 80;
    private long? _performanceHistoryHiddenAt;
    private double PreferredPerformanceHeight => 28;
    public PerformanceComponentSettings EffectivePerformanceSettings => PresentationSettings.PerformanceComponent with
    {
        Metrics = PresentationSettings.PerformanceComponent.DisplayMode == PerformanceDisplayMode.Parallel
            ? EffectiveRestEntries().Where(entry => entry.IsVisible && ComponentLayoutPolicy.IsMetric(entry.Component))
                .Select(entry => ComponentLayoutPolicy.Metric(entry.Component)).ToArray()
            : _performanceMetrics
    };

    private bool ConfigurePerformance()
    {
        var settings = PresentationSettings.PerformanceComponent.Normalize();
        var changed = _performanceSettings != settings;
        _performanceSettings = settings;
        _performanceMetrics = settings.GetOrderedMetrics();
        foreach (var metric in Enum.GetValues<MetricKind>())
        {
            if (!_metricPresenters.TryGetValue(metric, out var presenter))
            {
                presenter = new MetricPresenter { Metric = metric, ShowHover = false, IsHitTestVisible = false };
                var surface = new Border
                {
                    Name = "Metric" + metric + "HoverSurface",
                    Child = presenter,
                    CornerRadius = new CornerRadius(5),
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderBrush = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0, 1.25, 0, 0),
                    Padding = new Thickness(1, 1, 1, 2.25),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed,
                    Tag = ComponentLayoutPolicy.Component(metric)
                };
                surface.MouseEnter += (sender, _) => AnimateComponentHover((Border)sender, true);
                surface.MouseLeave += (sender, _) => AnimateComponentHover((Border)sender, false);
                Panel.SetZIndex(surface, 4);
                ((Panel)TaskbarPerformanceHoverSurface.Parent).Children.Add(surface);
                _metricPresenters.Add(metric, presenter); _metricSurfaces.Add(metric, surface);
                changed = true;
            }
            presenter.VisualStyle = settings.Visuals.Get(metric);
            presenter.Vertical = _isVertical;
            presenter.Compact = true;
        }
        var width = _performanceMetrics.Select(metric => _metricPresenters[metric].PreferredWidth + TaskbarWidgetPadding * 2).DefaultIfEmpty(80).Max();
        changed |= Math.Abs(width - _performanceWidth) > .01;
        _performanceWidth = width;
        _verticalPerformanceLength = _performanceMetrics.Select(metric => _metricPresenters[metric].PreferredVerticalLength).DefaultIfEmpty(64).Max();
        TaskbarPerformanceSurface.Width = width;
        TaskbarPerformanceSurface.Height = PreferredPerformanceHeight;
        return changed;
    }

    public void ApplyPerformanceSnapshot(SystemMetricsSnapshot snapshot, int cycleIndex, bool canOpenTaskManager)
    {
        _performanceCycleIndex = cycleIndex;
        var active = UpdatePerformanceHistoryVisibility(IsPerformanceComponentVisible);
        foreach (var pair in _metricPresenters)
        {
            if (active && EffectivePerformanceSettings.Metrics!.Contains(pair.Key)) pair.Value.ApplySnapshot(snapshot);
            var surface = _metricSurfaces[pair.Key];
            surface.ToolTip = pair.Value.ToolTip;
            System.Windows.Automation.AutomationProperties.SetName(surface, System.Windows.Automation.AutomationProperties.GetName(pair.Value));
            surface.Cursor = canOpenTaskManager ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
        }
        if (_isVertical) ApplyVerticalSectionGeometry(MainBorder.Height);
        else ApplyTaskbarSectionGeometry(MainBorder.Width);
    }

    private void PlaceMetricSurfaces(TaskbarRestLayout layout, double crossSize)
    {
        var parallel = _performanceSettings.DisplayMode == PerformanceDisplayMode.Parallel;
        var current = _performanceMetrics.Count == 0 ? MetricKind.SystemMemory : _performanceMetrics[Math.Clamp(_performanceCycleIndex, 0, _performanceMetrics.Count - 1)];
        foreach (var pair in _metricSurfaces)
        {
            if (IsDetachedElement(pair.Value)) continue;
            var component = parallel ? ComponentLayoutPolicy.Component(pair.Key) : TaskbarRestComponent.Performance;
            var placement = parallel || pair.Key == current ? layout.Find(component) : null;
            var surface = pair.Value;
            surface.Tag = component;
            if (placement is not { } position)
            {
                if (surface.Visibility == Visibility.Visible) AnimateComponentHover(surface, false, immediate: true);
                SetRestComponentVisible(surface, false);
                continue;
            }
            SetRestComponentVisible(surface, true);
            if (_isVertical)
            {
                surface.Width = crossSize; surface.Height = position.Width;
                surface.VerticalAlignment = VerticalAlignment.Top;
                surface.Margin = new Thickness(2, position.Left, 0, 0);
            }
            else
            {
                surface.VerticalAlignment = VerticalAlignment.Center;
                surface.Height = crossSize;
                surface.Width = position.Width;
                surface.Margin = new Thickness(position.Left, 0, 0, 0);
            }
        }
        TaskbarPerformanceHoverSurface.Visibility = Visibility.Collapsed;
        TaskbarPerformanceSurface.Visibility = Visibility.Collapsed;
    }
    /// <summary>
    /// 记录趋势历史在"继续采样"与"停止采样"之间的切换，并返回这次是否应继续采样。
    /// 组件隐藏、宿主挂起、显示关闭/睡眠剪枝都只暂停采样；停止时间超过保留期后在恢复时清空历史，不创建计时器。
    /// </summary>
    private bool UpdatePerformanceHistoryVisibility(bool componentVisible)
    {
        if (!componentVisible || _isHostVisibilitySuspended || _backgroundPruneLevel >= MemoryPruneLevel.DisplayOff)
        {
            _performanceHistoryHiddenAt ??= Stopwatch.GetTimestamp();
            return false;
        }
        // 短暂无媒体或短暂隐藏只保留有界历史，恢复后仍从同一时间轴续画。
        if (_performanceHistoryHiddenAt is long hiddenAt && Stopwatch.GetElapsedTime(hiddenAt).TotalSeconds >= MetricHistory.RetentionSeconds)
            ClearPerformanceHistory();
        _performanceHistoryHiddenAt = null;
        return true;
    }

    private void ClearPerformanceHistory()
    {
        foreach (var presenter in _metricPresenters.Values) presenter.Reset();
        _performanceHistoryHiddenAt = null;
    }
}
