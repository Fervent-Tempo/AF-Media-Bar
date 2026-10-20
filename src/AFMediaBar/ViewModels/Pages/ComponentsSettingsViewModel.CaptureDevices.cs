// 设备候选复用音频目录；只保存端点标识和名称，离开页面上下文后拒绝迟到枚举结果。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

public sealed partial class ComponentsSettingsViewModel
{
    /// <summary>频谱设备选择的呈现条目；null 标识表示跟随当前媒体来源。</summary>
    public sealed record CaptureDeviceOption(string? DeviceId, string DisplayName, string SystemName);

    private IAudioDeviceCatalog? _captureDevicesCatalog;
    private IReadOnlyList<AudioDeviceOption> _lastCaptureDevices = [];
    private int _captureDevicesVersion;
    private int _captureDevicesRefreshQueued;
    private bool _updatingCaptureDevices;
    public IReadOnlyList<CaptureDeviceOption> SpectrumCaptureDevices { get; private set; } = [];
    public string SpectrumCaptureDeviceStatus { get; private set; } = string.Empty;
    public bool IsDeviceCapture => SpectrumCaptureSource == AFMediaBar.Classes.Settings.SpectrumCaptureSource.CurrentDevice;

    public CaptureDeviceOption? SelectedSpectrumCaptureDevice
    {
        get => SpectrumCaptureDevices.FirstOrDefault(option => string.Equals(option.DeviceId,
            _configuration.Current.SpectrumComponent.CaptureDeviceId, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (_disposed || !_configuration.IsActive || _updatingCaptureDevices || value is null) return;
            _configuration.SetSpectrum(_configuration.Current.SpectrumComponent with
            { CaptureDeviceId = value.DeviceId, CaptureDeviceName = value.SystemName });
            OnPropertyChanged();
        }
    }

    private void InitializeCaptureDevices(IAudioDeviceCatalog? catalog)
    {
        _captureDevicesCatalog = catalog;
        if (catalog is not null) catalog.Changed += CaptureDevicesChanged;
        RefreshCaptureDeviceOptions();
    }

    public async Task RefreshCaptureDevicesAsync()
    {
        if (_disposed || !_configuration.IsActive || _captureDevicesCatalog is null) return;
        var version = ++_captureDevicesVersion;
        var token = _configuration.CancellationToken;
        try
        {
            var devices = await _captureDevicesCatalog.GetDevicesAsync(AudioDeviceDirection.Output, includeHidden: true, token).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || !_configuration.IsActive || version != _captureDevicesVersion) return;
                _lastCaptureDevices = devices;
                SpectrumCaptureDeviceStatus = string.Empty;
                RefreshCaptureDeviceOptions();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
            try
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    if (_disposed || token.IsCancellationRequested || !_configuration.IsActive || version != _captureDevicesVersion) return;
                    SpectrumCaptureDeviceStatus = Translations.Get("Media.Spectrum.CaptureDevice.ListUnavailable");
                    OnPropertyChanged(nameof(SpectrumCaptureDeviceStatus));
                });
            }
            catch (OperationCanceledException) { }
        }
    }

    private void RefreshCaptureDeviceOptions()
    {
        _updatingCaptureDevices = true;
        try
        {
            var options = new List<CaptureDeviceOption>
            { new(null, Translations.Get("Media.Spectrum.CaptureDevice.Automatic"), string.Empty) };
            foreach (var device in _lastCaptureDevices.DistinctBy(device => device.PolicyId, StringComparer.OrdinalIgnoreCase))
                options.Add(new(device.PolicyId, device.IsActive ? device.DisplayName :
                    Translations.Format("Media.Spectrum.CaptureDevice.Offline", device.DisplayName), device.SystemName));
            var settings = _configuration.Current.SpectrumComponent;
            if (settings.CaptureDeviceId is { } id && !options.Any(option => string.Equals(option.DeviceId, id, StringComparison.OrdinalIgnoreCase)))
                options.Add(new(id, Translations.Format("Media.Spectrum.CaptureDevice.Offline",
                    string.IsNullOrWhiteSpace(settings.CaptureDeviceName) ? id : settings.CaptureDeviceName), settings.CaptureDeviceName));
            SpectrumCaptureDevices = options;
            OnPropertyChanged(nameof(SpectrumCaptureDevices));
            OnPropertyChanged(nameof(SelectedSpectrumCaptureDevice));
            OnPropertyChanged(nameof(SpectrumCaptureDeviceStatus));
            OnPropertyChanged(nameof(IsDeviceCapture));
        }
        finally { _updatingCaptureDevices = false; }
    }

    private void CaptureDevicesChanged(object? sender, EventArgs args) => QueueCaptureDeviceRefresh();

    private void QueueCaptureDeviceRefresh()
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished ||
            Interlocked.Exchange(ref _captureDevicesRefreshQueued, 1) != 0) return;
        _dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _captureDevicesRefreshQueued, 0);
            if (!_disposed && _configuration.IsActive) _ = RefreshCaptureDevicesAsync();
        }));
    }

    private void DisposeCaptureDevices()
    {
        _captureDevicesVersion++;
        if (_captureDevicesCatalog is not null) _captureDevicesCatalog.Changed -= CaptureDevicesChanged;
    }
}
