namespace AFMediaBar.Classes.Models;

/// <summary>Shell 通知区域图标的物理像素范围。 / Physical-pixel bounds of a Shell notification icon.</summary>
public readonly record struct TrayIconBounds(int Left, int Top, int Right, int Bottom)
{
    /// <summary>
    /// 调用 Contains，提供 API。
    /// Provides the public Contains entry point required by this component.
    /// </summary>
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}
