// 完整面板与设置示例共用的指标网格，只呈现宿主提供的读数。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Components;

/// <summary>三列指标布局，网络在第二行占两列；不拥有采样或订阅。</summary>
public sealed class FullPanelMetricsView : Grid
{
    public IReadOnlyList<MetricPresenter> Presenters { get; }
    public FullPanelMetricsView()
    {
        for (var column = 0; column < 3; column++) ColumnDefinitions.Add(new ColumnDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var kinds = new[] { MetricKind.SystemCpu, MetricKind.SystemMemory, MetricKind.SystemGpu, MetricKind.ProcessMemory, MetricKind.SystemNetwork };
        var presenters = new List<MetricPresenter>();
        for (var index = 0; index < kinds.Length; index++)
        {
            var presenter = new MetricPresenter { Metric = kinds[index], Compact = false, Height = 60,
                Margin = new Thickness(0, 0, index is 2 or 4 ? 0 : 6, index < 3 ? 6 : 0) };
            SetRow(presenter, index < 3 ? 0 : 1);
            SetColumn(presenter, index < 3 ? index : index - 3);
            if (index == 4) SetColumnSpan(presenter, 2);
            Children.Add(presenter); presenters.Add(presenter);
        }
        Presenters = presenters.AsReadOnly();
    }
}
