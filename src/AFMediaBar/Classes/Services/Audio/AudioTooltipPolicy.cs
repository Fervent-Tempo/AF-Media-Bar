using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 生成托盘音频状态提示文本，不执行音频读取或 UI 更新。
///
/// 提示是纯策略拼出来的（拿不到依赖注入），因此按当前界面语言取值；设备名与应用名来自系统，原样保留。
/// Builds tray audio status text without performing audio reads or UI updates.
///
/// The text is composed by a pure policy that cannot receive dependency injection and therefore reads the active interface
/// language; device and application names come from the system and are kept as they are.
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
            TrayWheelBehavior.AdjustVolume => BuildMediaVolume(application?.DisplayName, application?.VolumePercent),
            TrayWheelBehavior.SwitchOutputDevice => BuildOutputDevice(device),
            TrayWheelBehavior.SwitchInputDevice => device?.DisplayName ?? Translations.Get("Audio.Device.Unavailable"),
            _ => Translations.Get("Audio.Tooltip.WheelDisabled")
        };
    }

    /// <summary>
    /// 生成输出设备提示；托盘与任务栏悬停层按钮共用同一措辞。
    /// Builds the output-device tooltip; the tray and the taskbar hover-layer button share this wording.
    /// </summary>
    public static string BuildOutputDevice(AudioDeviceOption? device) =>
        device is null
            ? Translations.Get("Audio.OutputDevice.Unavailable")
            : Translations.Format("Audio.OutputDevice.Value", device.DisplayName);

    /// <summary>
    /// 生成当前媒体音量提示；无媒体应用或音量不可读时给出明确回退。
    /// Builds the current-media-volume tooltip, with an explicit fallback when no media application or volume is available.
    /// </summary>
    public static string BuildMediaVolume(string? displayName, int? volumePercent) =>
        string.IsNullOrWhiteSpace(displayName) || volumePercent is null
            ? Translations.Get("Audio.Volume.Unavailable")
            : Translations.Format("Audio.Volume.Value", displayName, volumePercent);
}
