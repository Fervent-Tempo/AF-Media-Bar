// 完整层打开意图携带稳定屏幕锚点；不持有宿主或窗口资源。
using System.Windows;

namespace AFMediaBar.Classes.Models;

/// <summary>屏幕 DIP 锚点及打开语义；仅打开请求不会切换关闭已有面板。</summary>
public sealed class FullPanelOpenRequestEventArgs(Rect anchor, bool verticalAnchor, bool ensureOpen) : EventArgs
{
    public Rect Anchor { get; } = anchor;
    public bool VerticalAnchor { get; } = verticalAnchor;
    public bool EnsureOpen { get; } = ensureOpen;
}
