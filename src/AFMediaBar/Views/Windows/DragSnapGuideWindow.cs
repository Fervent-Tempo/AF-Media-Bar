// 无激活、鼠标穿透的拖动提示窗；由所属宿主复用和关闭，不注册 Hook 或计时器。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

/// <summary>在任务栏裁剪区以外绘制临时对齐点，不改变媒体栏的输入区域或层级。</summary>
public sealed class DragSnapGuideWindow : Window
{
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private nint _handle;
    private bool _closed;
    private (Rect Area, double Dpi, DragSnapMode Mode, DragSnapTarget Active, bool Blocked, bool HighContrast)? _presentation;

    public DragSnapGuideWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.Manual;
        Content = _canvas;
        WindowHelper.SetNoActivate(this);
        Closed += (_, _) => _closed = true;
    }

    internal void Dismiss() { if (!_closed) Hide(); }
    internal void CloseGuide() { if (!_closed) Close(); }

    internal void Present(Rect area, double dpi, DragSnapMode mode, DragSnapTarget active, bool blocked)
    {
        if (_closed) return;
        if (_handle == nint.Zero)
        {
            _handle = new WindowInteropHelper(this).EnsureHandle();
            // TOOLWINDOW 防止进入任务切换器；样式和物理位置必须在首次 Show 之前就绪。
            const int ToolWindow = 0x00000080;
            SetWindowLong(_handle, GWL_EXSTYLE, GetWindowLong(_handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | ToolWindow);
        }
        var presentation = (area, dpi, mode, active, blocked, SystemParameters.HighContrast);
        if (_presentation == presentation && IsVisible)
        {
            SetWindowPos(_handle, -1, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            return;
        }
        var place = !IsVisible || _presentation?.Area != area;
        if (place) Place();
        _presentation = presentation;
        _canvas.Children.Clear();
        var scale = VisualTreeHelper.GetDpi(this);
        var accent = SystemParameters.HighContrast ? SystemColors.HighlightBrush
            : TryFindResource("AccentTextFillColorPrimaryBrush") as Brush ?? SystemParameters.WindowGlassBrush;
        foreach (var target in DragSnapPolicy.Targets(mode))
        {
            var selected = active == target;
            var point = target switch
            {
                DragSnapTarget.Top => new Point(area.Width / 2, 4 * dpi),
                DragSnapTarget.Right => new Point(area.Width - 4 * dpi, area.Height / 2),
                DragSnapTarget.Bottom => new Point(area.Width / 2, area.Height - 4 * dpi),
                DragSnapTarget.Left => new Point(4 * dpi, area.Height / 2),
                _ => new Point(area.Width / 2, area.Height / 2)
            };
            var size = (selected ? 8 : 6) * dpi;
            var brush = blocked ? SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.Goldenrod : accent;
            var dot = new Ellipse
            {
                Width = size / scale.DpiScaleX,
                Height = size / scale.DpiScaleY,
                Stroke = brush,
                StrokeThickness = dpi / scale.DpiScaleX,
                Fill = blocked ? Brushes.Transparent : brush,
                Opacity = selected ? 1 : .8
            };
            Canvas.SetLeft(dot, (point.X - size / 2) / scale.DpiScaleX);
            Canvas.SetTop(dot, (point.Y - size / 2) / scale.DpiScaleY);
            _canvas.Children.Add(dot);
        }
        if (!IsVisible) Show();
        if (place) Place();

        void Place() => SetWindowPos(_handle, -1, (int)Math.Round(area.Left), (int)Math.Round(area.Top),
            Math.Max(1, (int)Math.Ceiling(area.Width)), Math.Max(1, (int)Math.Ceiling(area.Height)), SWP_NOACTIVATE);
    }
}
