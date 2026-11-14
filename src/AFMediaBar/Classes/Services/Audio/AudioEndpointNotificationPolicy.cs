// 识别 WASAPI 端点属性通知中由环回采集启停产生的项；这些通知不能反过来触发采集重建。
using NAudio.CoreAudioApi;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>区分普通端点变化与采集自身产生的生命周期属性通知。</summary>
internal static class AudioEndpointNotificationPolicy
{
    // Windows 未公开该属性键；实测环回采集每开始/停止一次会为所有端点发出 {c4f6b0fa-...},3。
    private static readonly Guid CaptureStateFormatId = new("c4f6b0fa-fd32-4c99-8050-8f8a193a7e48");
    private const int CaptureStatePropertyId = 3;

    /// <summary>该通知是否应由采集启停忽略。/ Whether a capture start/stop notification should be ignored.</summary>
    internal static bool IsCaptureState(PropertyKey key) =>
        key.formatId == CaptureStateFormatId && key.propertyId == CaptureStatePropertyId;

    /// <summary>该属性变化是否需要让音频平台刷新。/ Whether this property change should invalidate the audio platform.</summary>
    internal static bool ShouldInvalidate(PropertyKey key) => !IsCaptureState(key);
}
