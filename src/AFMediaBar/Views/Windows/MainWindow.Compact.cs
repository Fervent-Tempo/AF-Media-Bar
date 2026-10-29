// 仅在任务栏真实收起副信息后提示一次；点击定位正式的显示模式选择入口。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Resources;

namespace AFMediaBar.Views.Windows;

public partial class MainWindow
{
    private readonly SmallTaskbarNotificationGate _smallTaskbarNotification = new();

    private void TaskbarWindow_InformationAutomaticallyHidden(object? sender, EventArgs args)
    {
        if (sender is TaskbarWindow window && _taskbarWindows.Contains(window)) TryNotifySmallTaskbar();
    }

    private void TryNotifySmallTaskbar()
    {
        if (_isClosing || _runtime.IsSwitching || _runtime.RunningMode != WindowMode.Taskbar) return;
        _smallTaskbarNotification.TryNotify(_taskbarWindows.Any(window => window.HasAutomaticallyHiddenInformation),
            () => _systemNotifications.TryShowNotification(Translations.Get("Taskbar.Compact.Notification.Title"),
                Translations.Get("Taskbar.Compact.Notification.Body"), ShellNotificationTarget.FloatingBarExperiment, "Taskbar.Compact"));
    }
}
