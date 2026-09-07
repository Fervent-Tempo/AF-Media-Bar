using System.Runtime.InteropServices;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 枚举所有活动输出端点的 Core Audio 会话，并按进程聚合读取或设置应用音量。
/// Enumerates Core Audio sessions across all active render endpoints and reads or sets volume aggregated by process.
///
/// 注意：跨端点聚合可保持音量合成器列表稳定，但不会迁移应用已有的音频流。
/// Note: Cross-endpoint aggregation keeps the mixer list stable but does not migrate an application's existing audio stream.
/// </summary>
public sealed class ApplicationVolumeService
{
    private const string SystemSoundsProcessName = "AFMediaBar.SystemSounds";
    private static readonly Guid DeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid AudioSessionManager2Id = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    private static readonly Guid VolumeEventContext = new("4F74C2B5-7E7B-4A02-ABCB-EF8FA77BD65A");
    private const int DeviceStateActive = 0x00000001;
    private readonly object _gate = new();
    private readonly MediaSourceProcessResolver _processResolver;
    private readonly ApplicationIconService _iconService;
    private readonly AudioProcessInfoService _processInfo;

    public ApplicationVolumeService(
        MediaSourceProcessResolver processResolver,
        ApplicationIconService iconService,
        AudioProcessInfoService processInfo)
    {
        _processResolver = processResolver;
        _iconService = iconService;
        _processInfo = processInfo;
    }

    public IReadOnlyList<ApplicationVolumeSnapshot> GetApplications(string? sourceId, string? sourceName) =>
        GetApplicationsCore(sourceId, sourceName, includeIcons: true);

    public ApplicationVolumeSnapshot? GetCurrentMediaVolume(string? sourceId, string? sourceName) =>
        GetApplicationsCore(sourceId, sourceName, includeIcons: false)
            .FirstOrDefault(value => value.IsCurrentMedia);

    private IReadOnlyList<ApplicationVolumeSnapshot> GetApplicationsCore(
        string? sourceId,
        string? sourceName,
        bool includeIcons)
    {
        lock (_gate)
        {
            var aggregates = new Dictionary<string, Aggregate>(StringComparer.OrdinalIgnoreCase);
            ForEachSession((processId, processName, displayName, iconPath, state, level, muted, _) =>
            {
                if (!aggregates.TryGetValue(processName, out var aggregate))
                {
                    aggregate = new Aggregate(processName, processName == SystemSoundsProcessName);
                    aggregates.Add(processName, aggregate);
                }

                aggregate.Add(
                    displayName,
                    state,
                    level,
                    muted,
                    includeIcons ? _iconService.GetIconData(processId, iconPath) : null);
            });

            var snapshots = aggregates.Values
                .Select(value => value.Create(_processResolver.Matches(
                    sourceId,
                    sourceName,
                    value.ProcessName,
                    value.DisplayName)))
                .OrderByDescending(value => value.IsCurrentMedia)
                .ThenBy(value => value.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            var current = snapshots.FirstOrDefault(value => value.IsCurrentMedia);
            if (current is not null)
            {
                _processResolver.Remember(sourceId, sourceName, current.ProcessName);
            }

            return snapshots;
        }
    }

    public bool SetApplicationVolume(string processName, int volumePercent)
    {
        lock (_gate)
        {
            var target = Math.Clamp(volumePercent, 0, 100) / 100f;
            var changed = false;
            ForEachSession((_, candidate, _, _, _, _, _, volume) =>
            {
                if (!string.Equals(candidate, processName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var context = VolumeEventContext;
                Marshal.ThrowExceptionForHR(volume.SetMasterVolume(target, ref context));
                if (target > 0)
                {
                    context = VolumeEventContext;
                    Marshal.ThrowExceptionForHR(volume.SetMute(false, ref context));
                }

                changed = true;
            });
            return changed;
        }
    }

    private void ForEachSession(Action<uint, string, string?, string?, AudioSessionState, float, bool, ISimpleAudioVolume> visitor)
    {
        object? enumeratorObject = null;
        IMMDeviceCollection? devices = null;
        try
        {
            var type = Type.GetTypeFromCLSID(DeviceEnumeratorClassId, throwOnError: true)!;
            enumeratorObject = Activator.CreateInstance(type) ??
                throw new InvalidOperationException("无法创建 Windows 音频设备枚举器。");
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out devices));
            Marshal.ThrowExceptionForHR(devices.GetCount(out var deviceCount));

            for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
            {
                IMMDevice? device = null;
                try
                {
                    Marshal.ThrowExceptionForHR(devices.Item(deviceIndex, out device));
                    ForEachSessionOnDevice(device, visitor);
                }
                catch (COMException)
                {
                    // 切换或断开设备时端点可能在枚举期间失效。 / An endpoint may disappear during enumeration while devices switch or disconnect.
                }
                finally
                {
                    Release(device);
                }
            }
        }
        finally
        {
            Release(devices);
            Release(enumeratorObject);
        }
    }

    private void ForEachSessionOnDevice(
        IMMDevice device,
        Action<uint, string, string?, string?, AudioSessionState, float, bool, ISimpleAudioVolume> visitor)
    {
        object? managerObject = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            var managerId = AudioSessionManager2Id;
            Marshal.ThrowExceptionForHR(device.Activate(ref managerId, ClsCtx.All, nint.Zero, out managerObject));
            var manager = (IAudioSessionManager2)managerObject;
            Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out sessions));
            Marshal.ThrowExceptionForHR(sessions.GetCount(out var count));

