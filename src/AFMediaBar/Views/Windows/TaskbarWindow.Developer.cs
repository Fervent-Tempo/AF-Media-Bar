// 每个窗口使用独立测试身份，重建后旧目标失效；不拥有开发者服务。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

public partial class TaskbarWindow
{
    internal string DeveloperHostId { get; } = Guid.NewGuid().ToString("N");
    internal DeveloperLyricsHostState CaptureDeveloperLyricsState() => MediaControl.CaptureDeveloperLyricsState(DeveloperHostId, _targetMonitorDeviceId);
    internal DeveloperActionResult ExecuteDeveloperLyricsAction(string command) => MediaControl.ExecuteDeveloperLyricsAction(command);

    /// <summary>
    /// 开发诊断：直接写空区域，验证不可见宿主的自愈链路；只模拟裁剪故障，不证明与现场根因相同。
    /// </summary>
    internal DeveloperActionResult BreakSurfaceForDiagnostics()
    {
        if (_isClosing || _isEnvironmentSuspended || _windowHandle == IntPtr.Zero || !_hasTaskbarPresentationPlacement)
            return new(DeveloperActionStatus.Unavailable);
        var region = CreateRectRgn(0, 0, 0, 0);
        if (region == IntPtr.Zero)
            return new(DeveloperActionStatus.Failed);
        // 只有成功后区域所有权才转移给窗口。
        if (!SetWindowRgn(_windowHandle, region, true))
        {
            DeleteObject(region);
            return new(DeveloperActionStatus.Failed);
        }

        _surfaceSession.Reset();
        _staleSurfaceSamples = 0;
        AppLogService.Current?.Info("Taskbar", $"开发诊断：已把媒体栏宿主裁成空区域，等待自愈重建: {_targetMonitorDeviceId}");
        return new(DeveloperActionStatus.Accepted);
    }
}
