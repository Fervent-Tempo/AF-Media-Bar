using System.Diagnostics;
using AFMediaBar.Classes.Models;
using Windows.Media.Audio;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 读取当前设备的空间音效状态，并通过系统设置提供受支持的配置入口。
/// Reads spatial-audio state and delegates configuration to the supported Windows settings surface.
/// </summary>
public sealed class SpatialAudioService
{
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

    public SpatialAudioSnapshot GetState(string deviceId)
    {
        try
        {
            var configuration = SpatialAudioDeviceConfiguration.GetForDeviceId(deviceId);
            if (!configuration.IsSpatialAudioSupported)
            {
                return new SpatialAudioSnapshot(false, "此设备不支持", null);
            }

            var active = configuration.ActiveSpatialAudioFormat;
            var name = string.IsNullOrWhiteSpace(active)
                ? "关闭"
                : KnownFormats.TryGetValue(active, out var known) ? known : "已启用";
            return new SpatialAudioSnapshot(true, name, active);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[SpatialAudioService] Read failed: {exception.Message}");
            return new SpatialAudioSnapshot(false, "状态不可用", null);
        }
    }

    public void OpenSystemSettings()
    {
        Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
    }
}
