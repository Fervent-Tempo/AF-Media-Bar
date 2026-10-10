// 每个窗口使用独立测试身份，重建后旧目标失效；不拥有开发者服务。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Views.Windows;

public partial class TaskbarWindow
{
    internal string DeveloperHostId { get; } = Guid.NewGuid().ToString("N");
    internal DeveloperLyricsHostState CaptureDeveloperLyricsState() => MediaControl.CaptureDeveloperLyricsState(DeveloperHostId, _targetMonitorDeviceId);
    internal DeveloperActionResult ExecuteDeveloperLyricsAction(string command) => MediaControl.ExecuteDeveloperLyricsAction(command);
}
