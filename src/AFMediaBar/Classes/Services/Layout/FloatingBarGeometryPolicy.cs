// 屏幕内的悬浮位置与尺寸约束，只处理工作区相对 DIP，不调用平台或改写保存位置。
using System.Windows;

namespace AFMediaBar.Classes.Services;

/// <summary>尺寸、朝向和 DPI 改变时保留中心并限制在所属显示器工作区。</summary>
public static class FloatingBarGeometryPolicy
{
    public static Rect Resolve(Size workArea, Size requested, Point center)
    {
        workArea = new Size(double.IsFinite(workArea.Width) ? Math.Max(1, workArea.Width) : 1,
            double.IsFinite(workArea.Height) ? Math.Max(1, workArea.Height) : 1);
        var width = Math.Clamp(double.IsFinite(requested.Width) ? requested.Width : 1, 1, Math.Max(1, workArea.Width));
        var height = Math.Clamp(double.IsFinite(requested.Height) ? requested.Height : 1, 1, Math.Max(1, workArea.Height));
        var x = double.IsFinite(center.X) ? center.X : workArea.Width / 2;
        var y = double.IsFinite(center.Y) ? center.Y : workArea.Height / 2;
        return new Rect(Math.Clamp(x - width / 2, 0, Math.Max(0, workArea.Width - width)),
            Math.Clamp(y - height / 2, 0, Math.Max(0, workArea.Height - height)), width, height);
    }
}
