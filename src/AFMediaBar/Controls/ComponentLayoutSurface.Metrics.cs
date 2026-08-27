using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.Controls;

internal sealed partial class ComponentLayoutSurface
{
    private FrameworkElement BuildMetrics(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as MetricsWidgetSettings ??
            new MetricsWidgetSettings(MetricKind.SystemMemory, false, 2500, [MetricKind.SystemMemory]);
        var text = new TextBlock
        {
            Tag = BuiltInWidgetTypeIds.Metrics,
            Text = _metricsText,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        SetDynamicResource(text, TextBlock.FontFamilyProperty, "AppTextFontFamily");
        SetDynamicResource(text, TextBlock.FontWeightProperty, "PlayerTextFontWeight");
        SetDynamicResource(text, TextBlock.ForegroundProperty, ResolveContentResourceKey("TaskbarSecondaryTextBrush"));
        var border = new Border
        {
            Width = 74,
            Height = 24,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(12),
            Cursor = settings.OpenTaskManagerOnClick ? Cursors.Hand : Cursors.Arrow,
            Child = text
        };
        SetDynamicResource(border, Border.BackgroundProperty, ResolveContentResourceKey("TaskbarHoverBrush"));
        SetIsInteractiveElement(border, settings.OpenTaskManagerOnClick);
        border.MouseLeftButtonUp += (_, args) =>
        {
            if (!settings.OpenTaskManagerOnClick) return;
            args.Handled = true;
            if (_designMode) return;
            MetricsRequested?.Invoke(this, new LayoutMetricsEventArgs(settings.OpenTaskManagerOnClick));
        };
        _metricStates[widget.InstanceId] = new(text, settings);
        return border;
    }
}
