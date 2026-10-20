// Coordinates application-volume policy; NAudio owns Core Audio interop and scoped native resources.
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using AFMediaBar.Classes.Models;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 枚举所有活动输出端点的 Core Audio 会话，并按进程聚合读取或设置应用音量。
/// Enumerates Core Audio sessions across all active render endpoints and reads or sets volume aggregated by process.
///
/// 注意：跨端点聚合可保持音量合成器列表稳定，但不会迁移应用已有的音频流。
/// Note: Cross-endpoint aggregation keeps the mixer list stable but does not migrate an application's existing audio stream.
///
/// 会话显示名与进程名来自系统；只有系统声音这一条的名字由本程序给出，因此按当前界面语言取值。
/// Session display names and process names come from the system; only the system-sounds entry is named by this application
/// and therefore follows the active interface language.
/// </summary>
public sealed class ApplicationVolumeService : IApplicationAudioControl
{
    private const string SystemSoundsProcessName = "AFMediaBar.SystemSounds";
    private readonly object _gate;
    private readonly MediaSourceProcessResolver _processResolver;
    private readonly ApplicationIconService _iconService;
    private readonly AudioProcessInfoService _processInfo;
    private readonly ApplicationVolumeMemoryService _volumeMemory;

    /// <summary>
    /// 创建应用音量协调器，并使用共享来源解析器将媒体来源映射到音频会话。
    /// Creates the application-volume coordinator and maps media sources to audio sessions through the shared resolver.
    /// </summary>
    public ApplicationVolumeService(
        MediaSourceProcessResolver processResolver,
        ApplicationIconService iconService,
        AudioProcessInfoService processInfo,
        ApplicationVolumeMemoryService volumeMemory)
    {
        _processResolver = processResolver;
        _iconService = iconService;
        _processInfo = processInfo;
        _volumeMemory = volumeMemory;
        _gate = volumeMemory.WriteGate;
    }

    /// <summary>
    /// 枚举所有活动渲染端点的应用音频会话，并标记与媒体来源匹配的项。
    /// Enumerates application audio sessions on all active render endpoints and marks entries matching the media source.
    /// </summary>
    public IReadOnlyList<ApplicationVolumeSnapshot> GetApplications(string? sourceId, string? sourceName) =>
        GetApplicationsCore(sourceId, sourceName, includeIcons: true);

    /// <summary>
    /// 返回当前媒体来源对应的应用音量快照，无法匹配时返回空值。
    /// Returns the application-volume snapshot matching the current media source, or null when no session matches.
    /// </summary>
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
            ForEachSession((processId, processName, displayName, iconPath, state, level, muted, _, _) =>
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

    /// <summary>
    /// 将指定进程的所有匹配音频会话设置为限制后的百分比，并报告是否至少更新一项。
    /// Applies the clamped percentage to all matching audio sessions for a process and reports whether any session changed.
    /// </summary>
    public bool SetApplicationVolume(string processName, int volumePercent)
    {
        lock (_gate)
        {
            var target = Math.Clamp(volumePercent, 0, 100) / 100f;
            var changed = false;
            ForEachSession((processId, candidate, _, _, _, _, _, volume, session) =>
            {
                if (!string.Equals(candidate, processName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                volume.Volume = target;
                RememberVolume(processId, session, volumePercent);
                if (target > 0)
                {
                    volume.Mute = false;
                }

                changed = true;
            });
            return changed;
        }
    }

    /// <summary>在后台队列中写入并回读；静音不修改会话音量，所有原生包装在本次枚举内释放。</summary>
    public ApplicationAudioControlResult Apply(string processName, int? volumePercent, bool? muted, Func<bool> isCurrent)
    {
        lock (_gate)
        {
            if (!isCurrent()) return new(new(AudioDeviceOperationStatus.Cancelled));
            var aggregate = new Aggregate(processName, processName == SystemSoundsProcessName);
            var matched = 0;
            var applied = 0;
            var failed = 0;
            ForEachSession((processId, candidate, name, icon, state, _, _, volume, session) =>
            {
                if (!string.Equals(candidate, processName, StringComparison.OrdinalIgnoreCase) || !isCurrent()) return;
                matched++;
                try
                {
                    if (volumePercent is { } percent)
                    {
                        volume.Volume = Math.Clamp(percent, 0, 100) / 100f;
                        RememberVolume(processId, session, percent);
                        if (percent > 0) volume.Mute = false;
                    }
                    if (muted is { } mute) volume.Mute = mute;
                    applied++;
                }
                catch (Exception) { failed++; }
                try { aggregate.Add(name, state, volume.Volume, volume.Mute, null); }
                catch (Exception) { failed++; }
            });
            if (!isCurrent()) return new(new(AudioDeviceOperationStatus.Cancelled));
            var status = matched == 0 ? AudioDeviceOperationStatus.Unavailable :
                failed == 0 ? AudioDeviceOperationStatus.Applied : applied > 0 ? AudioDeviceOperationStatus.Partial : AudioDeviceOperationStatus.Failed;
            return new(new(status), aggregate.Count > 0 ? aggregate.Create(false) : null);
        }
    }

    private void RememberVolume(uint processId, AudioSessionControl session, int percent)
    {
        if (processId == 0) return;
        try
        {
            var path = _processInfo.GetExecutablePath(processId);
            if (path is null) return;
            string? key = null;
            try { key = session.GetSessionInstanceIdentifier; }
            catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
            _volumeMemory.RecordAppliedVolume(path, percent, key);
        }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine($"[VolumeMemory] Record: {error.Message}"); }
    }

    private void ForEachSession(Action<uint, string, string?, string?, AudioSessionState, float, bool, SimpleAudioVolume, AudioSessionControl> visitor)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        for (var index = 0; index < devices.Count; index++)
        {
            try
            {
                using var device = devices[index];
                // MMDevice owns its manager; the manager owns the session collection.
                var sessions = device.AudioSessionManager.Sessions;
                for (var sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
                {
                    try
                    {
                        using var session = sessions[sessionIndex];
                        var state = session.State;
                        if (state == AudioSessionState.AudioSessionStateExpired)
                            continue;
                        var processId = session.GetProcessID;
                        if (processId == Environment.ProcessId)
                            continue;

                        // PID 0 is system sounds; preserve proxy-session classification.
                        var processName = processId == 0
                            ? SystemSoundsProcessName
                            : _processInfo.GetProcessName(processId) ?? $"AFMediaBar.Process.{processId}";
                        if (string.IsNullOrWhiteSpace(processName))
                            continue;
                        var volume = session.SimpleAudioVolume;
                        if (volume is null)
                            continue;
                        string? displayName = null;
                        string? iconPath = null;
                        try { displayName = session.DisplayName; } catch (COMException) { }
                        try { iconPath = session.IconPath; } catch (COMException) { }
                        visitor(processId, processName, displayName, iconPath, state,
                            volume.Volume, volume.Mute, volume, session);
                    }
                    catch (COMException)
                    {
                        // A session can expire between enumeration and access.
                    }
                    catch (InvalidCastException)
                    {
                        // Some system sessions do not expose application-volume interfaces.
                    }
                }
            }
            catch (COMException)
            {
                // A disconnected endpoint must not hide sessions on other devices.
            }
        }
    }

    private sealed class Aggregate(string processName, bool isSystemSounds)
    {
        private double _total;
        private int _count;
        private bool _allMuted = true;
        private string? _sessionName;
        private byte[]? _iconData;

        public int Count => _count;
        public string ProcessName { get; } = processName;
        public string DisplayName => isSystemSounds
            ? Translations.Get("Audio.Application.SystemSounds")
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

}
