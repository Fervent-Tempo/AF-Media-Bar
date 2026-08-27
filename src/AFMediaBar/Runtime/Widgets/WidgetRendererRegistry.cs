using System.Windows;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.Controls;

/// <summary>
/// In-process renderer dispatch. It keeps the surface independent from a
/// growing type switch while the individual built-in renderers are split out.
/// </summary>
internal sealed class WidgetRendererRegistry
{
    private readonly IReadOnlyDictionary<string, Func<LayoutWidgetElement, FrameworkElement>> _renderers;

    internal WidgetRendererRegistry(
        IReadOnlyDictionary<string, Func<LayoutWidgetElement, FrameworkElement>> renderers)
    {
        _renderers = renderers;
    }

    internal FrameworkElement Build(LayoutWidgetElement widget, Func<LayoutWidgetElement, FrameworkElement> fallback)
    {
        return _renderers.TryGetValue(widget.TypeId, out var renderer)
            ? renderer(widget)
            : fallback(widget);
    }
}
