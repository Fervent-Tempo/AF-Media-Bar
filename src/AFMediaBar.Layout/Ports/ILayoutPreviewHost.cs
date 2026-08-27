using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Ports;

/// <summary>
/// Adapter boundary for displaying a layout in a host. The editor owns the
/// state and geometry; WPF owns the actual visual tree.
/// </summary>
public interface ILayoutPreviewHost
{
    void Show(LayoutProfile profile, LayoutGridRect? previewBounds, string? selectedInstanceId);
}
