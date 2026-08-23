using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Adapters;
using AFMediaBar.Interop;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar;

/// <summary>
/// 协调输出设备、应用音量和频谱控件；系统调用与设备枚举由音频服务负责。
/// Coordinates output-device, application-volume, and spectrum controls while audio services own system access.
/// </summary>
public partial class MainWindow
{
    private const int VolumeWheelStepPercent = 2;
    private const int AudioMonitorIntervalMilliseconds = 50;

    private readonly AudioDeviceService _audioDeviceService = new(WpfStringLocalizer.Instance);
    private readonly ApplicationVolumeService _applicationVolumeService = new(WpfStringLocalizer.Instance);
    private readonly DispatcherTimer _audioMonitorTimer;
    private readonly DispatcherTimer _outputDeviceApplyTimer;
    private readonly DispatcherTimer _volumeApplyTimer;
    private readonly DispatcherTimer _volumePopupCloseTimer;
    private IReadOnlyList<AudioDeviceOption> _outputDevices = [];
    private AudioMonitorService? _audioMonitorService;
    // 输出设备滚轮先预览候选项，停止输入一秒后再真正切换。
    // Output-device wheel input previews a candidate, then applies it after one idle second.
    private string? _pendingOutputDeviceId;
    private int _pendingOutputDeviceWheelSteps;
    private ApplicationVolumeSnapshot? _currentApplicationVolume;
    // 音量滚轮合并快速步进，并以短延迟批量写入 Core Audio。
    // Volume wheel steps are coalesced and written to Core Audio after a short delay.
    private int? _pendingVolumePercent;
    private int _pendingVolumeWheelSteps;
    // 来源切换时递增版本号，丢弃旧进程匹配查询的迟到结果。
    // Increment on source changes so stale process-matching results are ignored.
    private int _volumeRefreshVersion;
    private string? _lastVolumeSourceId;
    private bool _isUpdatingVolumeSlider;
    private bool _isProcessingOutputDeviceWheel;
    private bool _isProcessingVolumeWheel;
    private bool _outputDeviceWheelUsesCompactStatus;
    private bool _volumeWheelUsesCompactStatus;
    private bool _showingOutputDeviceHoverStatus;
    private bool _showingVolumeHoverStatus;
    private readonly float[] _audioSpectrum = new float[AudioMonitorService.BandCount];
    private readonly float[] _smoothedAudioSpectrum = new float[AudioMonitorService.BandCount];
    private Border[] _audioBars = null!;

    private void ApplyOutputDeviceSettings()
    {
        var enabled = _metricSettings.OutputDeviceSwitcherEnabled;
        OutputDeviceHost.Visibility = enabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        VerticalOutputDeviceHost.Visibility = OutputDeviceHost.Visibility;
        UpdatePlayerWidth(_metricSettings.SelectedCount > 0);
        if (enabled)
        {
            return;
        }

        OutputDevicePopup.IsOpen = false;
        OutputDeviceStatusPopup.IsOpen = false;
        _outputDeviceApplyTimer.Stop();
        _pendingOutputDeviceId = null;
        _pendingOutputDeviceWheelSteps = 0;
        _outputDeviceWheelUsesCompactStatus = false;
        _outputDevices = [];
        OutputDeviceList.ItemsSource = null;
    }

    private void ApplyVolumeControlSettings()
    {
        var enabled = _metricSettings.VolumeControlEnabled;
        VolumeControlHost.Visibility = enabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        VerticalVolumeControlHost.Visibility = VolumeControlHost.Visibility;
        UpdatePlayerWidth(_metricSettings.SelectedCount > 0);
        if (enabled)
        {
            _ = RefreshCurrentMediaVolumeAsync(
                _mediaSessionService.SelectedSourceId,
                _mediaSessionService.SelectedSourceName);
            return;
        }

        VolumeControlPopup.IsOpen = false;
        VolumeStatusPopup.IsOpen = false;
        _volumeApplyTimer.Stop();
        _volumePopupCloseTimer.Stop();
        _pendingVolumePercent = null;
        _pendingVolumeWheelSteps = 0;
        _volumeWheelUsesCompactStatus = false;
        _currentApplicationVolume = null;
    }

