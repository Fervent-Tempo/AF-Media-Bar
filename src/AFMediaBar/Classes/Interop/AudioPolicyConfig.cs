// Windows default-endpoint switching is not exposed by NAudio. This adapter owns and releases the policy COM client per call.
using System.Runtime.InteropServices;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Interop;

internal static class AudioPolicyConfig
{
    private static readonly Guid PolicyConfigClientClassId = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    internal static void SetDefaultDevice(string policyDeviceId, bool communications, Func<bool> isCurrent)
    {
        if (string.IsNullOrWhiteSpace(policyDeviceId))
            return;

        object? client = null;
        try
        {
            var type = Type.GetTypeFromCLSID(PolicyConfigClientClassId, throwOnError: true)!;
            client = Activator.CreateInstance(type) ??
                throw new InvalidOperationException(Translations.Get("Audio.Error.CreatePolicyConfig"));
            var policy = (IPolicyConfig)client;
            var roles = communications ? new[] { ERole.Console, ERole.Multimedia, ERole.Communications } : new[] { ERole.Console, ERole.Multimedia };
            foreach (var role in roles)
            {
                if (!isCurrent()) throw new OperationCanceledException();
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(policyDeviceId, role));
            }
        }
        finally
        {
            if (client is not null && Marshal.IsComObject(client))
            {
                Marshal.ReleaseComObject(client);
            }
        }
    }

    private enum ERole { Console, Multimedia, Communications }

    // vtable 顺序来自 Windows PolicyConfig ABI，未使用槽位仍须保留。
    // The vtable follows the Windows PolicyConfig ABI; unused slots must remain.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat(string id, out nint format);
        [PreserveSig]
        int GetDeviceFormat(string id, int defaultFormat, out nint format);
        [PreserveSig]
        int ResetDeviceFormat(string id);
        [PreserveSig]
        int SetDeviceFormat(string id, nint endpointFormat, nint mixFormat);
        [PreserveSig]
        int GetProcessingPeriod(string id, int defaultPeriod, out long period, out long minimumPeriod);
        [PreserveSig]
        int SetProcessingPeriod(string id, ref long period);
        [PreserveSig]
        int GetShareMode(string id, out nint mode);
        [PreserveSig]
        int SetShareMode(string id, nint mode);
        [PreserveSig]
        int GetPropertyValue(string id, int store, nint key, out nint value);
        [PreserveSig]
        int SetPropertyValue(string id, int store, nint key, nint value);
        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
        [PreserveSig]
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
