// Named ITaskbarDockService to avoid clashing with Wpf.Ui's ITaskBarService.
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// Docking engine that attaches windows to the Explorer taskbar and positions them
/// in taskbar coordinates (ported from FluentFlyout's TaskbarWindow logic).
/// </summary>
public interface ITaskbarDockService
{
    /// <summary>
    /// Finds the taskbar window (Shell_TrayWnd / Shell_SecondaryTrayWnd) on the monitor
    /// with the given index. May return IntPtr.Zero while Explorer is (re)starting.
    /// </summary>
    IntPtr GetSelectedTaskbarHandle(int selectedMonitorIndex, out bool isMainTaskbarSelected);

    /// <summary>DPI scaling factor of the taskbar (GetDpiForWindow / 96). Returns 0 if the handle is invalid.</summary>
    double GetTaskbarDpiScale(IntPtr taskbarHandle);

    /// <summary>Gets the taskbar window bounds in screen coordinates.</summary>
    bool TryGetTaskbarRect(IntPtr taskbarHandle, out RECT rect);

    /// <summary>Turns the window into a WS_CHILD of the taskbar (SetParent).</summary>
    void DockWindow(IntPtr windowHandle, IntPtr taskbarHandle);

    /// <summary>
    /// Positions and sizes the child window over the taskbar. Coordinates are converted
    /// from screen space to taskbar-client space (ScreenToClient) before SetWindowPos.
    /// </summary>
    void SetWindowPosition(IntPtr windowHandle, IntPtr taskbarHandle, RECT taskbarRect, int width, int height);

    /// <summary>Clips the window so only the given rects are visible and hit-testable (SetWindowRgn).</summary>
    void ApplyInputRegion(IntPtr windowHandle, IReadOnlyList<RECT> rects);
}
