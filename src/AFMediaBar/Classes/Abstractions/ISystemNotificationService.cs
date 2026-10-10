// 系统通知的发送与点击契约；原生图标、消息窗口和释放由实现持有。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>发送系统通知并表达用户的点击意图。</summary>
public interface ISystemNotificationService
{
    event Action<ShellNotificationTarget>? NotificationClicked;

    /// <summary>尝试发送一次通知；返回 Shell 是否接受，不保证系统实际显示。</summary>
    bool TryShowNotification(string title, string message, ShellNotificationTarget target = ShellNotificationTarget.None);
}
