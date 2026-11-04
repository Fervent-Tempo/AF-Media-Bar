// 每个媒体栏宿主拥有一个手势会话和提示窗；结束手势隐藏，宿主释放时关闭。
using System.Windows;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Views.Windows;

internal readonly record struct DragSnapContext(Rect Area, double Dpi, DragSnapMode Mode, string MonitorId, nint Host);

internal sealed class DragSnapSession(Func<DragSnapGuideWindow>? factory) : IDisposable
{
    private DragSnapGuideWindow? _guide;
    private DragSnapContext? _context;
    private Size _size;
    private bool _disposed;
    private bool _invalid;
    internal DragSnapTarget Target { get; private set; }

    internal bool TryResolve(Rect raw, DragSnapContext context, bool merging, out Rect bounds)
    {
        bounds = raw;
        if (_disposed || _invalid || raw.IsEmpty || raw.Width <= 0 || raw.Height <= 0 || context.Area.IsEmpty ||
            context.Area.Width <= 0 || context.Area.Height <= 0 || !double.IsFinite(context.Dpi) || context.Dpi <= 0) return false;
        if (_context is { } frozen && (frozen != context || _size != raw.Size))
        {
            _invalid = true;
            Target = DragSnapTarget.None;
            _guide?.Dismiss();
            return false;
        }
        _context = context;
        _size = raw.Size;
        if (merging)
        {
            Target = DragSnapTarget.None;
            _guide?.Dismiss();
            bounds = DragSnapPolicy.Clamp(raw, context.Area);
            return true;
        }
        var result = DragSnapPolicy.Resolve(raw, context.Area, context.Dpi, context.Mode, Target);
        Target = result.Target;
        bounds = result.Bounds;
        return true;
    }

    internal void Show(bool blocked = false)
    {
        if (_disposed || _invalid || _context is not { } context || factory is null) return;
        (_guide ??= factory()).Present(context.Area, context.Dpi, context.Mode, Target, blocked);
    }

    internal void Reset()
    {
        _context = null;
        _invalid = false;
        Target = DragSnapTarget.None;
        _guide?.Dismiss();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
        _guide?.CloseGuide();
        _guide = null;
    }
}
