namespace AFMediaBar.Classes.Models;

/// <summary>播放队列面板打开意图：携带队列按钮的物理屏幕边界，不持有宿主或窗口资源。</summary>
public sealed class PlayerQueuePanelRequestEventArgs(TrayIconBounds anchor) : EventArgs
{
    /// <summary>队列按钮的物理屏幕边界。/ Physical screen bounds of the queue button.</summary>
    public TrayIconBounds Anchor { get; } = anchor;
}
