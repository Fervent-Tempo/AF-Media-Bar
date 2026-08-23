using AFMediaBar.Interop;

namespace AFMediaBar.Models;

public readonly record struct TaskbarHostBounds(
    nint Taskbar,
    NativeMethods.Rect ScreenBounds,
    uint Dpi)
{
    public double Scale => Dpi > 0 ? Dpi / 96d : 1d;
}
