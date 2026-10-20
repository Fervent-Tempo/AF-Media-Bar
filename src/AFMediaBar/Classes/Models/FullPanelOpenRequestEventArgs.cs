// 完整层打开意图携带稳定屏幕锚点；不持有宿主或窗口资源。
using System.Windows;

namespace AFMediaBar.Classes.Models;

/// <summary>完整面板相对媒体栏的打开侧；不参与设置持久化。</summary>
public enum FullPanelPlacementSide { Above, Below, Left, Right }

/// <summary>屏幕 DIP 锚点及打开语义；仅打开请求不会切换关闭已有面板。</summary>
public sealed class FullPanelOpenRequestEventArgs(Rect anchor, bool verticalAnchor, bool ensureOpen,
    FullPanelPlacementSide? placementSide = null) : EventArgs
{
    public Rect Anchor { get; } = anchor;
    public bool VerticalAnchor { get; } = verticalAnchor;
    public bool EnsureOpen { get; } = ensureOpen;
    public FullPanelPlacementSide? PlacementSide { get; } = placementSide;
}
