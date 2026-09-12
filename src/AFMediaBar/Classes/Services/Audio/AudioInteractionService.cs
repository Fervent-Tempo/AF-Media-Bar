using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 为任务栏、完整面板和托盘提供同一组设备与应用音量操作。
/// Provides one device/application-volume action boundary for taskbar, full panel, and tray surfaces.
/// </summary>
public sealed class AudioInteractionService
{
    private const int VolumeStepPercent = 2;
    private static readonly TimeSpan WheelDeviceDelay = TimeSpan.FromMilliseconds(1200);
    private readonly AudioDeviceService _deviceService;
    private readonly ApplicationVolumeService _volumeService;
    private readonly MediaSessionService _mediaSessionService;
    private readonly AudioMonitorService _audioMonitorService;
    private readonly object _deviceGate = new();
    private readonly SemaphoreSlim _volumeGate = new(1, 1);
    private int _deviceApplyVersion;
    private string? _previewDeviceId;

    public AudioInteractionService(
        AudioDeviceService deviceService,
        ApplicationVolumeService volumeService,
        MediaSessionService mediaSessionService,
        AudioMonitorService audioMonitorService)
    {
        _deviceService = deviceService;
        _volumeService = volumeService;
        _mediaSessionService = mediaSessionService;
        _audioMonitorService = audioMonitorService;
    }

    public Task<IReadOnlyList<AudioDeviceOption>> GetOutputDevicesAsync() =>
        _deviceService.GetRenderDevicesAsync();

    public IReadOnlyList<ApplicationVolumeSnapshot> GetApplications() =>
        _volumeService.GetApplications(_mediaSessionService.SelectedSourceId, _mediaSessionService.SelectedSourceName);

    public ApplicationVolumeSnapshot? GetCurrentMediaVolume() =>
        _volumeService.GetCurrentMediaVolume(_mediaSessionService.SelectedSourceId, _mediaSessionService.SelectedSourceName);

    public bool SetApplicationVolume(string processName, int volumePercent) =>
        _volumeService.SetApplicationVolume(processName, volumePercent);

    public async Task<string> AdjustCurrentMediaVolumeAsync(int signedSteps)
    {
        if (signedSteps == 0)
            return "当前媒体音量：未改变";

        await _volumeGate.WaitAsync();
        try
        {
            var current = await Task.Run(GetCurrentMediaVolume);
            if (current is null)
                return "当前媒体音量：不可用";

            var next = Math.Clamp(current.VolumePercent + signedSteps * VolumeStepPercent, 0, 100);
            await Task.Run(() => SetApplicationVolume(current.ProcessName, next));
            return $"{current.DisplayName}：{next}%";
        }
        finally
        {
            _volumeGate.Release();
        }
    }

    public async Task SetOutputDeviceAsync(AudioDeviceOption device)
    {
        if (_deviceService.IsDefaultRenderDevice(device.Id))
            return;

        await Task.Run(() => _deviceService.SetDefaultRenderDevice(device.PolicyId));
        _audioMonitorService.ResetAfterEnvironmentChange();
    }

    public async Task<string> CycleOutputDeviceAsync(int signedSteps, bool deferApply)
    {
        if (signedSteps == 0)
            return "输出设备：未改变";

        var devices = await GetOutputDevicesAsync();
        if (devices.Count == 0)
            return "输出设备：不可用";

        AudioDeviceOption target;
        int version;
        lock (_deviceGate)
        {
            var current = devices.ToList().FindIndex(device =>
                string.Equals(device.Id, _previewDeviceId, StringComparison.OrdinalIgnoreCase));
            if (current < 0)
                current = Math.Max(0, devices.ToList().FindIndex(device => device.IsDefault));
            target = devices[WheelInput.MoveCircular(current, signedSteps, devices.Count)];
            _previewDeviceId = target.Id;
            version = ++_deviceApplyVersion;
        }

        if (deferApply)
            await Task.Delay(WheelDeviceDelay);

        lock (_deviceGate)
        {
            if (version != _deviceApplyVersion)
                return $"输出设备：{target.DisplayName}";
        }

        await SetOutputDeviceAsync(target);
        lock (_deviceGate)
        {
            if (version == _deviceApplyVersion)
                _previewDeviceId = null;
        }
        return $"输出设备：{target.DisplayName}";
    }
}
