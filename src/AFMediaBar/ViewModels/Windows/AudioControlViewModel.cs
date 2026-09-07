using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>
/// 协调托盘音频面板的设备预览、应用音量和滚轮语义。
/// Coordinates device preview, application volume, and tray-wheel semantics for the audio flyout.
/// </summary>
public partial class AudioControlViewModel : ObservableObject, IDisposable
{
    private const int VolumeStepPercent = 2;
    private static readonly TimeSpan DeviceApplyDelay = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan VolumeApplyDelay = TimeSpan.FromMilliseconds(100);
    private readonly AudioDeviceService _deviceService;
    private readonly SpatialAudioService _spatialAudioService;
    private readonly ApplicationVolumeService _volumeService;
    private readonly MediaSessionService _mediaSessionService;
    private readonly ShellTrayIconService _trayIconService;
    private readonly NativeMouseInputMonitor _mouseInputMonitor;
    private readonly Dictionary<string, int> _volumeApplyVersions = new(StringComparer.OrdinalIgnoreCase);
    private int _deviceApplyVersion;
    private bool _isRefreshing;
    private int _pendingTrayVolumeSteps;
    private bool _isProcessingTrayVolume;
    private int _tooltipRefreshVersion;
    private string? _tooltipSourceId;
    private string? _tooltipSourceName;
    private bool _disposed;

    public ObservableCollection<AudioDeviceOption> OutputDevices { get; } = [];
    public ObservableCollection<ApplicationVolumeItemViewModel> Applications { get; } = [];

