using AFMediaBar.Interop;

namespace AFMediaBar.Models;

internal readonly record struct TaskbarHostBounds(
    nint Taskbar,
    NativeMethods.Rect ScreenBounds,
    uint Dpi)
{
    internal double Scale => Dpi > 0 ? Dpi / 96d : 1d;
}
