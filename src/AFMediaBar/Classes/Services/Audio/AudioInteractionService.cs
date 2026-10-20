using AFMediaBar.Classes.Models;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 为任务栏、完整面板和托盘提供同一组设备与应用音量操作。
///
/// 返回的文本是用户可见的提示（滚轮结果），因此按当前界面语言取值；设备名与应用名来自系统，原样保留。
/// Provides one device/application-volume action boundary for taskbar, full panel, and tray surfaces.
///
/// The returned text is a user-visible tooltip (a wheel result) and therefore follows the active interface language; device
/// and application names come from the system and are kept as they are.
/// </summary>
public sealed class AudioInteractionService
{
    private const int VolumeStepPercent = 2;
    private readonly AudioDeviceService _deviceService;
    private readonly ApplicationVolumeService _volumeService;
    private readonly MediaSessionService _mediaSessionService;
    private readonly AudioMonitorService _audioMonitorService;
    private readonly AudioDeviceSwitchCoordinator _switches;
    private readonly IForegroundAudioApplicationResolver _foreground;
    private readonly Dictionary<(object Owner, AudioDeviceDirection Direction), AudioDeviceTarget> _gestureTargets = new();
    private readonly Dictionary<(object Owner, AudioDeviceDirection Direction), Task<ApplicationAudioSnapshot?>> _captures = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, OwnerState> _owners = new();
    private sealed class OwnerState { public int Version; }
    private readonly object _targetGate = new();
    public AudioDeviceSwitchCoordinator DeviceSwitches => _switches;
    private readonly SemaphoreSlim _volumeGate = new(1, 1);


    public AudioInteractionService(
        AudioDeviceService deviceService,
        ApplicationVolumeService volumeService,
        MediaSessionService mediaSessionService,
        AudioMonitorService audioMonitorService,
        AudioDeviceSwitchCoordinator switches,
        IForegroundAudioApplicationResolver foreground)
    {
        _deviceService = deviceService;
        _volumeService = volumeService;
        _mediaSessionService = mediaSessionService;
        _audioMonitorService = audioMonitorService;
        _switches = switches;
        _foreground = foreground;
    }

    public Task<string> GetDeviceDescriptionAsync(AudioDeviceDirection direction) => _deviceService.GetDefaultDescriptionAsync(direction);
    public Task<IReadOnlyList<AudioDeviceOption>> GetInputDevicesAsync() => _deviceService.GetDevicesAsync(AudioDeviceDirection.Input);

    public Task<IReadOnlyList<AudioDeviceOption>> GetOutputDevicesAsync() =>
        _deviceService.GetRenderDevicesAsync();

    public IReadOnlyList<ApplicationVolumeSnapshot> GetApplications() =>
        _volumeService.GetApplications(_mediaSessionService.SelectedSourceId, _mediaSessionService.SelectedSourceName);

    public ApplicationVolumeSnapshot? GetCurrentMediaVolume() =>
        _volumeService.GetCurrentMediaVolume(_mediaSessionService.SelectedSourceId, _mediaSessionService.SelectedSourceName);

    public bool SetApplicationVolume(string processName, int volumePercent) =>
        _volumeService.SetApplicationVolume(processName, volumePercent);

    public async Task<string> AdjustCurrentMediaVolumeAsync(int signedSteps, CancellationToken cancellationToken = default)
    {
        if (signedSteps == 0)
            return Translations.Get("Audio.Volume.Unchanged");

        await _volumeGate.WaitAsync(cancellationToken);
        try
        {
            var current = await Task.Run(GetCurrentMediaVolume);
            if (current is null)
                return Translations.Get("Audio.Volume.Unavailable");

            cancellationToken.ThrowIfCancellationRequested();
            var next = Math.Clamp(current.VolumePercent + signedSteps * VolumeStepPercent, 0, 100);
            await Task.Run(() => { cancellationToken.ThrowIfCancellationRequested(); return SetApplicationVolume(current.ProcessName, next); }, cancellationToken);
            return Translations.Format("Audio.Volume.Value", current.DisplayName, next);
        }
        finally
        {
            _volumeGate.Release();
        }
    }


    public async Task SetOutputDeviceAsync(AudioDeviceOption device) =>
        await _switches.SelectAsync(new(device.Direction), device, this);


    public async Task<AudioDeviceTarget?> ResolveTargetAsync(AudioDeviceDirection direction,
        AFMediaBar.Classes.Settings.AudioDeviceSwitchTarget scope, object owner, bool wheel)
    {
        if (scope == AFMediaBar.Classes.Settings.AudioDeviceSwitchTarget.Disabled) return null;
        if (scope == AFMediaBar.Classes.Settings.AudioDeviceSwitchTarget.SystemDefault) return new(direction);
        var ownerState = _owners.GetValue(owner, static _ => new OwnerState());
        var ownerVersion = Volatile.Read(ref ownerState.Version);
        var key = (owner, direction);
        AudioDeviceTarget? previous;
        lock (_targetGate) previous = _gestureTargets.GetValueOrDefault(key);
        if (wheel && previous is not null && _switches.HasPending(previous)) return previous;
        Task<ApplicationAudioSnapshot?> capture;
        TaskCompletionSource<ApplicationAudioSnapshot?>? source = null;
        lock (_targetGate)
        {
            if (!_captures.TryGetValue(key, out capture!))
            {
                source = new(TaskCreationOptions.RunContinuationsAsynchronously);
                capture = source.Task;
                _captures[key] = capture;
            }
        }
        if (source is not null) _ = CaptureForegroundAsync(source);
        ApplicationAudioSnapshot? app;
        try { app = await capture; }
        finally
        {
            lock (_targetGate)
                if (ReferenceEquals(_captures.GetValueOrDefault(key), capture)) _captures.Remove(key);
        }
        if (app is null) return null;
        var target = new AudioDeviceTarget(direction, app);
        lock (_targetGate)
        {
            if (ownerVersion != ownerState.Version) return null;
            if (wheel) _gestureTargets[key] = target;
        }
        return target;
    }

    private async Task CaptureForegroundAsync(TaskCompletionSource<ApplicationAudioSnapshot?> source)
    {
        try { source.TrySetResult(await _foreground.CaptureAsync()); }
        catch (Exception exception) { source.TrySetException(exception); }
    }

    public void CancelDeviceOwner(object owner)
    {
        lock (_targetGate)
        {
            _owners.GetValue(owner, static _ => new OwnerState()).Version++;
            foreach (var key in _captures.Keys.Where(key => ReferenceEquals(key.Owner, owner)).ToArray()) _captures.Remove(key);
            foreach (var key in _gestureTargets.Keys.Where(key => ReferenceEquals(key.Owner, owner)).ToArray()) _gestureTargets.Remove(key);
        }
        _switches.CancelOwner(owner);
    }

    public Task<string> CycleOutputDeviceAsync(int signedSteps, bool deferApply, object? owner = null, CancellationToken cancellationToken = default) =>
        CycleDeviceAsync(AudioDeviceDirection.Output, signedSteps, owner, cancellationToken);
    public async Task<string> CycleDeviceAsync(AudioDeviceDirection direction, int signedSteps, object? owner = null, CancellationToken cancellationToken = default)
    {
        var target = new AudioDeviceTarget(direction);
        cancellationToken.ThrowIfCancellationRequested();
        var device = await _switches.PreviewAsync(target, signedSteps, owner ?? this);
        return device is null ? Translations.Get("Audio.Device.Unavailable") : device.DeviceHint;
    }

}
