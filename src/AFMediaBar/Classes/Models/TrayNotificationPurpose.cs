// 标明当前 Shell 通知的用途；仅影响点击路由，不是持久化设置。
namespace AFMediaBar.Classes.Models;

/// <summary>系统通知用途，避免空间提示误导航到更新页面。</summary>
public enum TrayNotificationPurpose { Update, TaskbarPlacement }

/// <summary>Shell 通知点击的当前用途。</summary>
public sealed class TrayNotificationClickedEventArgs(TrayNotificationPurpose purpose) : EventArgs
{
    public TrayNotificationPurpose Purpose { get; } = purpose;
}
