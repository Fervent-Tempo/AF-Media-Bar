using System.Runtime.InteropServices;
using AFMediaBar.Models;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace AFMediaBar.Services;

/// <summary>
/// 枚举 Windows 输出设备，并通过音频策略接口切换默认端点。
/// Enumerates Windows render devices and switches the default audio endpoint.
/// </summary>
internal sealed class AudioDeviceService
{
    // PolicyConfig 是 Windows 自用的未公开 COM 接口，未来系统版本可能改变。
    // PolicyConfig is an undocumented Windows COM interface and may change.
    private static readonly Guid PolicyConfigClientClassId =
        new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    internal async Task<IReadOnlyList<AudioDeviceOption>> GetRenderDevicesAsync()
    {
        var defaultId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
        var deviceInformation = await DeviceInformation.FindAllAsync(
            MediaDevice.GetAudioRenderSelector());
        return deviceInformation
            .Where(device => device.IsEnabled)
            .Select(device => new AudioDeviceOption(
                device.Id,
                GetPolicyDeviceId(device.Id),
                string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name,
                string.Equals(device.Id, defaultId, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetPolicyDeviceId(string deviceInformationId)
    {
        // WinRT 返回设备接口路径，PolicyConfig 只接受其中的 MMDevice 端点 ID。
        // WinRT returns an interface path; PolicyConfig expects its MMDevice endpoint ID.
        const string marker = "MMDEVAPI#";
        var start = deviceInformationId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return deviceInformationId;
        }

        start += marker.Length;
        var end = deviceInformationId.IndexOf("#{", start, StringComparison.OrdinalIgnoreCase);
        return end > start
            ? deviceInformationId[start..end]
            : deviceInformationId[start..];
    }

    internal void SetDefaultRenderDevice(string deviceId)
    {
        object? client = null;
        try
        {
            var clientType = Type.GetTypeFromCLSID(
                PolicyConfigClientClassId,
                throwOnError: true)!;
            client = Activator.CreateInstance(clientType) ??
                throw new InvalidOperationException("无法创建 Windows 音频策略服务。");
            var policy = (IPolicyConfig)client;
            SetDefaultEndpoint(policy, deviceId, ERole.Console);
            SetDefaultEndpoint(policy, deviceId, ERole.Multimedia);
        }
        finally
        {
            // 此服务不持有长期 COM 状态；每次调用结束都必须释放 RCW。
            // The service keeps no COM state; release the RCW after every call.
            ReleaseComObject(client);
        }
    }

    private static void SetDefaultEndpoint(
        IPolicyConfig policy,
        string deviceId,
        ERole role)
    {
        Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    private enum ERole
    {
        Console,
        Multimedia,
        Communications
    }

    // vtable 顺序来自 Windows PolicyConfig 的实际 ABI；方法即使未使用也不能删除。
    // The vtable follows the observed PolicyConfig ABI; unused methods keep slot order.
    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat(string deviceId, out nint format);
        int GetDeviceFormat(string deviceId, int defaultFormat, out nint format);
        int ResetDeviceFormat(string deviceId);
        int SetDeviceFormat(string deviceId, nint endpointFormat, nint mixFormat);
        int GetProcessingPeriod(string deviceId, int defaultPeriod, out long period, out long minimumPeriod);
        int SetProcessingPeriod(string deviceId, ref long period);
        int GetShareMode(string deviceId, out nint mode);
        int SetShareMode(string deviceId, nint mode);
        int GetPropertyValue(string deviceId, int store, nint key, out nint value);
        int SetPropertyValue(string deviceId, int store, nint key, nint value);
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }
}
