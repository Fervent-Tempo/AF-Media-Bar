// 在统一通知边界选择呈现渠道；需要操作的通知继续由 Shell 处理。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

/// <summary>宠物接收失败时立即回退系统通知，保留原点击契约。</summary>
public sealed class PetNotificationRouter(ShellTrayIconService shell, PetCompanionService pet) : ISystemNotificationService
{
    public event Action<ShellNotificationTarget>? NotificationClicked
    {
        add => shell.NotificationClicked += value;
        remove => shell.NotificationClicked -= value;
    }

    public bool TryShowNotification(string title, string message, ShellNotificationTarget target = ShellNotificationTarget.None) =>
        (target is ShellNotificationTarget.None or ShellNotificationTarget.MemoryResult && pet.TryNotify(title, message)) ||
        shell.TryShowNotification(title, message, target);
}
