using System.Diagnostics;
using System.Runtime.InteropServices;
using AFMediaBar.Abstractions;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 将 GSMTC 媒体来源匹配到 Core Audio 会话，并读取或设置应用音量。
/// Matches GSMTC sources to Core Audio sessions and reads or sets app volume.
/// </summary>
public sealed class ApplicationVolumeService
{
    private const int MaximumRememberedSources = 64;
    private static readonly Guid DeviceEnumeratorClassId =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid AudioSessionManager2Id =
        new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    private static readonly Guid VolumeEventContext =
        new("4F74C2B5-7E7B-4A02-ABCB-EF8FA77BD65A");
    private readonly object _syncRoot = new();
    // 有些 GSMTC SourceId 不等于进程名，缓存成功匹配可稳定后续调节。
    // Some GSMTC source IDs differ from process names; cache successful matches.
    private readonly Dictionary<string, string> _sourceProcessNames = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _sourceProcessOrder = new();
    private readonly IStringLocalizer _localizer;

    public ApplicationVolumeService(IStringLocalizer localizer)
    {
        _localizer = localizer;
    }

    public ApplicationVolumeSnapshot? GetCurrentMediaVolume(
        string? sourceId,
        string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceId) &&
            string.IsNullOrWhiteSpace(sourceName))
        {
            return null;
        }

        lock (_syncRoot)
        {
            return GetCurrentMediaVolumeCore(sourceId, sourceName);
        }
    }

    public bool SetApplicationVolume(string processName, int volumePercent)
    {
        lock (_syncRoot)
        {
            var target = Math.Clamp(volumePercent, 0, 100) / 100f;
            var changed = false;
            ForEachSession((candidate, _, _, _, _, volume) =>
            {
                if (!string.Equals(
                        candidate,
                        processName,
                        StringComparison.OrdinalIgnoreCase))
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

    private ApplicationVolumeSnapshot? GetCurrentMediaVolumeCore(
        string? sourceId,
        string? sourceName)
    {
        var applications = new Dictionary<string, ApplicationVolumeAggregate>(
            StringComparer.OrdinalIgnoreCase);
        ForEachSession((processName, displayName, state, level, muted, _) =>
        {
            if (!applications.TryGetValue(processName, out var application))
            {
                application = new ApplicationVolumeAggregate(processName);
                applications.Add(processName, application);
            }

            application.AddSession(displayName, state, level, muted);
        });

        var sourceKey = GetSourceKey(sourceId, sourceName);
        string? rememberedProcessName = null;
        var hadRememberedProcess = sourceKey is not null &&
            _sourceProcessNames.TryGetValue(sourceKey, out rememberedProcessName);
        if (hadRememberedProcess &&
            rememberedProcessName is not null &&
            applications.TryGetValue(rememberedProcessName, out var rememberedApplication))
        {
            return rememberedApplication.CreateSnapshot(
                sourceId,
                sourceName,
                _localizer.Get("Main.Media.UnknownSource"));
        }

        ApplicationVolumeAggregate? onlyActiveApplication = null;
        var hasMultipleActiveApplications = false;
        foreach (var application in applications.Values)
        {
            var unknownSourceName = _localizer.Get("Main.Media.UnknownSource");
            var snapshot = application.CreateSnapshot(
                sourceId,
                sourceName,
                unknownSourceName);
            if (MatchesCurrentMedia(
                    snapshot.ProcessName,
                    snapshot.DisplayName,
                    sourceId,
                    sourceName,
                    unknownSourceName))
            {
                RememberSourceProcess(sourceKey, snapshot.ProcessName);
                return snapshot;
            }

            if (!application.HasActiveSession)
            {
                continue;
            }

            if (onlyActiveApplication is null)
            {
                onlyActiveApplication = application;
            }
            else
            {
                hasMultipleActiveApplications = true;
            }
        }

        if (hadRememberedProcess || !string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        if (hasMultipleActiveApplications || onlyActiveApplication is null)
        {
            return null;
        }

        var fallback = onlyActiveApplication.CreateSnapshot(
            sourceId,
            sourceName,
            _localizer.Get("Main.Media.UnknownSource"));
        RememberSourceProcess(sourceKey, fallback.ProcessName);
        return fallback;
    }

    private void RememberSourceProcess(string? sourceKey, string processName)
    {
        if (sourceKey is not null)
        {
            if (!_sourceProcessNames.ContainsKey(sourceKey))
            {
                while (_sourceProcessNames.Count >= MaximumRememberedSources &&
                    _sourceProcessOrder.TryDequeue(out var oldestSource))
                {
                    _sourceProcessNames.Remove(oldestSource);
                }

                _sourceProcessOrder.Enqueue(sourceKey);
            }

            _sourceProcessNames[sourceKey] = processName;
        }
    }

    private static string? GetSourceKey(string? sourceId, string? sourceName)
    {
        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            return $"id:{sourceId}";
        }

        return string.IsNullOrWhiteSpace(sourceName)
            ? null
            : $"name:{sourceName}";
    }

    private static string GetApplicationDisplayName(
        string processName,
        string? sessionDisplayName,
        string? sourceId,
        string? sourceName,
        string unknownSourceName)
    {
        if (MatchesProcess(sourceId, processName) && !string.IsNullOrWhiteSpace(sourceName))
        {
            return sourceName;
        }

        return sessionDisplayName ?? MediaSourceNameFormatter.GetDisplayName(
            processName,
            unknownSourceName);
    }

    private static bool MatchesCurrentMedia(
        string processName,
        string displayName,
        string? sourceId,
        string? sourceName,
        string unknownSourceName)
    {
        if (MatchesProcess(sourceId, processName))
        {
            return true;
        }

        var mappedProcessName = MediaSourceNameFormatter.GetDisplayName(
            processName,
            unknownSourceName);
        return !string.IsNullOrWhiteSpace(sourceName) &&
            (string.Equals(displayName, sourceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    mappedProcessName,
                    sourceName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesProcess(string? sourceId, string processName)
    {
        return !string.IsNullOrWhiteSpace(sourceId) &&
            sourceId.Contains(processName, StringComparison.OrdinalIgnoreCase);
    }

    private void ForEachSession(
        Action<string, string?, AudioSessionState, float, bool, ISimpleAudioVolume> visitor)
    {
        // Core Audio 对象仅在本次枚举内有效，所有退出路径都必须释放 COM RCW。
        // Core Audio objects are call-scoped; every exit path must release each COM RCW.
        object? deviceEnumeratorObject = null;
        IMMDevice? device = null;
        object? managerObject = null;
        IAudioSessionEnumerator? sessionEnumerator = null;
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(
                DeviceEnumeratorClassId,
                throwOnError: true)!;
            deviceEnumeratorObject = Activator.CreateInstance(enumeratorType) ??
                throw new InvalidOperationException(
                    _localizer.Get("Msg.AudioDeviceEnumeratorCreateFailed"));
            var deviceEnumerator = (IMMDeviceEnumerator)deviceEnumeratorObject;
            Marshal.ThrowExceptionForHR(deviceEnumerator.GetDefaultAudioEndpoint(
                EDataFlow.Render,
                ERole.Multimedia,
                out device));

            var managerId = AudioSessionManager2Id;
            Marshal.ThrowExceptionForHR(device.Activate(
                ref managerId,
                ClsCtx.All,
                nint.Zero,
                out managerObject));
            var manager = (IAudioSessionManager2)managerObject;
            Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out sessionEnumerator));
            Marshal.ThrowExceptionForHR(sessionEnumerator.GetCount(out var count));
            for (var index = 0; index < count; index++)
            {
                IAudioSessionControl? control = null;
                try
                {
                    Marshal.ThrowExceptionForHR(sessionEnumerator.GetSession(index, out control));
                    var control2 = (IAudioSessionControl2)control;
                    Marshal.ThrowExceptionForHR(control2.GetState(out var state));
                    if (state == AudioSessionState.Expired)
                    {
                        continue;
                    }

                    Marshal.ThrowExceptionForHR(control2.GetProcessId(out var processId));
                    if (processId == 0 || processId == Environment.ProcessId)
                    {
                        continue;
                    }

                    var processName = GetProcessName(processId);
                    if (string.IsNullOrWhiteSpace(processName))
                    {
                        continue;
                    }

                    _ = control.GetDisplayName(out var displayName);
                    var simpleVolume = (ISimpleAudioVolume)control;
                    Marshal.ThrowExceptionForHR(simpleVolume.GetMasterVolume(out var level));
                    Marshal.ThrowExceptionForHR(simpleVolume.GetMute(out var muted));
                    visitor(processName, displayName, state, level, muted, simpleVolume);
                }
                catch (COMException)
                {
                    // 枚举期间音频会话可能失效。 / Sessions may expire during enumeration.
                }
                catch (InvalidCastException)
                {
                    // 部分系统会话不提供应用音量接口。 / Some system sessions expose no app volume.
                }
                finally
                {
                    ReleaseComObject(control);
                }
            }
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);
            ReleaseComObject(managerObject);
            ReleaseComObject(device);
            ReleaseComObject(deviceEnumeratorObject);
        }
    }

    private static string? GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    private sealed class ApplicationVolumeAggregate(string processName)
    {
        private double _volumeTotal;
        private int _sessionCount;
        private bool _allMuted = true;
        private string? _displayName;

        internal bool HasActiveSession { get; private set; }

        internal void AddSession(
            string? displayName,
            AudioSessionState state,
            float volume,
            bool muted)
        {
            if (_displayName is null &&
                !string.IsNullOrWhiteSpace(displayName) &&
                !displayName.StartsWith('@'))
            {
                _displayName = displayName;
            }

            _volumeTotal += volume;
            _sessionCount++;
            _allMuted &= muted;
            HasActiveSession |= state == AudioSessionState.Active;
        }

        internal ApplicationVolumeSnapshot CreateSnapshot(
            string? sourceId,
            string? sourceName,
            string unknownSourceName)
        {
            var volumePercent = _sessionCount == 0
                ? 0
                : (int)Math.Round(_volumeTotal / _sessionCount * 100);
            return new ApplicationVolumeSnapshot(
                processName,
                GetApplicationDisplayName(
                    processName,
                    _displayName,
                    sourceId,
                    sourceName,
                    unknownSourceName),
                volumePercent,
                _allMuted);
        }
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

    private enum AudioSessionState
    {
        Inactive,
        Active,
        Expired
    }

    [Flags]
    private enum ClsCtx
    {
        InprocServer = 0x1,
        InprocHandler = 0x2,
        LocalServer = 0x4,
        All = InprocServer | InprocHandler | LocalServer
    }

    // 以下声明来自 Windows Core Audio 的 mmdeviceapi.h 与 audiopolicy.h。
    // These declarations mirror Windows Core Audio mmdeviceapi.h and audiopolicy.h.
    // 方法顺序必须保持 ABI 一致，返回值按 HRESULT 处理。
    // Method order is ABI-sensitive and return values are HRESULTs.
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
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int GetAudioSessionControl(ref Guid sessionId, uint streamFlags, out nint control);
        int GetSimpleAudioVolume(ref Guid sessionId, uint streamFlags, out nint volume);
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
        int RegisterSessionNotification(nint notification);
        int UnregisterSessionNotification(nint notification);
        int RegisterDuckNotification(string sessionId, nint notification);
        int UnregisterDuckNotification(nint notification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int count);
        int GetSession(int index, out IAudioSessionControl control);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        int GetState(out AudioSessionState state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        int SetDisplayName(string displayName, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        int SetIconPath(string iconPath, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingId);
        int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        int RegisterAudioSessionNotification(nint client);
        int UnregisterAudioSessionNotification(nint client);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        int GetState(out AudioSessionState state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        int SetDisplayName(string displayName, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        int SetIconPath(string iconPath, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingId);
        int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        int RegisterAudioSessionNotification(nint client);
        int UnregisterAudioSessionNotification(nint client);
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionId);
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string instanceId);
        int GetProcessId(out uint processId);
        int IsSystemSoundsSession();
        int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        int SetMasterVolume(float level, ref Guid eventContext);
        int GetMasterVolume(out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
