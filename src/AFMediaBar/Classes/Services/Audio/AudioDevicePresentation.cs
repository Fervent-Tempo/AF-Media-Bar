// 设备操作结果的本地化呈现，不执行平台读取。
using AFMediaBar.Classes.Models;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>按结果状态生成反馈，设备未变化时返回空文本，不将路由偏好误报为音频流迁移。</summary>
public static class AudioDevicePresentation
{
    public static string GetResultText(AudioDeviceOperationResult result, bool application)
    {
        if (result.Status == AudioDeviceOperationStatus.Unchanged) return string.Empty;
        return Translations.Get(result.Status switch
        {
            AudioDeviceOperationStatus.Applied => application ? "Audio.Route.Saved" : "Audio.Device.Applied",
            AudioDeviceOperationStatus.Unsupported => "Audio.Route.Unsupported",
            AudioDeviceOperationStatus.Partial => "Audio.Route.Partial",
            AudioDeviceOperationStatus.Cancelled => "Audio.Device.Cancelled",
            AudioDeviceOperationStatus.Unavailable => application ? "Audio.App.Unavailable" : "Audio.Device.Unavailable",
            _ => "Audio.Route.Failed"
        });
    }
}
