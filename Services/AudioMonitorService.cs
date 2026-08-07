using System.Runtime.InteropServices;

namespace TaskbarPlayer.Services;

/// <summary>
/// Reads the default output endpoint's peak meter without opening an audio stream.
/// </summary>
internal sealed class AudioMonitorService : IDisposable
{
    private static readonly Guid AudioMeterInformationId =
        new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioMeterInformation? _meter;
    private bool _disposed;

    internal float GetPeakValue()
    {
        if (_disposed || !EnsureMeter())
        {
            return 0;
        }

        try
        {
            var result = _meter!.GetPeakValue(out var peak);
            if (result < 0)
            {
                ReleaseMeter();
                return 0;
            }

            return Math.Clamp(peak, 0f, 1f);
        }
        catch (Exception)
        {
            ReleaseMeter();
            return 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseMeter();
        ReleaseComObject(ref _deviceEnumerator);
        GC.SuppressFinalize(this);
    }

    private bool EnsureMeter()
    {
        if (_meter is not null)
        {
            return true;
        }

        try
        {
            _deviceEnumerator ??= (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;

            if (_deviceEnumerator.GetDefaultAudioEndpoint(
                    EDataFlow.Render,
                    ERole.Multimedia,
                    out _device) < 0 ||
                _device is null)
            {
                return false;
            }

            var interfaceId = AudioMeterInformationId;
            if (_device.Activate(
                    ref interfaceId,
                    ClsCtx.All,
                    nint.Zero,
                    out var meter) < 0 ||
                meter is not IAudioMeterInformation audioMeter)
            {
                ReleaseMeter();
                return false;
            }

            _meter = audioMeter;
            return true;
        }
        catch (Exception)
        {
            ReleaseMeter();
            return false;
        }
    }

    private void ReleaseMeter()
    {
        ReleaseComObject(ref _meter);
        ReleaseComObject(ref _device);
    }

    private static void ReleaseComObject<T>(ref T? value)
        where T : class
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }

        value = null;
    }

    private enum EDataFlow
    {
        Render,
        Capture,
        All
    }

    private enum ERole
    {
        Console,
        Multimedia,
        Communications
    }

    [Flags]
    private enum ClsCtx
    {
        InprocServer = 0x1,
        InprocHandler = 0x2,
        LocalServer = 0x4,
        All = InprocServer | InprocHandler | LocalServer
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out nint devices);

        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);

        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        int RegisterEndpointNotificationCallback(nint client);

        int UnregisterEndpointNotificationCallback(nint client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(
            ref Guid interfaceId,
            ClsCtx classContext,
            nint activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);

        int OpenPropertyStore(int accessMode, out nint properties);

        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        int GetState(out int state);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        int GetPeakValue(out float peak);

        int GetMeteringChannelCount(out int channelCount);

        int GetChannelsPeakValues(int channelCount, out float peaks);

        int QueryHardwareSupport(out int hardwareSupportMask);
    }
}
