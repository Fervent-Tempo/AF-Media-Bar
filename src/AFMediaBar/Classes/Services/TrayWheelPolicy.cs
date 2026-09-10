using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 计算托盘滚轮输入对应的设备步进或媒体音量步进。
/// Calculates output-device or media-volume steps for tray-wheel input.
/// </summary>
public static class TrayWheelPolicy
{
    /// <summary>
    /// 将滚轮增量转换为带方向的音量步数。
    /// Converts a wheel delta into signed volume steps.
    /// </summary>
    public static int GetVolumeSteps(int delta)
    {
        if (delta == 0)
            return 0;

        var steps = WheelInput.GetStepCount(delta);
        return delta > 0 ? steps : -steps;
    }

    /// <summary>
    /// 返回当前滚轮行为是否需要媒体音量处理。
    /// Returns whether the selected wheel behavior adjusts media volume.
    /// </summary>
    public static bool AdjustsVolume(TrayWheelBehavior behavior) =>
        behavior == TrayWheelBehavior.AdjustVolume;
}
