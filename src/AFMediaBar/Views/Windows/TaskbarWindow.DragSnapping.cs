// 任务栏宿主持有吸附会话与仅在主体拖动时运行的兜底计时器；挂起和关闭统一清理。
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

public partial class TaskbarWindow
{
    private readonly DragSnapSession _dragSnap;
    private int _bodyDragGeneration;
    private readonly DispatcherTimer _bodyDragTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };

    private DragSnapContext SnapContext(RECT taskbar, double dpi) => new(
        new Rect(taskbar.Left, taskbar.Top, taskbar.Right - taskbar.Left, taskbar.Bottom - taskbar.Top), dpi,
        IsVerticalPresentation ? DragSnapMode.VerticalTaskbar : DragSnapMode.HorizontalTaskbar, _targetMonitorDeviceId, _lastTaskbarHandle);

    private bool ResolveBodyDrag(int cursorPrimary, RECT taskbar, double dpi)
    {
        if (dpi <= 0) return false;
        var canvas = MediaControl.CurrentLayout?.Canvas;
        var crossLength = IsVerticalPresentation ? taskbar.Right - taskbar.Left : taskbar.Bottom - taskbar.Top;
        var thickness = Math.Min(crossLength, (int)Math.Round((IsVerticalPresentation ? canvas?.Width ?? 48 : canvas?.Height ?? 44) * dpi));
        var cross = Math.Clamp((crossLength - thickness) / 2 +
            (int)Math.Round(SettingsManager.Current.TaskbarBarCrossAxisOffsetDip * dpi), 0, Math.Max(0, crossLength - thickness));
        var primary = Math.Clamp(_dragStartBarPrimary + cursorPrimary - _dragStartCursorPrimary, 0, _dragPrimaryLimit);
        var raw = IsVerticalPresentation
            ? new Rect(taskbar.Left + cross, taskbar.Top + primary, thickness, _bodyDragLength)
            : new Rect(taskbar.Left + primary, taskbar.Top + cross, _bodyDragLength, thickness);
        if (!_dragSnap.TryResolve(raw, SnapContext(taskbar, dpi), false, out var snapped)) return false;
        _bodyDragPrimary = (int)Math.Round(IsVerticalPresentation ? snapped.Top - taskbar.Top : snapped.Left - taskbar.Left);
        return true;
    }

    private void ShowTaskbarSnap(RECT taskbar, double dpi, int length, TaskbarRestComponent? movingComponent)
    {
        var ranges = CurrentComponentRanges(taskbar, dpi, duringGesture: true);
        var occupied = _detachedRects.Where(pair => pair.Key != movingComponent).Select(pair => PrimaryRange(pair.Value)).ToList();
        if (movingComponent.HasValue && _renderedPrimaryRect is { } body && HasArea(body))
        {
            // 渲染矩形在竖向路径中交换了轴；预留区始终使用任务栏客户区坐标。
            occupied.Add(IsVerticalPresentation ? new(body.Left, body.Right) : PrimaryRange(body));
        }
        var free = ComponentPlacementPolicy.Subtract(ranges, occupied, (int)Math.Ceiling(4 * dpi));
        var center = ((IsVerticalPresentation ? taskbar.Bottom - taskbar.Top : taskbar.Right - taskbar.Left) - length) / 2d;
        _dragSnap.Show(!free.Any(range => range.Start <= center && range.End >= center + length));
    }

    private void BodyDragTick(object? sender, EventArgs args)
    {
        if (!_isDragging) { _bodyDragTimer.Stop(); return; }
        if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) { EndTaskbarDrag(releaseCapture: false); return; }
        MediaControl_PreviewMouseMove(this, new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseMoveEvent });
    }

    private void DisposeDragSnapping()
    {
        _bodyDragTimer.Stop();
        _bodyDragTimer.Tick -= BodyDragTick;
        _dragSnap.Dispose();
    }
}