            for (var index = 0; index < count; index++)
            {
                IAudioSessionControl? control = null;
                try
                {
                    Marshal.ThrowExceptionForHR(sessions.GetSession(index, out control));
                    var control2 = (IAudioSessionControl2)control;
                    Marshal.ThrowExceptionForHR(control2.GetState(out var state));
                    if (state == AudioSessionState.Expired)
                    {
                        continue;
                    }

                    Marshal.ThrowExceptionForHR(control2.GetProcessId(out var processId));
                    if (processId == Environment.ProcessId)
                    {
                        continue;
                    }

                    // Core Audio 使用 PID 0 表示系统声音。IsSystemSoundsSession 在部分会话代理上会产生错误归类。
                    // Core Audio uses PID 0 for system sounds. IsSystemSoundsSession can misclassify proxied sessions.
                    var processName = processId == 0
                        ? SystemSoundsProcessName
                        : _processInfo.GetProcessName(processId) ?? $"AFMediaBar.Process.{processId}";
                    if (string.IsNullOrWhiteSpace(processName))
                    {
                        continue;
                    }

                    _ = control.GetDisplayName(out var displayName);
                    _ = control.GetIconPath(out var iconPath);
                    var volume = (ISimpleAudioVolume)control;
                    Marshal.ThrowExceptionForHR(volume.GetMasterVolume(out var level));
                    Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
                    visitor(processId, processName, displayName, iconPath, state, level, muted, volume);
                }
                catch (COMException)
                {
                    // 枚举期间会话可能失效。 / Sessions may expire during enumeration.
                }
                catch (InvalidCastException)
                {
                    // 某些系统会话没有应用音量接口。 / Some system sessions have no app-volume interface.
                }
                finally
                {
                    Release(control);
                }
            }
        }
        finally
        {
            Release(sessions);
            Release(managerObject);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    private sealed class Aggregate(string processName, bool isSystemSounds)
    {
        private double _total;
        private int _count;
        private bool _allMuted = true;
        private string? _sessionName;
        private byte[]? _iconData;

        public string ProcessName { get; } = processName;
        public string DisplayName => isSystemSounds
            ? "系统声音"
            : string.IsNullOrWhiteSpace(_sessionName) || _sessionName.StartsWith('@')
            ? MediaSourceNameFormatter.GetDisplayName(ProcessName, ProcessName)
            : _sessionName;

        public void Add(string? displayName, AudioSessionState state, float volume, bool muted, byte[]? iconData)
        {
            if (string.IsNullOrWhiteSpace(_sessionName) && !string.IsNullOrWhiteSpace(displayName))
            {
                _sessionName = displayName;
            }

            _total += volume;
            _count++;
            _allMuted &= muted;
            _iconData ??= iconData;
        }

        public ApplicationVolumeSnapshot Create(bool isCurrentMedia) => new(
            ProcessName,
            DisplayName,
            _count == 0 ? 0 : (int)Math.Round(_total / _count * 100),
            _allMuted,
            isCurrentMedia,
            _iconData);
    }

    private enum EDataFlow { Render, Capture, All }
    private enum ERole { Console, Multimedia, Communications }
    private enum AudioSessionState { Inactive, Active, Expired }
    [Flags] private enum ClsCtx { InprocServer = 1, InprocHandler = 2, LocalServer = 4, All = 7 }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow flow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(nint client);
        int UnregisterEndpointNotificationCallback(nint client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out int count);
        int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid interfaceId, ClsCtx context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        int OpenPropertyStore(int accessMode, out nint properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int GetAudioSessionControl(ref Guid id, uint flags, out nint control);
        int GetSimpleAudioVolume(ref Guid id, uint flags, out nint volume);
        int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        int RegisterSessionNotification(nint notification);
        int UnregisterSessionNotification(nint notification);
        int RegisterDuckNotification(string id, nint notification);
        int UnregisterDuckNotification(nint notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int count);
        int GetSession(int index, out IAudioSessionControl control);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        int GetState(out AudioSessionState state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int SetDisplayName(string name, ref Guid context);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        int SetIconPath(string path, ref Guid context);
        int GetGroupingParam(out Guid id);
        int SetGroupingParam(ref Guid id, ref Guid context);
        int RegisterAudioSessionNotification(nint client);
        int UnregisterAudioSessionNotification(nint client);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        int GetState(out AudioSessionState state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int SetDisplayName(string name, ref Guid context);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        int SetIconPath(string path, ref Guid context);
        int GetGroupingParam(out Guid id);
        int SetGroupingParam(ref Guid id, ref Guid context);
        int RegisterAudioSessionNotification(nint client);
        int UnregisterAudioSessionNotification(nint client);
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetProcessId(out uint processId);
        int IsSystemSoundsSession();
        int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        int SetMasterVolume(float level, ref Guid context);
        int GetMasterVolume(out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
