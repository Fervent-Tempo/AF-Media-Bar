// 共用的图文指标卡片；接收宿主快照，历史由图表持有，不创建采样或计时器。
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Components;

/// <summary>将标签、真实读数和图形放在同一表面；长读数保留单位并在卡片内回退排列。</summary>
public sealed class MetricPresenter : Grid
{
    private readonly Grid[] _lanes = [new(), new()];
    private readonly MetricChart[] _charts = [new(), new()];
    private readonly TextBlock[] _names = [new(), new()];
    private readonly TextBlock[] _values = [new(), new()];
    private readonly TextBlock _measureText = new();
    private MetricKind _metric;
    private MetricVisualStyle _visualStyle;
    private bool _compact = true;
    private bool _vertical;
    private SystemMetricsSnapshot _snapshot;
    public bool ShowHover { get; set; } = true;
    public MetricKind Metric { get => _metric; set { _metric = value; Refresh(); } }
    public MetricVisualStyle VisualStyle { get => _visualStyle; set { _visualStyle = value; Refresh(); } }
    public bool Compact { get => _compact; set { _compact = value; Refresh(); } }
    public bool Vertical { get => _vertical; set { _vertical = value; Refresh(); } }
    public IReadOnlyList<MetricChart> Charts => _charts.Take(Metric == MetricKind.SystemNetwork ? 2 : 1).ToArray();
    public double PreferredWidth => Metric == MetricKind.SystemNetwork ? VisualStyle == MetricVisualStyle.Ring ? 180 : 156
        : Metric == MetricKind.ProcessMemory ? 160 : VisualStyle == MetricVisualStyle.Numeric ? 64 : 80;
    public double PreferredVerticalLength => Metric == MetricKind.SystemNetwork ? 100 : 64;

