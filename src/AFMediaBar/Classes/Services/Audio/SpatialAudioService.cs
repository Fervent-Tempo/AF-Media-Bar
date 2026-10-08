using System.Diagnostics;
using AFMediaBar.Classes.Models;
using AFMediaBar.Resources;
using Windows.Media.Audio;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 读取当前设备的空间音效状态，并通过系统设置提供受支持的配置入口。
///
/// 状态名按当前界面语言取值；格式名（Windows Sonic、Dolby Atmos 等）是系统与品牌给出的名字，原样显示。
/// Reads spatial-audio state and delegates configuration to the supported Windows settings surface.
///
/// State names follow the active interface language; format names (Windows Sonic, Dolby Atmos, and so on) are the names the
/// system and the brands give them and are shown as they are.
/// </summary>
public sealed class SpatialAudioService
{
    // Windows 用全零 GUID 而非空字符串表示「空间音效未启用」，只判空会把闲置设备误报为已启用。
    private const string EmptyFormatSubtype = "{00000000-0000-0000-0000-000000000000}";

    private static readonly IReadOnlyDictionary<string, string> KnownFormats =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SpatialAudioFormatSubtype.WindowsSonic] = "Windows Sonic",
            [SpatialAudioFormatSubtype.DolbyAtmosForHeadphones] = "Dolby Atmos for Headphones",
            [SpatialAudioFormatSubtype.DolbyAtmosForHomeTheater] = "Dolby Atmos for Home Theater",
            [SpatialAudioFormatSubtype.DolbyAtmosForSpeakers] = "Dolby Atmos for Speakers",
            [SpatialAudioFormatSubtype.DTSHeadphoneX] = "DTS Headphone:X",
            [SpatialAudioFormatSubtype.DTSXUltra] = "DTS:X Ultra"
        };

    private static bool IsActive(string? formatSubtype)
    {
        return !string.IsNullOrWhiteSpace(formatSubtype)
            && !string.Equals(formatSubtype, EmptyFormatSubtype, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 查询指定输出设备的空间音效状态；平台不支持或查询失败时返回不可用快照。
    /// Queries spatial-audio state for an output device and returns an unavailable snapshot when unsupported or failed.
    /// </summary>
    public SpatialAudioSnapshot GetState(string deviceId)
    {
        try
        {
            var configuration = SpatialAudioDeviceConfiguration.GetForDeviceId(deviceId);
            if (!configuration.IsSpatialAudioSupported)
            {
                return new SpatialAudioSnapshot(false, Translations.Get("Audio.Spatial.Unsupported"), null);
            }

            // 关闭态取 Default 会取到"用户选过但未生效"的格式，同样误报，故只认 Active。
            var active = configuration.ActiveSpatialAudioFormat;
            if (!IsActive(active))
            {
                return new SpatialAudioSnapshot(true, Translations.Get("Audio.Spatial.Off"), null);
            }

            var name = TryGetKnownFormatName(active, out var known)
                ? known
                : Translations.Get("Audio.Spatial.Enabled");
            return new SpatialAudioSnapshot(true, name, active);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[SpatialAudioService] Read failed: {exception.Message}");
            return new SpatialAudioSnapshot(false, Translations.Get("Audio.Spatial.Unavailable"), null);
        }
    }

    /// <summary>
    /// 打开 Windows 声音设置，由系统界面负责后续空间音效配置。
    /// Opens Windows sound settings and delegates further spatial-audio configuration to the system UI.
    /// </summary>
    public void OpenSystemSettings()
    {
        Process.Start(new ProcessStartInfo("ms-settings:sound-devices") { UseShellExecute = true });
    }

    private static bool TryGetKnownFormatName(string subtype, out string name)
    {
        if (KnownFormats.TryGetValue(subtype, out name!))
        {
            return true;
        }

        if (!Guid.TryParse(subtype, out var candidate))
        {
            name = string.Empty;
            return false;
        }

        foreach (var format in KnownFormats)
        {
            if (Guid.TryParse(format.Key, out var known) && candidate == known)
            {
                name = format.Value;
                return true;
            }
        }

        name = string.Empty;
        return false;
    }
}
