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
    private static readonly TimeSpan DeviceApplyDelay =
        TimeSpan.FromMilliseconds(AudioApplyPolicy.OutputDevicePreviewDelayMilliseconds);
    private static readonly TimeSpan VolumeApplyDelay =
        TimeSpan.FromMilliseconds(AudioApplyPolicy.ApplicationVolumeDelayMilliseconds);
    private readonly AudioDeviceService _deviceService;
    private readonly SpatialAudioService _spatialAudioService;
    private readonly ApplicationVolumeService _volumeService;
    private readonly MediaSessionService _mediaSessionService;
    private readonly ShellTrayIconService _trayIconService;
    private readonly NativeMouseInputMonitor _mouseInputMonitor;
    private readonly GlobalInteractionRouter _interactionRouter;
    private readonly AudioMonitorService _audioMonitorService;
    private readonly Dictionary<string, int> _volumeApplyVersions = new(StringComparer.OrdinalIgnoreCase);
    private int _deviceApplyVersion;
    private bool _isRefreshing;
    private bool _isPreviewingOutputDevice;
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
    public event EventHandler? SettingsOpenRequested;

    /// <summary>
    /// 调用 AudioControlViewModel，提供 API。
    /// Provides the public AudioControlViewModel entry point required by this component.
    /// </summary>
    public AudioControlViewModel(
        AudioDeviceService deviceService,
        SpatialAudioService spatialAudioService,
        ApplicationVolumeService volumeService,
        MediaSessionService mediaSessionService,
        ShellTrayIconService trayIconService,
        NativeMouseInputMonitor mouseInputMonitor,
        GlobalInteractionRouter interactionRouter,
        AudioMonitorService audioMonitorService)
    {
        _deviceService = deviceService;
        _spatialAudioService = spatialAudioService;
        _volumeService = volumeService;
        _mediaSessionService = mediaSessionService;
        _trayIconService = trayIconService;
        _mouseInputMonitor = mouseInputMonitor;
        _interactionRouter = interactionRouter;
        _audioMonitorService = audioMonitorService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenSpatialAudioSettingsCommand = new RelayCommand(OpenSpatialAudioSettings);
        _trayIconService.LeftClicked += OnTrayLeftClicked;
        _trayIconService.ContextMenuRequested += OnTrayContextMenuRequested;
        _trayIconService.TooltipOpening += OnTrayTooltipOpening;
        _mouseInputMonitor.WheelChanged += OnTrayWheelChanged;
        _mediaSessionService.SnapshotChanged += OnMediaSnapshotChanged;
        SettingsManager.TrayWheelBehaviorChanged += OnTrayWheelBehaviorChanged;
        SettingsManager.InteractionSettingsChanged += OnInteractionSettingsChanged;
        _mouseInputMonitor.Start();
        QueueTrayTooltipRefresh();
    }

    /// <summary>
    /// 刷新输出设备、应用音量和空间音效状态。
    /// Refreshes output devices, application volumes, and spatial-audio state.
    /// </summary>
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

    /// <summary>
    /// 根据托盘滚轮增量预览下一个输出设备。
    /// Previews the next output device for a tray-wheel delta.
    /// </summary>
    /// <param name="delta">滚轮增量 / Wheel delta.</param>
    public void PreviewOutputDeviceWheel(int delta)
    {
        if (OutputDevices.Count == 0 || delta == 0)
        {
            return;
        }

        var current = Math.Max(0, SelectedOutputDevice is null ? 0 : OutputDevices.IndexOf(SelectedOutputDevice));
        var steps = WheelInput.GetStepCount(delta) * (delta > 0 ? -1 : 1);
        var device = OutputDevices[WheelInput.MoveCircular(current, steps, OutputDevices.Count)];

        _isPreviewingOutputDevice = true;
        try
        {
            SelectedOutputDevice = device;
        }
        finally
        {
            _isPreviewingOutputDevice = false;
        }
    }

    partial void OnSelectedOutputDeviceChanged(AudioDeviceOption? value)
    {
        if (!_isRefreshing && value is not null)
        {
            StartOutputDeviceApply(
                value,
                _isPreviewingOutputDevice ? DeviceApplyDelay : TimeSpan.Zero);
        }
    }

    private void StartOutputDeviceApply(AudioDeviceOption device, TimeSpan delay)
    {
        var version = ++_deviceApplyVersion;
        _ = ApplyOutputDeviceAsync(device, version, delay);
        SetTrayTooltip($"输出设备：{device.DisplayName}");
    }

    private async Task ApplyOutputDeviceAsync(AudioDeviceOption device, int version, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            if (!AudioApplyPolicy.IsCurrent(_disposed, version, _deviceApplyVersion))
            {
                return;
            }

            // 缓冲结束后再次读取系统默认设备；循环滚回原设备时不要重复切换而打断音频。
            // Recheck the system default after buffering so cycling back does not interrupt audio with a redundant switch.
            if (_deviceService.IsDefaultRenderDevice(device.Id))
            {
                return;
            }

            await Task.Run(() => _deviceService.SetDefaultRenderDevice(device.PolicyId));
            _audioMonitorService.ResetAfterEnvironmentChange();
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            StatusText = $"切换输出设备失败：{exception.Message}";
            SetTrayTooltip("输出设备切换失败");
        }
    }

    private void QueueApplicationVolume(
        ApplicationVolumeItemViewModel application,
        int volumePercent,
        bool applyImmediately)
    {
        StartApplicationVolumeApply(
            application,
            volumePercent,
            applyImmediately ? TimeSpan.Zero : VolumeApplyDelay);
    }

    private void StartApplicationVolumeApply(
        ApplicationVolumeItemViewModel application,
        int volumePercent,
        TimeSpan delay)
    {
        var version = _volumeApplyVersions.GetValueOrDefault(application.ProcessName) + 1;
        _volumeApplyVersions[application.ProcessName] = version;
        _ = ApplyApplicationVolumeAsync(application, volumePercent, version, delay);
    }

    private async Task ApplyApplicationVolumeAsync(
        ApplicationVolumeItemViewModel application,
        int volumePercent,
        int version,
        TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

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
        var feedback = await _interactionRouter.ExecuteWheelAsync(
            e.Delta,
            e.IsLeftButtonDown,
            e.IsRightButtonDown,
            isTray: true);
        if (!string.IsNullOrWhiteSpace(feedback))
            SetTrayTooltip(feedback);
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

    private void OnTrayLeftClicked(object? sender, EventArgs e)
    {
        switch (SettingsManager.Current.Interaction.TrayClickAction)
        {
            case TrayClickAction.OpenSettings:
                SettingsOpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case TrayClickAction.OpenAudioControl:
                FlyoutToggleRequested?.Invoke(GetTrayBounds());
                break;
        }
    }
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

    private void OnInteractionSettingsChanged(object? sender, EventArgs e) => QueueTrayTooltipRefresh();

    private void QueueTrayTooltipRefresh()
    {
        var version = ++_tooltipRefreshVersion;
        _ = RefreshTrayTooltipAsync(version);
    }

    private async Task RefreshTrayTooltipAsync(int version)
    {
        try
        {
            await Task.CompletedTask;
            var interaction = SettingsManager.Current.Interaction;
            var text = interaction.TrayUsesGlobalWheel
                ? $"AF Media Bar · 滚轮：{GetWheelActionName(interaction.PrimaryWheelAction)}"
                : "AF Media Bar";

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
        var interaction = SettingsManager.Current.Interaction;
        SetTrayTooltip(interaction.TrayUsesGlobalWheel
            ? $"AF Media Bar · 滚轮：{GetWheelActionName(interaction.PrimaryWheelAction)}"
            : "AF Media Bar");
    }

    private static string GetWheelActionName(WheelAction action) => action switch
    {
        WheelAction.CurrentApplicationVolume => "当前应用音量",
        WheelAction.OutputDevice => "输出设备",
        _ => "上一首 / 下一首"
    };

    private void SetTrayTooltip(string text)
    {
        _tooltipRefreshVersion++;
        _trayIconService.UpdateTooltip(text);
    }

    private TrayIconBounds? GetTrayBounds() => _trayIconService.TryGetBounds(out var bounds) ? bounds : null;

    /// <summary>
    /// 取消托盘和媒体事件订阅并释放输入监听器。
    /// Unsubscribes tray and media events and releases the input monitor.
    /// </summary>
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
        SettingsManager.InteractionSettingsChanged -= OnInteractionSettingsChanged;
        _mouseInputMonitor.Dispose();
        _deviceApplyVersion++;
        _volumeApplyVersions.Clear();
    }
}
