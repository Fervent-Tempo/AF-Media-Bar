using System.Runtime.InteropServices;
using AFMediaBar.Classes.Models;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>枚举输出设备，并切换默认 Console/Multimedia 端点。 / Enumerates render devices and switches the default Console/Multimedia endpoint.</summary>
public sealed class AudioDeviceService
{
    private static readonly Guid PolicyConfigClientClassId = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    public async Task<IReadOnlyList<AudioDeviceOption>> GetRenderDevicesAsync()
    {
        var defaultId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
        var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
        return devices
            .Where(device => device.IsEnabled)
            .Select(device => new AudioDeviceOption(
                device.Id,
                GetPolicyDeviceId(device.Id),
                string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name,
                string.Equals(device.Id, defaultId, StringComparison.OrdinalIgnoreCase)))
            // 默认状态只决定选中项，不参与排序，避免切换后设备位置跳动。
            // Default status controls selection only, keeping wheel indexes stable after a switch.
            .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void SetDefaultRenderDevice(string policyDeviceId)
    {
        object? client = null;
        try
        {
            var type = Type.GetTypeFromCLSID(PolicyConfigClientClassId, throwOnError: true)!;
            client = Activator.CreateInstance(type) ?? throw new InvalidOperationException("无法创建 Windows 音频策略服务。");
            var policy = (IPolicyConfig)client;
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(policyDeviceId, ERole.Console));
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(policyDeviceId, ERole.Multimedia));
        }
        finally
        {
            if (client is not null && Marshal.IsComObject(client))
            {
                Marshal.ReleaseComObject(client);
            }
        }
    }

    private static string GetPolicyDeviceId(string deviceInformationId)
    {
        const string marker = "MMDEVAPI#";
        var start = deviceInformationId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return deviceInformationId;
        }

        start += marker.Length;
        var end = deviceInformationId.IndexOf("#{", start, StringComparison.OrdinalIgnoreCase);
        return end > start ? deviceInformationId[start..end] : deviceInformationId[start..];
    }

    private enum ERole { Console, Multimedia, Communications }

    // vtable 顺序来自 Windows PolicyConfig ABI，未使用槽位仍须保留。
    // The vtable follows the Windows PolicyConfig ABI; unused slots must remain.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat(string id, out nint format);
        int GetDeviceFormat(string id, int defaultFormat, out nint format);
        int ResetDeviceFormat(string id);
        int SetDeviceFormat(string id, nint endpointFormat, nint mixFormat);
        int GetProcessingPeriod(string id, int defaultPeriod, out long period, out long minimumPeriod);
        int SetProcessingPeriod(string id, ref long period);
        int GetShareMode(string id, out nint mode);
        int SetShareMode(string id, nint mode);
        int GetPropertyValue(string id, int store, nint key, out nint value);
        int SetPropertyValue(string id, int store, nint key, nint value);
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
