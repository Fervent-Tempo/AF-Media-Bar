// 识别 WPF 合成通道失效：DWM 重启或显卡复位后渲染线程已退出，本进程无法再合成任何窗口。
using System.Runtime.InteropServices;

namespace AFMediaBar.Classes.Services.Startup;

/// <summary>渲染通道失效的识别与自动重启门禁。</summary>
internal static class RenderChannelFailurePolicy
{
    /// <summary>UCEERR_RENDERTHREADFAILURE：DWM 侧的渲染线程已退出，进程内没有可重建的通道。</summary>
    private const int RenderThreadFailure = unchecked((int)0x88980406);

    /// <summary>启动后这段时间内再次失效只退出不重启，避免 DWM 持续失效时把进程反复拉起。</summary>
    internal static readonly TimeSpan MinimumUptimeBeforeRestart = TimeSpan.FromSeconds(60);

    /// <summary>只认渲染线程退出这一个 HRESULT；其它 COM 失败仍按原有崩溃路径处理。</summary>
    internal static bool IsRenderChannelFailure(Exception? exception) =>
        exception is COMException && exception.HResult == RenderThreadFailure;

    /// <summary>是否允许自动重启；每次运行最多重启一次由调用方保证。</summary>
    internal static bool CanRestart(TimeSpan uptime) => uptime >= MinimumUptimeBeforeRestart;
}