    [ObservableProperty] private AudioDeviceOption? _selectedOutputDevice;
    [ObservableProperty] private string _spatialAudioText = "状态不可用";
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public TrayWheelBehavior TrayWheelBehavior
    {
        get => SettingsManager.Current.TrayWheelBehavior;
        set
        {
            if (SettingsManager.Current.TrayWheelBehavior == value)
            {
                return;
            }

            SettingsManager.SetTrayWheelBehavior(value);
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand OpenSpatialAudioSettingsCommand { get; }

    public event Action<TrayIconBounds?>? FlyoutToggleRequested;
    public event Action<TrayIconBounds?>? TrayContextMenuRequested;

    public AudioControlViewModel(
        AudioDeviceService deviceService,
        SpatialAudioService spatialAudioService,
        ApplicationVolumeService volumeService,
        MediaSessionService mediaSessionService,
        ShellTrayIconService trayIconService,
        NativeMouseInputMonitor mouseInputMonitor)
    {
        _deviceService = deviceService;
        _spatialAudioService = spatialAudioService;
        _volumeService = volumeService;
        _mediaSessionService = mediaSessionService;
        _trayIconService = trayIconService;
        _mouseInputMonitor = mouseInputMonitor;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenSpatialAudioSettingsCommand = new RelayCommand(OpenSpatialAudioSettings);
        _trayIconService.LeftClicked += OnTrayLeftClicked;
        _trayIconService.ContextMenuRequested += OnTrayContextMenuRequested;
        _trayIconService.TooltipOpening += OnTrayTooltipOpening;
        _mouseInputMonitor.WheelChanged += OnTrayWheelChanged;
        _mediaSessionService.SnapshotChanged += OnMediaSnapshotChanged;
        SettingsManager.TrayWheelBehaviorChanged += OnTrayWheelBehaviorChanged;
        _mouseInputMonitor.Start();
        QueueTrayTooltipRefresh();
    }

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var devices = await _deviceService.GetRenderDevicesAsync().WaitAsync(TimeSpan.FromSeconds(3));
            _isRefreshing = true;
            OutputDevices.Clear();
            foreach (var device in devices)
            {
                OutputDevices.Add(device);
            }

            SelectedOutputDevice = devices.FirstOrDefault(device => device.IsDefault) ?? devices.FirstOrDefault();
            _isRefreshing = false;
            RefreshSpatialAudio();

            var applications = await Task.Run(() => _volumeService.GetApplications(
                _mediaSessionService.SelectedSourceId,
                _mediaSessionService.SelectedSourceName)).WaitAsync(TimeSpan.FromSeconds(3));
            Applications.Clear();
            foreach (var application in applications)
            {
                Applications.Add(new ApplicationVolumeItemViewModel(application, QueueApplicationVolume));
            }

            StatusText = applications.Count == 0 ? "当前没有应用音频会话" : string.Empty;
            UpdateTooltipFromLoadedState();
        }
        catch (Exception exception)
        {
            _isRefreshing = false;
            StatusText = $"读取音频状态失败：{exception.Message}";
            Debug.WriteLine($"[AudioControlViewModel] Refresh failed: {exception}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void PreviewOutputDeviceWheel(int delta)
    {
        if (OutputDevices.Count == 0 || delta == 0)
        {
            return;
        }

        var current = Math.Max(0, SelectedOutputDevice is null ? 0 : OutputDevices.IndexOf(SelectedOutputDevice));
        var steps = WheelInput.GetStepCount(delta) * (delta > 0 ? -1 : 1);
        SelectedOutputDevice = OutputDevices[WheelInput.MoveCircular(current, steps, OutputDevices.Count)];
    }

    partial void OnSelectedOutputDeviceChanged(AudioDeviceOption? value)
    {
        if (!_isRefreshing && value is not null)
        {
            QueueOutputDevice(value);
        }
    }

    private void QueueOutputDevice(AudioDeviceOption device)
    {
        var version = ++_deviceApplyVersion;
        _ = ApplyOutputDeviceAfterDelayAsync(device, version);
        SetTrayTooltip($"输出设备：{device.DisplayName}");
    }

    private async Task ApplyOutputDeviceAfterDelayAsync(AudioDeviceOption device, int version)
    {
        try
        {
            await Task.Delay(DeviceApplyDelay);
            if (_disposed || version != _deviceApplyVersion)
            {
                return;
            }

            await Task.Run(() => _deviceService.SetDefaultRenderDevice(device.PolicyId));
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            StatusText = $"切换输出设备失败：{exception.Message}";
            SetTrayTooltip("输出设备切换失败");
        }
    }

    private void QueueApplicationVolume(ApplicationVolumeItemViewModel application, int volumePercent)
    {
        var version = _volumeApplyVersions.GetValueOrDefault(application.ProcessName) + 1;
        _volumeApplyVersions[application.ProcessName] = version;
        _ = ApplyApplicationVolumeAfterDelayAsync(application, volumePercent, version);
    }

    private async Task ApplyApplicationVolumeAfterDelayAsync(
        ApplicationVolumeItemViewModel application,
        int volumePercent,
        int version)
    {
        try
        {
            await Task.Delay(VolumeApplyDelay);
            if (_disposed || !_volumeApplyVersions.TryGetValue(application.ProcessName, out var current) || current != version)
            {
                return;
            }

            await Task.Run(() => _volumeService.SetApplicationVolume(application.ProcessName, volumePercent));
        }
        catch (Exception exception)
        {
            StatusText = $"调节 {application.DisplayName} 音量失败：{exception.Message}";
        }
        finally
        {
            if (_volumeApplyVersions.TryGetValue(application.ProcessName, out var current) && current == version)
            {
                _volumeApplyVersions.Remove(application.ProcessName);
            }
        }
    }

    private async void OnTrayWheelChanged(object? sender, TrayWheelEventArgs e)
    {
        if (TrayWheelBehavior == TrayWheelBehavior.Disabled)
        {
            return;
        }

        var switchDevice = TrayWheelBehavior == TrayWheelBehavior.SwitchOutputDevice;
        if (e.IsShiftPressed)
        {
            switchDevice = !switchDevice;
        }

        if (switchDevice)
        {
            if (OutputDevices.Count == 0)
            {
                await RefreshAsync();
            }

            PreviewOutputDeviceWheel(e.Delta);
            return;
        }

        var stepCount = WheelInput.GetStepCount(e.Delta);
        _pendingTrayVolumeSteps += e.Delta > 0 ? stepCount : -stepCount;
        await ProcessTrayVolumeAsync();
    }

    private async Task ProcessTrayVolumeAsync()
    {
        if (_isProcessingTrayVolume)
        {
            return;
        }

        _isProcessingTrayVolume = true;
        try
        {
            while (_pendingTrayVolumeSteps != 0)
            {
                var steps = _pendingTrayVolumeSteps;
                _pendingTrayVolumeSteps = 0;
                var current = await Task.Run(() => _volumeService.GetCurrentMediaVolume(
                    _mediaSessionService.SelectedSourceId,
                    _mediaSessionService.SelectedSourceName));
                if (current is null)
                {
                    SetTrayTooltip("当前媒体音量：不可用");
                    continue;
                }

                var next = Math.Clamp(current.VolumePercent + steps * VolumeStepPercent, 0, 100);
                await Task.Run(() => _volumeService.SetApplicationVolume(current.ProcessName, next));
                Applications.FirstOrDefault(item => string.Equals(item.ProcessName, current.ProcessName, StringComparison.OrdinalIgnoreCase))?.SynchronizeVolume(next);
                SetTrayTooltip($"{current.DisplayName}：{next}%");
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[AudioControlViewModel] Tray volume failed: {exception}");
            SetTrayTooltip("音量调节失败");
        }
        finally
        {
            _isProcessingTrayVolume = false;
            if (_pendingTrayVolumeSteps != 0)
            {
                _ = ProcessTrayVolumeAsync();
            }
        }
    }

    private void RefreshSpatialAudio()
    {
        SpatialAudioText = SelectedOutputDevice is null
            ? "无输出设备"
            : _spatialAudioService.GetState(SelectedOutputDevice.Id).DisplayName;
    }

    private void OpenSpatialAudioSettings()
    {
        try
        {
            _spatialAudioService.OpenSystemSettings();
        }
        catch (Exception exception)
        {
            StatusText = $"无法打开系统声音设置：{exception.Message}";
        }
    }

    private void OnTrayLeftClicked(object? sender, EventArgs e) => FlyoutToggleRequested?.Invoke(GetTrayBounds());
    private void OnTrayContextMenuRequested(object? sender, EventArgs e) => TrayContextMenuRequested?.Invoke(GetTrayBounds());

    private void OnMediaSnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        if (string.Equals(_tooltipSourceId, snapshot.SourceId, StringComparison.Ordinal) &&
            string.Equals(_tooltipSourceName, snapshot.SourceName, StringComparison.Ordinal))
        {
            return;
        }

        _tooltipSourceId = snapshot.SourceId;
        _tooltipSourceName = snapshot.SourceName;
        QueueTrayTooltipRefresh();
    }

    private void OnTrayTooltipOpening(object? sender, EventArgs e) => QueueTrayTooltipRefresh();

    private void OnTrayWheelBehaviorChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(TrayWheelBehavior));
        QueueTrayTooltipRefresh();
    }

    private void QueueTrayTooltipRefresh()
    {
        var version = ++_tooltipRefreshVersion;
        _ = RefreshTrayTooltipAsync(version);
    }

    private async Task RefreshTrayTooltipAsync(int version)
    {
        try
        {
            string text;
            switch (TrayWheelBehavior)
            {
                case TrayWheelBehavior.AdjustVolume:
                    var application = await Task.Run(() => _volumeService.GetCurrentMediaVolume(
                        _mediaSessionService.SelectedSourceId,
                        _mediaSessionService.SelectedSourceName));
                    text = application is null
                        ? "当前媒体音量：不可用"
                        : $"{application.DisplayName}：{application.VolumePercent}%";
                    break;
                case TrayWheelBehavior.SwitchOutputDevice:
                    var devices = await _deviceService.GetRenderDevicesAsync();
                    var device = devices.FirstOrDefault(candidate => candidate.IsDefault);
                    text = device is null ? "输出设备：不可用" : $"输出设备：{device.DisplayName}";
                    break;
                default:
                    text = "托盘滚轮已禁用";
                    break;
            }

            if (!_disposed && version == _tooltipRefreshVersion)
            {
                _trayIconService.UpdateTooltip(text);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[AudioControlViewModel] Tooltip refresh failed: {exception.Message}");
            if (!_disposed && version == _tooltipRefreshVersion)
            {
                _trayIconService.UpdateTooltip("AF Media Bar");
            }
        }
    }

    private void UpdateTooltipFromLoadedState()
    {
        var text = TrayWheelBehavior switch
        {
            TrayWheelBehavior.AdjustVolume => Applications.FirstOrDefault(item => item.IsCurrentMedia) is { } application
                ? $"{application.DisplayName}：{application.VolumePercent}%"
                : "当前媒体音量：不可用",
            TrayWheelBehavior.SwitchOutputDevice => SelectedOutputDevice is { } device
                ? $"输出设备：{device.DisplayName}"
                : "输出设备：不可用",
            _ => "托盘滚轮已禁用"
        };
        SetTrayTooltip(text);
    }

    private void SetTrayTooltip(string text)
    {
        _tooltipRefreshVersion++;
        _trayIconService.UpdateTooltip(text);
    }

    private TrayIconBounds? GetTrayBounds() => _trayIconService.TryGetBounds(out var bounds) ? bounds : null;

    public void Dispose()
    {
        _disposed = true;
        _tooltipRefreshVersion++;
        _trayIconService.LeftClicked -= OnTrayLeftClicked;
        _trayIconService.ContextMenuRequested -= OnTrayContextMenuRequested;
        _trayIconService.TooltipOpening -= OnTrayTooltipOpening;
        _mouseInputMonitor.WheelChanged -= OnTrayWheelChanged;
        _mediaSessionService.SnapshotChanged -= OnMediaSnapshotChanged;
        SettingsManager.TrayWheelBehaviorChanged -= OnTrayWheelBehaviorChanged;
        _mouseInputMonitor.Dispose();
        _deviceApplyVersion++;
        _volumeApplyVersions.Clear();
    }
}
