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
    private static readonly TimeSpan DeviceApplyDelay = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan VolumeApplyDelay = TimeSpan.FromMilliseconds(100);
    private readonly AudioDeviceService _deviceService;
    private readonly SpatialAudioService _spatialAudioService;
    private readonly ApplicationVolumeService _volumeService;
    private readonly MediaSessionService _mediaSessionService;
    private readonly ShellTrayIconService _trayIconService;
    private readonly NativeMouseWheelMonitor _wheelMonitor;
    private readonly Dictionary<string, CancellationTokenSource> _volumeDelays = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _deviceDelay;
    private bool _isRefreshing;
    private int _pendingTrayVolumeSteps;
    private bool _isProcessingTrayVolume;

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

            SettingsManager.Current.TrayWheelBehavior = value;
            OnPropertyChanged();
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand OpenSpatialAudioSettingsCommand { get; }

    public event Action<TrayIconBounds?>? FlyoutToggleRequested;
    public event Action<TrayIconBounds?>? TrayContextMenuRequested;
    public event Action<string, TrayIconBounds?>? FeedbackRequested;

    public AudioControlViewModel(
        AudioDeviceService deviceService,
        SpatialAudioService spatialAudioService,
        ApplicationVolumeService volumeService,
        MediaSessionService mediaSessionService,
        ShellTrayIconService trayIconService,
        NativeMouseWheelMonitor wheelMonitor)
    {
        _deviceService = deviceService;
        _spatialAudioService = spatialAudioService;
        _volumeService = volumeService;
        _mediaSessionService = mediaSessionService;
        _trayIconService = trayIconService;
        _wheelMonitor = wheelMonitor;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenSpatialAudioSettingsCommand = new RelayCommand(OpenSpatialAudioSettings);
        _trayIconService.LeftClicked += OnTrayLeftClicked;
        _trayIconService.ContextMenuRequested += OnTrayContextMenuRequested;
        _wheelMonitor.WheelChanged += OnTrayWheelChanged;
        _mediaSessionService.SnapshotChanged += OnMediaSnapshotChanged;
        _wheelMonitor.Start();
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
        _deviceDelay?.Cancel();
        _deviceDelay?.Dispose();
        _deviceDelay = new CancellationTokenSource();
        _ = ApplyOutputDeviceAfterDelayAsync(device, _deviceDelay.Token);
        RaiseFeedback($"输出设备：{device.DisplayName}");
    }

    private async Task ApplyOutputDeviceAfterDelayAsync(AudioDeviceOption device, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DeviceApplyDelay, cancellationToken);
            await Task.Run(() => _deviceService.SetDefaultRenderDevice(device.PolicyId), cancellationToken);
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText = $"切换输出设备失败：{exception.Message}";
            RaiseFeedback("输出设备切换失败");
        }
    }

    private void QueueApplicationVolume(ApplicationVolumeItemViewModel application, int volumePercent)
    {
        if (_volumeDelays.Remove(application.ProcessName, out var oldDelay))
        {
            oldDelay.Cancel();
            oldDelay.Dispose();
        }

        var delay = new CancellationTokenSource();
        _volumeDelays[application.ProcessName] = delay;
        _ = ApplyApplicationVolumeAfterDelayAsync(application, volumePercent, delay);
    }

    private async Task ApplyApplicationVolumeAfterDelayAsync(
        ApplicationVolumeItemViewModel application,
        int volumePercent,
        CancellationTokenSource delay)
    {
        try
        {
            await Task.Delay(VolumeApplyDelay, delay.Token);
            await Task.Run(() => _volumeService.SetApplicationVolume(application.ProcessName, volumePercent), delay.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText = $"调节 {application.DisplayName} 音量失败：{exception.Message}";
        }
        finally
        {
            if (_volumeDelays.TryGetValue(application.ProcessName, out var current) && ReferenceEquals(current, delay))
            {
                _volumeDelays.Remove(application.ProcessName);
                delay.Dispose();
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
                    RaiseFeedback("未找到当前媒体应用的音量会话");
                    continue;
                }

                var next = Math.Clamp(current.VolumePercent + steps * VolumeStepPercent, 0, 100);
                await Task.Run(() => _volumeService.SetApplicationVolume(current.ProcessName, next));
                Applications.FirstOrDefault(item => string.Equals(item.ProcessName, current.ProcessName, StringComparison.OrdinalIgnoreCase))?.SynchronizeVolume(next);
                RaiseFeedback($"{current.DisplayName}  {next}%");
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[AudioControlViewModel] Tray volume failed: {exception}");
            RaiseFeedback("音量调节失败");
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
        _trayIconService.UpdateTooltip(snapshot.IsConnected ? $"{snapshot.Title}\n{snapshot.Artist}" : null);
    }

    private TrayIconBounds? GetTrayBounds() => _trayIconService.TryGetBounds(out var bounds) ? bounds : null;
    private void RaiseFeedback(string text) => FeedbackRequested?.Invoke(text, GetTrayBounds());

    public void Dispose()
    {
        _trayIconService.LeftClicked -= OnTrayLeftClicked;
        _trayIconService.ContextMenuRequested -= OnTrayContextMenuRequested;
        _wheelMonitor.WheelChanged -= OnTrayWheelChanged;
        _mediaSessionService.SnapshotChanged -= OnMediaSnapshotChanged;
        _wheelMonitor.Dispose();
        _deviceDelay?.Cancel();
        _deviceDelay?.Dispose();
        foreach (var delay in _volumeDelays.Values)
        {
            delay.Cancel();
            delay.Dispose();
        }
        _volumeDelays.Clear();
    }
}
