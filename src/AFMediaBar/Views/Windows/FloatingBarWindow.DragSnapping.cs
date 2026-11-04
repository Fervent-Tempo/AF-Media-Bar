// 悬浮主体与组件共用所属显示器的四边中点，不允许跨显示器写回位置。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Views.Windows;

public sealed partial class FloatingBarWindow
{
    private readonly DragSnapSession _dragSnap;
    private int _bodyDragGeneration;
    private DragSnapContext FloatingSnapContext => new(PlacementArea, Dpi, DragSnapMode.Floating, Monitor.DeviceId, _handle);

    private static FloatingBarDockEdge SnappedDockEdge(DragSnapTarget target) => target switch
    {
        DragSnapTarget.Top => FloatingBarDockEdge.Top,
        DragSnapTarget.Right => FloatingBarDockEdge.Right,
        DragSnapTarget.Bottom => FloatingBarDockEdge.Bottom,
        DragSnapTarget.Left => FloatingBarDockEdge.Left,
        _ => FloatingBarDockEdge.None
    };
}
