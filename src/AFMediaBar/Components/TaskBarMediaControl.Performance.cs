// 只呈现性能快照和指标排列；采样、网卡枚举及计数器生命周期由注入服务负责。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private readonly Dictionary<MetricKind, TextBlock> _performanceCells = [];
    private PerformanceComponentSettings _performanceSettings;
    private SystemMetricsSnapshot _performanceSnapshot;
    private int _performanceCycleIndex;
    private double _performanceWidth = 74;
    private string? _performanceFont;
    private bool _performanceVertical;

    private bool ConfigurePerformance()
    {
        var settings = SettingsManager.Current.PerformanceComponent.Normalize();
        var metrics = settings.GetOrderedMetrics();
        var font = TextElement.GetFontFamily(TaskbarPerformanceItems).Source;
        if (_performanceCells.Count > 0 && _performanceSettings.DisplayMode == settings.DisplayMode &&
            _performanceSettings.GetOrderedMetrics().SequenceEqual(metrics) && _performanceFont == font && _performanceVertical == _isVertical)
            return false;

        _performanceSettings = settings;
        _performanceVertical = _isVertical;
        _performanceFont = font;
        _performanceCells.Clear();
        TaskbarPerformanceItems.Children.Clear();
        foreach (var metric in metrics)
        {
            var cell = new TextBlock
            {
                FontSize = metric == MetricKind.SystemNetwork ? 10 : 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
                Text = metric switch
                {
                    MetricKind.SystemNetwork => "↑ 999.9 GiB/s\n↓ 999.9 GiB/s",
                    MetricKind.ProcessMemory => "APP 99999 MB",
                    _ => MetricPresentationPolicy.Format(metric, new(100, 100, 100, 99999))
                }
            };
            cell.SetResourceReference(TextBlock.FontWeightProperty, "AppTextMediumFontWeight");
            if (metric == MetricKind.SystemNetwork)
            {
                cell.LineHeight = 11;
                cell.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
            TaskbarPerformanceItems.Children.Add(cell);
            cell.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            cell.Width = Math.Ceiling(cell.DesiredSize.Width);
            _performanceCells.Add(metric, cell);
        }

        UpdatePerformanceCells();
        return UpdatePerformanceWidth();
    }

    public void ApplyPerformanceSnapshot(SystemMetricsSnapshot snapshot, int cycleIndex, bool canOpenTaskManager)
    {
        _performanceSnapshot = snapshot;
        _performanceCycleIndex = cycleIndex;
        UpdatePerformanceCells();
        if (_isVertical)
        {
            var verticalCursor = canOpenTaskManager ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
            TaskbarPerformanceSurface.Cursor = verticalCursor;
            TaskbarPerformanceHoverSurface.Cursor = verticalCursor;
            return;
        }
        var changed = false;
        foreach (var cell in _performanceCells.Values)
        {
            var width = cell.Width;
            cell.Width = double.NaN;
            cell.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // DesiredSize 包含 Margin；列间距不能在每次采样时累加到内容宽度。
            cell.Width = Math.Max(width, Math.Ceiling(cell.DesiredSize.Width - cell.Margin.Left - cell.Margin.Right));
            changed |= cell.Width > width;
        }
        if (changed && UpdatePerformanceWidth())
            RefreshDesiredSize();
        var cursor = canOpenTaskManager ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
        TaskbarPerformanceSurface.Cursor = cursor;
        TaskbarPerformanceHoverSurface.Cursor = cursor;
    }

    private void UpdatePerformanceCells()
    {
        var metrics = _performanceSettings.GetOrderedMetrics();
        var current = metrics.Count > 0 ? metrics[Math.Clamp(_performanceCycleIndex, 0, metrics.Count - 1)] : MetricKind.SystemMemory;
        var index = 0;
        foreach (var pair in _performanceCells)
        {
            if (_isVertical)
                ApplyVerticalMetricText(pair.Value, pair.Key);
            else
                pair.Value.Text = MetricPresentationPolicy.Format(pair.Key, _performanceSnapshot);
            pair.Value.Visibility = _performanceSettings.DisplayMode == PerformanceDisplayMode.Parallel || pair.Key == current
                ? Visibility.Visible : Visibility.Collapsed;
            pair.Value.Margin = _isVertical
                ? new Thickness(0, _performanceSettings.DisplayMode == PerformanceDisplayMode.Parallel && index++ > 0 ? 4 : 0, 0, 0)
                : new Thickness(_performanceSettings.DisplayMode == PerformanceDisplayMode.Parallel && index++ > 0 ? 8 : 0, 0, 0, 0);
        }
    }

    private void ApplyVerticalMetricText(TextBlock cell, MetricKind metric)
    {
        var formatted = MetricPresentationPolicy.Format(metric, _performanceSnapshot);
        cell.Inlines.Clear();
        cell.FontSize = 10;
        cell.LineHeight = double.NaN;
        cell.LineStackingStrategy = LineStackingStrategy.MaxHeight;
        cell.Height = metric == MetricKind.SystemNetwork ? 68 : 48;
        cell.Width = Math.Max(1, MainBorder.Width - 8);
        cell.TextTrimming = TextTrimming.CharacterEllipsis;
        if (metric == MetricKind.SystemNetwork)
        {
            foreach (var line in formatted.Split('\n'))
            {
                if (cell.Inlines.Count > 0) cell.Inlines.Add(new LineBreak());
                var parts = line.Split(' ', 3);
                cell.Inlines.Add(new Run(parts.Length > 1 ? parts[0] + " " + parts[1] : line) { FontSize = 13 });
                if (parts.Length > 2)
                {
                    cell.Inlines.Add(new LineBreak());
                    cell.Inlines.Add(new Run(parts[2]) { FontSize = 9 });
                }
            }
            return;
        }
        var values = formatted.Split(' ', 3);
        cell.Inlines.Add(new Run(values[0]) { FontSize = 10 });
        cell.Inlines.Add(new LineBreak());
        cell.Inlines.Add(new Run(values.Length > 1 ? values[1] : "—") { FontSize = 15 });
        if (values.Length > 2)
        {
            cell.Inlines.Add(new LineBreak());
            cell.Inlines.Add(new Run(values[2]) { FontSize = 9 });
        }
    }

    private bool UpdatePerformanceWidth()
    {
        if (_isVertical)
        {
            var length = _performanceSettings.DisplayMode == PerformanceDisplayMode.Parallel
                ? _performanceCells.Keys.Sum(metric => metric == MetricKind.SystemNetwork ? 68d : 48d) + Math.Max(0, _performanceCells.Count - 1) * 4 + 4
                : _performanceCells.ContainsKey(MetricKind.SystemNetwork) ? 72 : 52;
            var changedLength = Math.Abs(length - _verticalPerformanceLength) > 0.1;
            _verticalPerformanceLength = length;
            return changedLength;
        }
        TaskbarPerformanceSurface.Padding = new Thickness(8, 2, 8, 2);
        var width = _performanceCells.Count == 0 ? 74 : _performanceSettings.DisplayMode == PerformanceDisplayMode.Parallel
            ? _performanceCells.Values.Sum(cell => cell.Width) + 8 * (_performanceCells.Count - 1) + 16
            : _performanceCells.Values.Max(cell => cell.Width) + 16;
        width = Math.Max(74, width);
        var changed = Math.Abs(width - _performanceWidth) > 0.1;
        _performanceWidth = width;
        TaskbarPerformanceSurface.Width = width;
        TaskbarPerformanceHoverSurface.Width = width + TaskbarWidgetPadding * 2;
        return changed;
    }
}
