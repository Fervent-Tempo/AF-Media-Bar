namespace AFMediaBar.Classes.Services;

/// <summary>
/// 封装音频选择和音量应用的延迟、版本淘汰及生命周期判断。
/// Encapsulates delay, version supersession, and lifecycle decisions for audio changes.
/// </summary>
public static class AudioApplyPolicy
{
    /// <summary>输出设备预览延迟（毫秒）。/ Output-device preview delay in milliseconds.</summary>
    public const int OutputDevicePreviewDelayMilliseconds = 1200;

    /// <summary>应用音量拖动延迟（毫秒）。/ Application-volume drag delay in milliseconds.</summary>
    public const int ApplicationVolumeDelayMilliseconds = 100;

    /// <summary>
    /// 判断延迟任务是否仍然是当前有效任务。
    /// Determines whether a delayed operation is still current.
    /// </summary>
    public static bool IsCurrent(bool disposed, int scheduledVersion, int currentVersion) =>
        !disposed && scheduledVersion == currentVersion;
}