    private void ApplyAudioMonitorSettings()
    {
        if (_metricSettings.AudioMonitorEnabled)
        {
            _audioMonitorService ??= new AudioMonitorService();
            if (!_audioMonitorTimer.IsEnabled)
            {
                _audioMonitorTimer.Start();
            }
        }
        else
        {
            _audioMonitorTimer.Stop();
            _audioMonitorService?.Dispose();
            _audioMonitorService = null;
            Array.Clear(_audioSpectrum);
            Array.Clear(_smoothedAudioSpectrum);
            SetAudioBarHeights();
        }

        AudioVisualizerHost.Visibility = _metricSettings.AudioMonitorEnabled && !_isExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetExpanded(_isExpanded, animate: false);
    }

    private void OnAudioMonitorTimerTick(object? sender, EventArgs e)
    {
        if (!_metricSettings.AudioMonitorEnabled || _audioMonitorService is null)
        {
            return;
        }

        _audioMonitorService.GetSpectrum(_audioSpectrum);

        if (!_isExpanded)
        {
            SetAudioBarHeights();
        }
    }

    private void SetAudioBarHeights()
    {
        for (var index = 0; index < _audioBars.Length; index++)
        {
            var target = _audioSpectrum[index];
            var current = _smoothedAudioSpectrum[index];
            var response = target > current ? 0.72f : 0.18f;
            current += (target - current) * response;
            if (current < 0.008f)
            {
                current = 0;
            }

            _smoothedAudioSpectrum[index] = current;
            _audioBars[index].Height = Math.Clamp(3 + Math.Sqrt(current) * 32, 3, 35);
        }

        ComponentSurface_OnSpectrumChanged(_audioSpectrum);
    }

    private async Task RefreshOutputDevicesAsync(string? preferredId = null)
    {
        try
        {
            var devices = await _audioDeviceService.GetRenderDevicesAsync()
                .WaitAsync(TimeSpan.FromSeconds(2));
            _outputDevices = devices;
            OutputDeviceList.ItemsSource = devices;

            var selected = devices.FirstOrDefault(device =>
                    !string.IsNullOrWhiteSpace(preferredId) &&
                    string.Equals(device.Id, preferredId, StringComparison.OrdinalIgnoreCase)) ??
                devices.FirstOrDefault(device => device.IsDefault) ??
                devices.FirstOrDefault();
            OutputDeviceList.SelectedItem = selected;

            var current = devices.FirstOrDefault(device => device.IsDefault) ?? selected;
            OutputDeviceCurrentText.Text = current?.DisplayName ?? Loc.Get("Main.Device.NotFound");
            if (_showingOutputDeviceHoverStatus &&
                _pendingOutputDeviceId is null)
            {
                OutputDeviceStatusText.Text = current is null
                    ? Loc.Get("Main.Device.NoDevices")
                    : Loc.Get("Main.Device.OutputFormat", current.DisplayName);
            }
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("output-device-refresh", exception);
            _outputDevices = [];
            OutputDeviceList.ItemsSource = null;
            OutputDeviceCurrentText.Text = Loc.Get("Main.Device.ReadFailed");
            if (_showingOutputDeviceHoverStatus)
            {
                OutputDeviceStatusText.Text = Loc.Get("Main.Device.ReadFailedFormat", exception.Message);
            }
        }
    }

    private void OutputDeviceHost_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (!_metricSettings.OutputDeviceSwitcherEnabled ||
            OutputDevicePopup.IsOpen ||
            _pendingOutputDeviceId is not null)
        {
            return;
        }

