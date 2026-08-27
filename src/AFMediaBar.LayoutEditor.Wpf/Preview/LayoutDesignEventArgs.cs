using System.Windows;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.LayoutEditor.Wpf.Preview;

public sealed class LayoutDesignElementEventArgs(
    string instanceId,
    DependencyObject source,
    bool isContainer = false) : EventArgs
{
    public string InstanceId { get; } = instanceId;
    public DependencyObject Source { get; } = source;
    public bool IsContainer { get; } = isContainer;
}

public sealed class LayoutDesignPreviewStateEventArgs(
    string containerId,
    bool pointerNear) : EventArgs
{
    public string ContainerId { get; } = containerId;
    public bool PointerNear { get; } = pointerNear;
}

public sealed class LayoutDesignResizeEventArgs(
    string instanceId,
    LayoutEdge edge,
    double deltaDip) : EventArgs
{
    public string InstanceId { get; } = instanceId;
    public LayoutEdge Edge { get; } = edge;
    public double DeltaDip { get; } = deltaDip;
}

public sealed class LayoutDesignDeleteEventArgs(
    string instanceId,
    FrameworkElement source,
    Point position) : EventArgs
{
    public string InstanceId { get; } = instanceId;
    public FrameworkElement Source { get; } = source;
    public Point Position { get; } = position;
}
