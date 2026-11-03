// 越界宠物只扩展自己的透明窗口，不改变任务栏宿主的厚度或可用区。
using System.Windows;

namespace AFMediaBar.Components;

internal static class PetPlacementPolicy
{
    internal static Rect Arrange(Rect anchor, Rect screen, double size, bool vertical, bool onLeft, Rect? workArea = null)
    {
        var x = anchor.Left + (anchor.Width - size) / 2;
        var y = anchor.Top + (anchor.Height - size) / 2;
        if (vertical) x = onLeft ? anchor.Left : anchor.Right - size;
        else y = anchor.Top + anchor.Height / 2 < screen.Top + screen.Height / 2 ? anchor.Top : anchor.Bottom - size;
        if (workArea is { } work)
        {
            if (vertical) x = (onLeft ? (work.Left > screen.Left ? work.Left : anchor.Right) : (work.Right < screen.Right ? work.Right : anchor.Left)) - size / 2;
            else y = anchor.Top + anchor.Height / 2 < screen.Top + screen.Height / 2 ? (work.Top > screen.Top ? work.Top : anchor.Bottom) - size / 2
                    : (work.Bottom < screen.Bottom ? work.Bottom : anchor.Top) - size / 2;
        }
        return new Rect(Math.Clamp(x, screen.Left, Math.Max(screen.Left, screen.Right - size)),
            Math.Clamp(y, screen.Top, Math.Max(screen.Top, screen.Bottom - size)), size, size);
    }
}