        _showingOutputDeviceHoverStatus = true;
        var current = _outputDevices.FirstOrDefault(device => device.IsDefault);
        OutputDeviceStatusText.Text = current is null
            ? Loc.Get("Main.Device.Output")
            : Loc.Get("Main.Device.OutputFormat", current.DisplayName);
        OutputDeviceStatusPopup.IsOpen = true;
        if (_outputDevices.Count == 0)
        {
            _ = RefreshOutputDevicesAsync();
        }
    }

    private void OutputDeviceHost_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _showingOutputDeviceHoverStatus = false;
        if (_pendingOutputDeviceId is null)
        {
            OutputDeviceStatusPopup.IsOpen = false;
        }
    }

    private async void OutputDeviceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_metricSettings.OutputDeviceSwitcherEnabled &&
            sender is not FrameworkElement { Tag: MediaCommandKind.SelectOutputDevice })
        {
            return;
        }

        OutputDeviceStatusPopup.IsOpen = false;
        if (OutputDevicePopup.IsOpen)
        {
            OutputDevicePopup.IsOpen = false;
            return;
        }

        await RefreshOutputDevicesAsync(_pendingOutputDeviceId);
        if (_outputDevices.Count > 0)
        {
            OutputDevicePopup.IsOpen = true;
        }
    }

    private async void OutputDeviceList_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(
            OutputDeviceList,
            e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container?.DataContext is not AudioDeviceOption device)
        {
            return;
        }

        e.Handled = true;
        _outputDeviceApplyTimer.Stop();
        OutputDeviceStatusPopup.IsOpen = false;
        _pendingOutputDeviceId = null;
        _pendingOutputDeviceWheelSteps = 0;
        _outputDeviceWheelUsesCompactStatus = false;
        OutputDeviceList.SelectedItem = device;
        if (await SwitchOutputDeviceAsync(device))
        {
            OutputDevicePopup.IsOpen = false;
        }
    }

    private void OutputDevicePopup_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        e.Handled = true;
        QueueOutputDeviceFromWheel(e.Delta, useCompactStatus: false);
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private void OutputDeviceHost_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        e.Handled = true;
        QueueOutputDeviceFromWheel(e.Delta, useCompactStatus: true);
    }

    private void QueueOutputDeviceFromWheel(int delta, bool useCompactStatus)
    {
        if (!_metricSettings.OutputDeviceSwitcherEnabled || delta == 0)
        {
            return;
        }

        _outputDeviceWheelUsesCompactStatus = useCompactStatus;
        var stepCount = WheelInput.GetStepCount(delta);
        _pendingOutputDeviceWheelSteps += delta > 0 ? -stepCount : stepCount;
        _ = ProcessOutputDeviceWheelAsync();
    }

    private async Task ProcessOutputDeviceWheelAsync()
    {
        if (_isProcessingOutputDeviceWheel)
        {
            return;
        }

        _isProcessingOutputDeviceWheel = true;
        try
        {
            if (_outputDevices.Count == 0)
            {
                await RefreshOutputDevicesAsync();
            }

            if (!_metricSettings.OutputDeviceSwitcherEnabled ||
                _outputDevices.Count == 0)
            {
                _pendingOutputDeviceWheelSteps = 0;
                if (_outputDeviceWheelUsesCompactStatus)
                {
                    OutputDeviceStatusText.Text = Loc.Get("Main.Device.NoDevices");
                    OutputDeviceStatusPopup.IsOpen = true;
                    _outputDeviceApplyTimer.Stop();
                    _outputDeviceApplyTimer.Start();
                }

                return;
            }

            while (_pendingOutputDeviceWheelSteps != 0)
            {
                var wheelSteps = _pendingOutputDeviceWheelSteps;
                _pendingOutputDeviceWheelSteps = 0;
                var currentIndex = -1;
                if (!string.IsNullOrWhiteSpace(_pendingOutputDeviceId))
                {
                    currentIndex = FindOutputDeviceIndex(_pendingOutputDeviceId);
                }

                if (currentIndex < 0)
                {
                    currentIndex = _outputDevices
                        .Select((device, index) => (device, index))
                        .Where(pair => pair.device.IsDefault)
                        .Select(pair => pair.index)
                        .DefaultIfEmpty(0)
                        .First();
                }

                var nextIndex = WheelInput.MoveCircular(
                    currentIndex,
                    wheelSteps,
                    _outputDevices.Count);

                var nextDevice = _outputDevices[nextIndex];
                _pendingOutputDeviceId = nextDevice.Id;
                OutputDeviceList.SelectedItem = nextDevice;
                OutputDeviceCurrentText.Text = nextDevice.DisplayName;
                if (_outputDeviceWheelUsesCompactStatus)
                {
                    OutputDeviceStatusText.Text = Loc.Get("Main.Device.OutputFormat", nextDevice.DisplayName);
                    OutputDeviceStatusPopup.IsOpen = true;
                    OutputDevicePopup.IsOpen = false;
                }
                else
                {
                    OutputDeviceStatusPopup.IsOpen = false;
                    OutputDevicePopup.IsOpen = true;
                    OutputDeviceList.ScrollIntoView(nextDevice);
                }

                _outputDeviceApplyTimer.Stop();
                _outputDeviceApplyTimer.Start();
            }
        }
        finally
        {
            _isProcessingOutputDeviceWheel = false;
            if (_pendingOutputDeviceWheelSteps != 0 &&
                _metricSettings.OutputDeviceSwitcherEnabled)
            {
                _ = ProcessOutputDeviceWheelAsync();
            }
        }
    }

    private int FindOutputDeviceIndex(string deviceId)
    {
        for (var index = 0; index < _outputDevices.Count; index++)
        {
            if (string.Equals(
                    _outputDevices[index].Id,
                    deviceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private async void OnOutputDeviceApplyTimerTick(object? sender, EventArgs e)
    {
        _outputDeviceApplyTimer.Stop();
        OutputDevicePopup.IsOpen = false;
        OutputDeviceStatusPopup.IsOpen = false;
        _outputDeviceWheelUsesCompactStatus = false;
        var deviceId = _pendingOutputDeviceId;
        _pendingOutputDeviceId = null;
        var device = _outputDevices.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        if (device is not null)
        {
            await SwitchOutputDeviceAsync(device);
        }
    }

    private async Task<bool> SwitchOutputDeviceAsync(AudioDeviceOption device)
    {
        try
        {
            await Task.Run(() => _audioDeviceService.SetDefaultRenderDevice(device.PolicyId))
                .WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(180);
            if (_metricSettings.AudioMonitorEnabled)
            {
                _audioMonitorService?.Dispose();
                _audioMonitorService = null;
                ApplyAudioMonitorSettings();
            }

            await RefreshOutputDevicesAsync(device.Id);
            return true;
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write(
                "output-device-switch",
                exception,
                $"Device={device.DisplayName};Id={device.Id}");
            OutputDeviceCurrentText.Text = Loc.Get("Main.Device.SwitchFailed");
            OutputDeviceStatusText.Text = Loc.Get("Main.Device.SwitchFailedFormat", exception.Message);
            OutputDeviceStatusPopup.IsOpen = true;
            _outputDeviceApplyTimer.Stop();
            _outputDeviceApplyTimer.Start();
            return false;
        }
    }

    private void AudioControlPopup_OnOpened(object? sender, EventArgs e)
    {
        UpdateMouseHookState();
    }

    private void OutputDevicePopup_OnClosed(object? sender, EventArgs e)
    {
        UpdateMouseHookState();
        ScheduleCollapse();
    }

    private async Task RefreshCurrentMediaVolumeAsync(
        string? sourceId,
        string? sourceName)
    {
        if (!_metricSettings.VolumeControlEnabled)
        {
            return;
        }

        var version = Interlocked.Increment(ref _volumeRefreshVersion);
        try
        {
            var snapshot = await Task.Run(() =>
                    _applicationVolumeService.GetCurrentMediaVolume(sourceId, sourceName))
                .WaitAsync(TimeSpan.FromSeconds(2));
            if (version != _volumeRefreshVersion ||
                !_metricSettings.VolumeControlEnabled)
            {
                return;
            }

            SetCurrentApplicationVolume(snapshot);
        }
        catch (Exception exception)
        {
            if (version != _volumeRefreshVersion)
            {
                return;
            }

            DiagnosticsLogService.Write(
                "application-volume-refresh",
                exception,
                $"SourceId={sourceId};SourceName={sourceName}");
            SetCurrentApplicationVolume(null);
            if (_showingVolumeHoverStatus || VolumeStatusPopup.IsOpen)
            {
                VolumeStatusText.Text = Loc.Get("Main.Volume.ReadFailedFormat", exception.Message);
            }
        }
    }

    private void SetCurrentApplicationVolume(ApplicationVolumeSnapshot? snapshot)
    {
        _currentApplicationVolume = snapshot;
        _isUpdatingVolumeSlider = true;
        try
        {
            CurrentMediaVolumeSlider.IsEnabled = snapshot is not null;
            CurrentMediaVolumeSlider.Value = snapshot?.VolumePercent ?? 0;
            var selectedSourceName = _mediaSessionService.SelectedSourceName;
            VolumeMediaNameText.Text = snapshot?.DisplayName ??
                (string.IsNullOrWhiteSpace(selectedSourceName)
                    ? Loc.Get("Main.Volume.CurrentMedia")
                    : selectedSourceName);
            VolumePercentText.Text = snapshot is null
                ? Loc.Get("Main.Volume.None")
                : $"{snapshot.VolumePercent}%";
            if (VolumeStatusPopup.IsOpen || _showingVolumeHoverStatus)
            {
                UpdateVolumeStatusText();
            }
        }
        finally
        {
            _isUpdatingVolumeSlider = false;
        }
    }

    private async void VolumeControlButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_metricSettings.VolumeControlEnabled &&
            sender is not FrameworkElement { Tag: MediaCommandKind.AdjustVolume })
        {
            return;
        }

        VolumeStatusPopup.IsOpen = false;
        _volumePopupCloseTimer.Stop();
        if (VolumeControlPopup.IsOpen)
        {
            VolumeControlPopup.IsOpen = false;
            return;
        }

        await RefreshCurrentMediaVolumeAsync(
            _mediaSessionService.SelectedSourceId,
            _mediaSessionService.SelectedSourceName);
        VolumeControlPopup.IsOpen = true;
    }

    private void VolumeControlHost_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (!_metricSettings.VolumeControlEnabled || VolumeControlPopup.IsOpen)
        {
            return;
        }

        _showingVolumeHoverStatus = true;
        UpdateVolumeStatusText();
        VolumeStatusPopup.IsOpen = true;
        if (_currentApplicationVolume is null)
        {
            _ = RefreshCurrentMediaVolumeAsync(
                _mediaSessionService.SelectedSourceId,
                _mediaSessionService.SelectedSourceName);
        }
    }

    private void VolumeControlHost_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _showingVolumeHoverStatus = false;
        if (!_volumePopupCloseTimer.IsEnabled)
        {
            VolumeStatusPopup.IsOpen = false;
        }
    }

    private void VolumeControlHost_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        e.Handled = true;
        QueueVolumeWheel(e.Delta, useCompactStatus: true);
    }

    private void VolumeControlPopup_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        e.Handled = true;
        QueueVolumeWheel(e.Delta, useCompactStatus: false);
    }

    private void QueueVolumeWheel(int delta, bool useCompactStatus)
    {
        if (!_metricSettings.VolumeControlEnabled || delta == 0)
        {
            return;
        }

        _volumeWheelUsesCompactStatus = useCompactStatus;
        var stepCount = WheelInput.GetStepCount(delta);
        _pendingVolumeWheelSteps += delta > 0 ? stepCount : -stepCount;
        _ = ProcessVolumeWheelAsync();
    }

    private async Task ProcessVolumeWheelAsync()
    {
        if (_isProcessingVolumeWheel)
        {
            return;
        }

        _isProcessingVolumeWheel = true;
        try
        {
            if (_currentApplicationVolume is null)
            {
                await RefreshCurrentMediaVolumeAsync(
                    _mediaSessionService.SelectedSourceId,
                    _mediaSessionService.SelectedSourceName);
            }

            if (!_metricSettings.VolumeControlEnabled)
            {
                _pendingVolumeWheelSteps = 0;
                return;
            }

            var wheelSteps = _pendingVolumeWheelSteps;
            _pendingVolumeWheelSteps = 0;
            if (_currentApplicationVolume is null)
            {
                ShowVolumeWheelFeedback(_volumeWheelUsesCompactStatus);
                return;
            }

            var nextVolume = Math.Clamp(
                _currentApplicationVolume.VolumePercent +
                    wheelSteps * VolumeWheelStepPercent,
                0,
                100);
            _currentApplicationVolume = _currentApplicationVolume with
            {
                VolumePercent = nextVolume,
                IsMuted = false
            };
            SetVolumeSliderValue(nextVolume);
            QueueVolumeApply(nextVolume);
            ShowVolumeWheelFeedback(_volumeWheelUsesCompactStatus);
        }
        finally
        {
            _isProcessingVolumeWheel = false;
            if (_pendingVolumeWheelSteps != 0 &&
                _metricSettings.VolumeControlEnabled)
            {
                _ = ProcessVolumeWheelAsync();
            }
        }
    }

    private void SetVolumeSliderValue(int volumePercent)
    {
        _isUpdatingVolumeSlider = true;
        try
        {
            CurrentMediaVolumeSlider.Value = volumePercent;
            VolumePercentText.Text = $"{volumePercent}%";
            UpdateVolumeStatusText();
        }
        finally
        {
            _isUpdatingVolumeSlider = false;
        }
    }

    private void ShowVolumeWheelFeedback(bool useCompactStatus)
    {
        if (useCompactStatus)
        {
            UpdateVolumeStatusText();
            VolumeStatusPopup.IsOpen = true;
            VolumeControlPopup.IsOpen = false;
        }
        else
        {
            VolumeStatusPopup.IsOpen = false;
            VolumeControlPopup.IsOpen = true;
        }

        ScheduleVolumeInteractionClose();
    }

    private void UpdateVolumeStatusText()
    {
        var sourceName = _currentApplicationVolume?.DisplayName;
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = _mediaSessionService.SelectedSourceName;
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = Loc.Get("Main.Volume.CurrentMedia");
        }

        var volume = _currentApplicationVolume is null
            ? Loc.Get("Main.Volume.None")
            : $"{_currentApplicationVolume.VolumePercent}%";
        VolumeStatusText.Text = $"{sourceName}：{volume}";
    }

    private void ScheduleVolumeInteractionClose()
    {
        _volumePopupCloseTimer.Stop();
        _volumePopupCloseTimer.Start();
    }

    private void CurrentMediaVolumeSlider_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not Slider slider ||
            !slider.IsEnabled ||
            e.LeftButton != MouseButtonState.Pressed ||
            FindVisualAncestor<Thumb>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var track = FindVisualDescendant<Track>(slider);
        if (track is null || track.ActualHeight <= 0)
        {
            return;
        }

        var thumbHeight = track.Thumb?.ActualHeight ?? 0;
        var usableHeight = Math.Max(1, track.ActualHeight - thumbHeight);
        var position = Math.Clamp(
            e.GetPosition(track).Y - thumbHeight / 2,
            0,
            usableHeight);
        var fraction = 1 - position / usableHeight;
        if (track.IsDirectionReversed)
        {
            fraction = 1 - fraction;
        }

        slider.Value = slider.Minimum + fraction * (slider.Maximum - slider.Minimum);
        e.Handled = true;
    }

    private void CurrentMediaVolumeSlider_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingVolumeSlider || _currentApplicationVolume is null)
        {
            return;
        }

        var volumePercent = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
        _currentApplicationVolume = _currentApplicationVolume with
        {
            VolumePercent = volumePercent,
            IsMuted = false
        };
        VolumePercentText.Text = $"{volumePercent}%";
        QueueVolumeApply(volumePercent);
        VolumeStatusPopup.IsOpen = false;
        ScheduleVolumeInteractionClose();
    }

    private void QueueVolumeApply(int volumePercent)
    {
        _pendingVolumePercent = volumePercent;
        _volumeApplyTimer.Stop();
        _volumeApplyTimer.Start();
    }

    private async void OnVolumeApplyTimerTick(object? sender, EventArgs e)
    {
        _volumeApplyTimer.Stop();
        var volumePercent = _pendingVolumePercent;
        var application = _currentApplicationVolume;
        _pendingVolumePercent = null;
        if (!volumePercent.HasValue || application is null)
        {
            return;
        }

        try
        {
            var changed = await Task.Run(() =>
                    _applicationVolumeService.SetApplicationVolume(
                        application.ProcessName,
                        volumePercent.Value))
                .WaitAsync(TimeSpan.FromSeconds(2));
            if (!changed)
            {
                await RefreshCurrentMediaVolumeAsync(
                    _mediaSessionService.SelectedSourceId,
                    _mediaSessionService.SelectedSourceName);
            }
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write(
                "application-volume-set",
                exception,
                $"Process={application.ProcessName};Volume={volumePercent.Value}");
            VolumeStatusText.Text = Loc.Get("Main.Volume.AdjustFailedFormat", exception.Message);
            VolumeStatusPopup.IsOpen = true;
        }
    }

    private void OnVolumePopupCloseTimerTick(object? sender, EventArgs e)
    {
        _volumePopupCloseTimer.Stop();
        VolumeStatusPopup.IsOpen = false;
        VolumeControlPopup.IsOpen = false;
        _volumeWheelUsesCompactStatus = false;
    }

    private void VolumeControlPopup_OnClosed(object? sender, EventArgs e)
    {
        if (!VolumeStatusPopup.IsOpen)
        {
            _volumePopupCloseTimer.Stop();
        }

        UpdateMouseHookState();
        ScheduleCollapse();
    }

}
