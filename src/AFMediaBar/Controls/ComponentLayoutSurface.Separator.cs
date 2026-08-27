using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.Controls;

internal sealed partial class ComponentLayoutSurface
{
    private FrameworkElement BuildSeparator(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as SeparatorWidgetSettings ?? new SeparatorWidgetSettings(1, 22);
        var separator = new Border
        {
            Width = Math.Clamp(settings.ThicknessDip, 1, 8),
            Height = Math.Clamp(settings.LengthDip, 4, 256),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };
        SetDynamicResource(separator, Border.BackgroundProperty, ResolveContentResourceKey("TaskbarDividerBrush"));
        return separator;
    }
}
