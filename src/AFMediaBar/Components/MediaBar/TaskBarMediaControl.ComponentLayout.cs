// 将当前状态投影到同一布局策略；实际排列、长度、命中及拖动都以此结果为准。
using System.Windows;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private void ResizeResidentTransportButtons(double width, double height)
    {
        foreach (var button in new[] { ResidentPreviousButton, ResidentPlayPauseButton, ResidentNextButton })
        {
            button.Width = width;
            button.Height = height;
        }
    }

    private BarLayoutState RestLayoutState => _isConnected ? BarLayoutState.Resident : BarLayoutState.Idle;
    private IReadOnlyList<ComponentLayoutEntry> EffectiveRestEntries() => ComponentLayoutPolicy.Resolve(PresentationSettings, RestLayoutState);
    private TaskbarRestComponent[] EffectiveRestComponents() => PreviewRestEntries()
        .Where(entry => entry.IsVisible && (entry.Component != TaskbarRestComponent.BluetoothBattery || _batteryCanShow))
        .Select(entry => entry.Component).ToArray();
    private TaskbarRestComponent[] RestComponentsWithHandle()
    {
        var visible = EffectiveRestComponents();
        return visible.Length == 0 ? [] : [.. visible, TaskbarRestComponent.DragHandle];
    }
    private double MetricPrimarySize(TaskbarRestComponent component, bool vertical) =>
        _metricPresenters.TryGetValue(ComponentLayoutPolicy.Metric(component), out var presenter)
            ? vertical ? presenter.PreferredVerticalLength : presenter.PreferredWidth + TaskbarWidgetPadding * 2 : 80;
}
