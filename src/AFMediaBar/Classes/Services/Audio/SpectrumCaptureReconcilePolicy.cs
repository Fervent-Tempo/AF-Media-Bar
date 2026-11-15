// 设备通知只触发状态对账；目标端点、采集格式和端点状态才是重建依据。
using NAudio.CoreAudioApi;
using NAudio.Wave;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 判断一次端点解析是否需要重建采集，以及比较设备混音格式的稳定签名。
/// Decides whether a resolution requires rebuilding capture and provides a stable signature for device mix formats.
/// </summary>
internal static class SpectrumCaptureReconcilePolicy
{
    /// <summary>
    /// 只有“当前设备 + 跟随媒体来源”且目标端点变化时才重建；其它模式仍按各自显式路径处理。
    /// Only a changed target under "current device + follow media source" rebuilds here; the other modes keep their explicit paths.
    /// </summary>
    internal static bool ShouldRestartForResolution(
        SpectrumCaptureSource source,
        SpectrumCaptureDeviceMode mode,
        bool targetChanged) =>
        source == SpectrumCaptureSource.CurrentDevice &&
        mode == SpectrumCaptureDeviceMode.FollowMediaSource &&
        targetChanged;

    /// <summary>端点是否仍处于活动状态。/ Whether the endpoint is still active.</summary>
    internal static bool IsDeviceActive(DeviceState state) => state == DeviceState.Active;

    /// <summary>
    /// 比较采集格式与设备混音格式的核心字段。<see cref="WaveFormat"/>/<see cref="WaveFormatExtensible"/> 的包装差异
    /// 不应触发格式校验，否则会制造“格式一直不同 → 每秒重建”的循环。
    /// Compares the core fields of the capture format and the device mix format. A wrapper difference between
    /// <see cref="WaveFormat"/> and <see cref="WaveFormatExtensible"/> must not enable the format watchdog, or it would create a
    /// "formats never match → rebuild every second" loop.
    /// </summary>
    internal static bool CoreFormatsMatch(WaveFormat captureFormat, WaveFormat mixFormat)
    {
        // WaveFormatExtensible 用 SubFormat 表达 PCM/IEEE float，capture.WaveFormat 可能是标准化后的普通 WaveFormat；
        // 比较前先把两者归一到实际采样编码。其它扩展子格式无法安全比较，直接禁用本次格式校验。
        // WaveFormatExtensible carries PCM/IEEE float in SubFormat while capture.WaveFormat may be a standardized plain
        // WaveFormat, so both are normalized to their actual sample encoding before comparison. Other extensible sub-formats
        // cannot be compared safely, so the watchdog stays off for this capture.
        var captureEncoding = ResolveEncoding(captureFormat);
        var mixEncoding = ResolveEncoding(mixFormat);
        if (captureEncoding is null || mixEncoding is null) return false;
        return captureEncoding == mixEncoding &&
               captureFormat.SampleRate == mixFormat.SampleRate &&
               captureFormat.Channels == mixFormat.Channels &&
               captureFormat.BitsPerSample == mixFormat.BitsPerSample &&
               captureFormat.BlockAlign == mixFormat.BlockAlign;
    }

    private static WaveFormatEncoding? ResolveEncoding(WaveFormat format)
    {
        if (format is not WaveFormatExtensible extensible) return format.Encoding;
        if (extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT) return WaveFormatEncoding.IeeeFloat;
        if (extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_PCM) return WaveFormatEncoding.Pcm;
        return null;
    }

    /// <summary>
    /// 设备混音格式的稳定签名；扩展格式还记录子格式、有效位和声道掩码。
    /// Stable signature of a device mix format; extensible formats also record the sub-format, valid bits, and channel mask.
    /// </summary>
    internal static CaptureFormatSignature CreateFormatSignature(WaveFormat format) =>
        format is WaveFormatExtensible extensible
            ? new CaptureFormatSignature(format.Encoding, format.SampleRate, format.Channels, format.BitsPerSample,
                format.BlockAlign, extensible.SubFormat, extensible.ValidBitsPerSample, extensible.ChannelMask)
            : new CaptureFormatSignature(format.Encoding, format.SampleRate, format.Channels, format.BitsPerSample,
                format.BlockAlign, null, null, null);

    /// <summary>当前格式是否已偏离采集启动时记录的基线。/ Whether the current format has drifted from the baseline recorded at capture start.</summary>
    internal static bool HasFormatChanged(
        CaptureFormatSignature baseline,
        CaptureFormatSignature current) =>
        !baseline.Equals(current);

    /// <summary>一次设备混音格式的稳定比较键。/ Stable comparison key for one device mix format.</summary>
    internal readonly record struct CaptureFormatSignature(
        WaveFormatEncoding Encoding,
        int SampleRate,
        int Channels,
        int BitsPerSample,
        int BlockAlign,
        Guid? SubFormat,
        int? ValidBitsPerSample,
        int? ChannelMask);
}
