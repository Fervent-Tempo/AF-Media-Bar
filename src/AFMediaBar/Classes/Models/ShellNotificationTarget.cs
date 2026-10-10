// Shell 通知的点击意图；不保存窗口、页面类型或持久化设置。
namespace AFMediaBar.Classes.Models;

/// <summary>系统通知点击后由宿主处理的目标。</summary>
public enum ShellNotificationTarget
{
    None,
    Application,
    TaskbarBackground,
    LyricsRecovery,
    DeveloperLyricsPreview
}
