using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 生成托盘音频状态提示文本，不执行音频读取或 UI 更新。
/// Builds tray audio status text without performing audio reads or UI updates.
/// </summary>
public static class AudioTooltipPolicy
{
    /// <summary>
    /// 根据滚轮模式、当前媒体应用和默认输出设备生成提示。
    /// Builds tooltip text from wheel mode, current media application, and default output device.
    /// </summary>
    public static string Build(
        TrayWheelBehavior behavior,
        ApplicationVolumeSnapshot? application,
        AudioDeviceOption? device)
    {
        return behavior switch
        {
            TrayWheelBehavior.AdjustVolume => application is null
                ? "当前媒体音量：不可用"
                : $"{application.DisplayName}：{application.VolumePercent}%",
            TrayWheelBehavior.SwitchOutputDevice => device is null
                ? "输出设备：不可用"
                : $"输出设备：{device.DisplayName}",
            _ => "托盘滚轮已禁用"
        };
    }
}
