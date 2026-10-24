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
    private IReadOnlyList<MetricKind> _performanceMetrics = [];
    private PerformanceComponentSettings _performanceSettings;
    private SystemMetricsSnapshot _performanceSnapshot;
    private int _performanceCycleIndex;
    private double _performanceWidth = 74;
    private string? _performanceFont;
    private bool _performanceVertical;
    private bool _performanceCompact;
    private double PreferredPerformanceHeight => !_performanceCompact && _performanceMetrics.Contains(MetricKind.SystemNetwork) ? 26 : 24;

    private bool ConfigurePerformance()
    {
        var settings = PresentationSettings.PerformanceComponent.Normalize();
        var compact = IsBarPresentation && !_isVertical && MainBorder.Height <
            26 + RestWidgetVerticalInset(TaskbarPerformanceHoverSurface) + _textBudget.PaddingDip * 2;
        var metrics = settings.GetOrderedMetrics();
        var font = TextElement.GetFontFamily(TaskbarPerformanceItems).Source;
        if (_performanceCells.Count > 0 && _performanceSettings.DisplayMode == settings.DisplayMode &&
            _performanceMetrics.SequenceEqual(metrics) && _performanceFont == font && _performanceVertical == _isVertical && _performanceCompact == compact)
            return false;

        _performanceSettings = settings;
        _performanceMetrics = metrics;
        _performanceVertical = _isVertical;
        _performanceCompact = compact;
        _performanceFont = font;
        _performanceCells.Clear();
        TaskbarPerformanceItems.Children.Clear();
        foreach (var metric in metrics)
        {
            var cell = new TextBlock
            {
                FontSize = metric == MetricKind.SystemNetwork ? 10 : 12,
                VerticalAlignment = VerticalAlignment.Center,
                // 网速横向两行（↑ / ↓）必须左对齐：cell 宽度按最大占位固定后只增不减，两行各自居中的话
                // 短行的箭头会随速率左右漂移，和长行的箭头对不齐。其余指标是单行，居中不变。
                // The horizontal network cell (two lines, up/down) must be left-aligned: its width is pinned by the
                // widest placeholder and never shrinks, so centering each line would let the short line's arrow drift
                // with the rate and miss the long line's arrow. The other metrics are single-line and stay centered.
                TextAlignment = metric == MetricKind.SystemNetwork && !compact ? TextAlignment.Left : TextAlignment.Center,
                IsHitTestVisible = false,
                Text = metric switch
                {
                    MetricKind.SystemNetwork => compact ? "↑ 999.9 GiB/s · ↓ 999.9 GiB/s" : "↑ 999.9 GiB/s\n↓ 999.9 GiB/s",
                    MetricKind.ProcessMemory => "APP 99999 MB",
                    _ => MetricPresentationPolicy.Format(metric, new(100, 100, 100, 99999))
                }
            };
            cell.SetResourceReference(TextBlock.FontWeightProperty, "AppTextMediumFontWeight");
            if (metric == MetricKind.SystemNetwork && !compact)
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
        var metrics = _performanceMetrics;
        var current = metrics.Count > 0 ? metrics[Math.Clamp(_performanceCycleIndex, 0, metrics.Count - 1)] : MetricKind.SystemMemory;
        var index = 0;
        foreach (var pair in _performanceCells)
        {
            if (_isVertical)
                ApplyVerticalMetricText(pair.Value, pair.Key);
            else
                pair.Value.Text = _performanceCompact
                    ? MetricPresentationPolicy.Format(pair.Key, _performanceSnapshot).Replace("\n", " · ")
                    : MetricPresentationPolicy.Format(pair.Key, _performanceSnapshot);
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
        // 每个指标始终占两行；复用文本元素，避免每次采样重建 TextContainer 的内容树。
        if (cell.Inlines.Count != 3 || cell.Inlines.FirstInline is not Run firstLine || cell.Inlines.LastInline is not Run secondLine)
        {
            cell.Inlines.Clear();
            firstLine = new Run();
            secondLine = new Run();
            cell.Inlines.Add(firstLine);
            cell.Inlines.Add(new LineBreak());
            cell.Inlines.Add(secondLine);
        }
        cell.FontSize = 10;
        cell.LineHeight = 16;
        cell.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        cell.Height = 32;
        cell.Width = Math.Max(1, MainBorder.Width - 8);
        cell.TextTrimming = TextTrimming.None;
        cell.TextWrapping = TextWrapping.NoWrap;
        void UpdateLine(Run line, string text, double size)
        {
            var measured = MeasureTextWidthExact(text, cell) * size / cell.FontSize;
            var fitted = measured > cell.Width ? size * cell.Width / measured : size;
            line.Text = text;
            line.FontSize = Math.Max(1, fitted);
        }
        if (metric == MetricKind.SystemNetwork)
        {
            var lines = formatted.Split('\n');
            UpdateLine(firstLine, lines[0], 11);
            UpdateLine(secondLine, lines[1], 11);
            return;
        }
        var values = formatted.Split(' ', 3);
        UpdateLine(firstLine, values.Length > 2 ? values[0] + " " + values[2] : values[0], 10);
        UpdateLine(secondLine, values.Length > 1 ? values[1] : "—", 14);
    }

    private bool UpdatePerformanceWidth()
    {
        if (_isVertical)
        {
            var count = _performanceSettings.DisplayMode == PerformanceDisplayMode.Parallel ? Math.Max(1, _performanceCells.Count) : 1;
            var length = Math.Ceiling(count * 32d + (count - 1) * 4 + 2 + RestWidgetVerticalInset(TaskbarPerformanceHoverSurface));
            var changedLength = Math.Abs(length - _verticalPerformanceLength) > 0.1;
            _verticalPerformanceLength = length;
            return changedLength;
        }
        TaskbarPerformanceSurface.Height = PreferredPerformanceHeight;
        TaskbarPerformanceSurface.Padding = _performanceCompact ? new Thickness(4, 0, 4, 0) : new Thickness(8, 2, 8, 2);
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