    public MetricPresenter()
    {
        Background = Brushes.Transparent;
        RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition());
        ColumnDefinitions.Add(new ColumnDefinition()); ColumnDefinitions.Add(new ColumnDefinition());
        for (var index = 0; index < 2; index++)
        {
            var lane = _lanes[index];
            lane.Children.Add(_charts[index]);
            lane.Children.Add(_names[index]); lane.Children.Add(_values[index]);
            foreach (var text in new[] { _names[index], _values[index] })
            {
                text.IsHitTestVisible = false;
                text.TextAlignment = TextAlignment.Center;
                text.VerticalAlignment = VerticalAlignment.Center;
                text.TextWrapping = TextWrapping.Wrap;
            }
            _values[index].SetResourceReference(TextBlock.FontWeightProperty, "AppTextMediumFontWeight");
            Children.Add(lane);
        }
        Unloaded += (_, _) => Reset();
        Refresh();
    }

    public void ApplySnapshot(SystemMetricsSnapshot snapshot)
    {
        _snapshot = snapshot;
        _charts[0].ApplySample(Metric switch
        {
            MetricKind.SystemCpu => snapshot.SystemCpuPercent,
            MetricKind.SystemGpu => snapshot.SystemGpuPercent,
            MetricKind.ProcessMemory => snapshot.ProcessMemoryMegabytes,
            MetricKind.SystemNetwork => snapshot.NetworkUploadBytesPerSecond,
            _ => snapshot.SystemMemoryPercent
        });
        if (Metric == MetricKind.SystemNetwork) _charts[1].ApplySample(snapshot.NetworkDownloadBytesPerSecond);
        Refresh();
    }

    public void ShowPreview()
    {
        foreach (var chart in _charts) chart.ShowPreview();
        ApplySnapshot(new(68, 42, 25, 250, 1740, 36200));
    }
    public void Reset() { foreach (var chart in _charts) chart.Reset(); }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { base.OnRenderSizeChanged(sizeInfo); Refresh(); }
    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    { base.OnMouseEnter(e); if (ShowHover) SetResourceReference(BackgroundProperty, "ControlFillColorSecondaryBrush"); }
    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    { base.OnMouseLeave(e); if (ShowHover) Background = Brushes.Transparent; }

    private void Refresh()
    {
        if (_lanes[0] is null) return;
        var network = Metric == MetricKind.SystemNetwork;
        var sideBySide = network && VisualStyle == MetricVisualStyle.Ring && !Vertical;
        _lanes[1].Visibility = network ? Visibility.Visible : Visibility.Collapsed;
        for (var index = 0; index < 2; index++)
        {
            var lane = _lanes[index]; var chart = _charts[index]; var name = _names[index]; var readout = _values[index];
            SetRow(lane, network && !sideBySide ? index : 0); SetRowSpan(lane, network && !sideBySide ? 1 : 2);
            SetColumn(lane, sideBySide ? index : 0); SetColumnSpan(lane, sideBySide ? 1 : 2);
            var formatted = network ? (index == 0 ? "↑ " + MetricPresentationPolicy.FormatRate(_snapshot.NetworkUploadBytesPerSecond)
                : "↓ " + MetricPresentationPolicy.FormatRate(_snapshot.NetworkDownloadBytesPerSecond))
                : MetricPresentationPolicy.Format(Metric, _snapshot);
            var split = formatted.IndexOf(' ');
            var label = formatted[..Math.Max(0, split)];
            var value = split >= 0 ? formatted[(split + 1)..] : formatted;
            var size = Compact ? network ? 10d : 12d : 15d;
            var plotHeight = Math.Max(1, ActualHeight / (network && !sideBySide ? 2 : 1));
            chart.IsPercentage = Metric is not (MetricKind.ProcessMemory or MetricKind.SystemNetwork);
            chart.VisualStyle = VisualStyle;
            chart.Width = double.NaN; chart.Height = double.NaN; chart.Margin = new Thickness(2);
            chart.Opacity = 1;
            name.FontSize = Compact ? 10 : 11; readout.FontSize = size;
            name.Opacity = SystemParameters.HighContrast ? 1 : 0.78;
            readout.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
            name.Text = label; readout.Text = value;
            name.Margin = new Thickness(4, 0, 4, 0); readout.Margin = new Thickness(4, 0, 4, 0);
            name.HorizontalAlignment = HorizontalAlignment.Left; readout.HorizontalAlignment = HorizontalAlignment.Right;
            name.VerticalAlignment = readout.VerticalAlignment = VerticalAlignment.Center;
            name.Background = readout.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : Brushes.Transparent;
            _measureText.TextWrapping = TextWrapping.NoWrap;
            _measureText.FontFamily = readout.FontFamily;
            _measureText.FontSize = size;
            _measureText.Text = value;
            _measureText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var readoutWidth = _measureText.DesiredSize.Width;
            _measureText.FontSize = name.FontSize; _measureText.Text = label;
            _measureText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var laneWidth = ActualWidth / (sideBySide ? 2 : 1);
            if (!Compact || Vertical || readoutWidth + _measureText.DesiredSize.Width + 12 > laneWidth)
            {
                name.HorizontalAlignment = readout.HorizontalAlignment = HorizontalAlignment.Center;
                name.VerticalAlignment = VerticalAlignment.Top;
                readout.VerticalAlignment = VerticalAlignment.Bottom;
                name.Margin = new Thickness(3, 2, 3, 0); readout.Margin = new Thickness(3, 0, 3, 2);
            }
            if (VisualStyle == MetricVisualStyle.Ring)
            {
                // 竖向卡片为标签和单位预留行高，圆心读数不与折行后的标签重叠。
                var header = 0d;
                if (Vertical)
                {
                    _measureText.FontSize = name.FontSize;
                    _measureText.Text = label + " " + (value.EndsWith('%') ? "%" : value.Contains(' ') ? value[(value.IndexOf(' ') + 1)..] : string.Empty);
                    _measureText.TextWrapping = TextWrapping.Wrap;
                    _measureText.Measure(new Size(Math.Max(1, laneWidth - 6), double.PositiveInfinity));
                    header = _measureText.DesiredSize.Height + 4;
                    _measureText.TextWrapping = TextWrapping.NoWrap;
                }
                var diameter = Math.Min(Math.Min(plotHeight - header - 2, laneWidth - 4), Compact ? 32 : 48);
                chart.Width = chart.Height = Math.Max(8, diameter);
                chart.HorizontalAlignment = HorizontalAlignment.Right; chart.VerticalAlignment = VerticalAlignment.Center;
                var number = value.TrimEnd('%').Split(' ')[0];
                var unit = value.EndsWith('%') ? "%" : value.Contains(' ') ? value[(value.IndexOf(' ') + 1)..] : string.Empty;
                _measureText.Text = number; _measureText.FontSize = size; _measureText.FontFamily = readout.FontFamily;
                _measureText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var numberFits = _measureText.DesiredSize.Width <= diameter - 6;
                _measureText.Text = label + " " + unit; _measureText.FontSize = name.FontSize;
                _measureText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (numberFits && plotHeight >= 22 && (Vertical || _measureText.DesiredSize.Width + diameter + 8 <= laneWidth))
                {
                    name.Text = label + (unit.Length == 0 ? "" : " " + unit);
                    name.VerticalAlignment = Vertical ? VerticalAlignment.Top : VerticalAlignment.Center;
                    name.HorizontalAlignment = Vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                    chart.HorizontalAlignment = Vertical ? HorizontalAlignment.Center : HorizontalAlignment.Right;
                    chart.Margin = Vertical ? new Thickness(2, header, 2, 2) : new Thickness(2);
                    readout.Text = number; readout.HorizontalAlignment = Vertical ? HorizontalAlignment.Center : HorizontalAlignment.Right;
                    readout.VerticalAlignment = VerticalAlignment.Center;
                    readout.Width = Math.Max(1, diameter);
                    readout.Margin = Vertical ? new Thickness(0, header, 0, 2) : new Thickness(0, 0, 2, 0);
                }
                else
                {
                    readout.Width = double.NaN;
                    chart.Opacity = SystemParameters.HighContrast ? 1 : 0.25;
                }
            }
            else
            {
                readout.Width = double.NaN;
                chart.HorizontalAlignment = HorizontalAlignment.Stretch; chart.VerticalAlignment = VerticalAlignment.Stretch;
            }
        }
        var fullName = Metric switch
        {
            MetricKind.SystemMemory => Translations.Get("Media.Metric.SystemMemory.Title"),
            MetricKind.ProcessMemory => Translations.Get("Media.Metric.ProcessMemory.Title"),
            MetricKind.SystemNetwork => Translations.Get("Media.Metric.SystemNetwork.Title"),
            MetricKind.SystemGpu => "GPU",
            _ => "CPU"
        };
        var description = fullName + ": " + MetricPresentationPolicy.Format(Metric, _snapshot);
        if (Metric is MetricKind.ProcessMemory or MetricKind.SystemNetwork && VisualStyle is MetricVisualStyle.Ring or MetricVisualStyle.Bar)
            description += " · " + Translations.Get("Performance.Visual.Relative");
        ToolTip = description;
        AutomationProperties.SetName(this, description);
    }
}
