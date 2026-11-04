// 拖动吸附只计算物理像素几何；不探测任务栏、不拥有窗口，也不保存设置。
using System.Windows;

namespace AFMediaBar.Classes.Services;

internal enum DragSnapTarget { None, Center, Top, Right, Bottom, Left }
internal enum DragSnapMode { HorizontalTaskbar, VerticalTaskbar, Floating }
internal readonly record struct DragSnapResult(Rect Bounds, DragSnapTarget Target);

/// <summary>主体与分离组件共用的吸附策略；进入和退出阈值分别为 16、24 DIP。</summary>
internal static class DragSnapPolicy
{
    internal static Rect Clamp(Rect bounds, Rect area)
    {
        var width = Math.Min(area.Width, bounds.Width);
        var height = Math.Min(area.Height, bounds.Height);
        return new Rect(Math.Clamp(bounds.Left, area.Left, area.Right - width),
            Math.Clamp(bounds.Top, area.Top, area.Bottom - height), width, height);
    }

    internal static DragSnapTarget[] Targets(DragSnapMode mode) => mode == DragSnapMode.Floating
        ? [DragSnapTarget.Top, DragSnapTarget.Right, DragSnapTarget.Bottom, DragSnapTarget.Left] : [DragSnapTarget.Center];

    internal static Rect TargetBounds(Rect raw, Rect area, DragSnapMode mode, DragSnapTarget target)
    {
        var bounds = Clamp(raw, area);
        var x = area.Left + (area.Width - bounds.Width) / 2;
        var y = area.Top + (area.Height - bounds.Height) / 2;
        return target switch
        {
            DragSnapTarget.Center => mode == DragSnapMode.VerticalTaskbar
                ? new Rect(bounds.Left, y, bounds.Width, bounds.Height) : new Rect(x, bounds.Top, bounds.Width, bounds.Height),
            DragSnapTarget.Top => new Rect(x, area.Top, bounds.Width, bounds.Height),
            DragSnapTarget.Right => new Rect(area.Right - bounds.Width, y, bounds.Width, bounds.Height),
            DragSnapTarget.Bottom => new Rect(x, area.Bottom - bounds.Height, bounds.Width, bounds.Height),
            DragSnapTarget.Left => new Rect(area.Left, y, bounds.Width, bounds.Height),
            _ => bounds
        };
    }

    internal static DragSnapResult Resolve(Rect raw, Rect area, double dpi, DragSnapMode mode, DragSnapTarget current)
    {
        var bounds = Clamp(raw, area);
        double Distance(Rect target) => mode switch
        {
            DragSnapMode.HorizontalTaskbar => Math.Abs(bounds.Left - target.Left),
            DragSnapMode.VerticalTaskbar => Math.Abs(bounds.Top - target.Top),
            _ => (bounds.TopLeft - target.TopLeft).Length
        };
        var targets = Targets(mode);
        if (current != DragSnapTarget.None && targets.Contains(current))
        {
            var held = TargetBounds(bounds, area, mode, current);
            if (Distance(held) <= 24 * dpi) return new(held, current);
        }
        var candidate = targets.Select(target => new DragSnapResult(TargetBounds(bounds, area, mode, target), target))
            .OrderBy(result => Distance(result.Bounds)).First();
        return Distance(candidate.Bounds) <= 16 * dpi ? candidate : new(bounds, DragSnapTarget.None);
    }
}
