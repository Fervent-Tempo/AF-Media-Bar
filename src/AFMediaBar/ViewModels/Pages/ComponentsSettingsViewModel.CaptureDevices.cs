// 设备候选复用音频目录；只保存端点标识和名称，离开页面上下文后拒绝迟到枚举结果。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

public sealed partial class ComponentsSettingsViewModel
{
    /// <summary>
    /// 频谱设备选择的呈现条目。<see cref="Mode"/> 表达该条目代表的设备策略；<see cref="DeviceId"/> 只在
    /// <see cref="SpectrumCaptureDeviceMode.SpecificDevice"/> 下有意义，「跟随媒体来源」与「系统默认输出设备」
    /// 两个条目都带 null 标识，靠模式区分。
    /// A spectrum device entry. Mode states the strategy the entry stands for; DeviceId only means anything under
    /// SpecificDevice, while the follow-media-source and system-default entries both carry a null id and are told
    /// apart by their mode.
    /// </summary>
    public sealed record CaptureDeviceOption(string? DeviceId, string DisplayName, string SystemName, SpectrumCaptureDeviceMode Mode);

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
        get
        {
            var settings = _configuration.Current.SpectrumComponent;
            // 选中项按生效策略匹配：旧配置缺策略字段时按 CaptureDeviceId 回退，显示与升级前一致。
            // 非指定设备模式下保存的设备标识不参与匹配，否则切过去再回来会看到错选的设备。
            return settings.ResolveCaptureDeviceMode() switch
            {
                SpectrumCaptureDeviceMode.SystemDefault => SpectrumCaptureDevices.FirstOrDefault(option =>
                    option.Mode == SpectrumCaptureDeviceMode.SystemDefault),
                SpectrumCaptureDeviceMode.FollowMediaSource => SpectrumCaptureDevices.FirstOrDefault(option =>
                    option.Mode == SpectrumCaptureDeviceMode.FollowMediaSource),
                _ => SpectrumCaptureDevices.FirstOrDefault(option => option.Mode == SpectrumCaptureDeviceMode.SpecificDevice &&
                    string.Equals(option.DeviceId, settings.CaptureDeviceId, StringComparison.OrdinalIgnoreCase))
            };
        }
        set
        {
            if (_disposed || !_configuration.IsActive || _updatingCaptureDevices || value is null) return;
            var current = _configuration.Current.SpectrumComponent;
            // 非指定设备模式保留已保存的设备标识与名称，切回「指定设备」时恢复原选择；
            // 该标识在这些模式下不参与设备选择，保留只是为了让用户的选择不丢失。
            var specific = value.Mode == SpectrumCaptureDeviceMode.SpecificDevice;
            _configuration.SetSpectrum(current with
            {
                CaptureDeviceMode = value.Mode,
                CaptureDeviceId = specific ? value.DeviceId : current.CaptureDeviceId,
                CaptureDeviceName = specific ? value.SystemName : current.CaptureDeviceName
            });
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
            {
                new(null, Translations.Get("Media.Spectrum.CaptureDevice.Automatic"), string.Empty, SpectrumCaptureDeviceMode.FollowMediaSource),
                new(null, Translations.Get("Media.Spectrum.CaptureDevice.SystemDefault"), string.Empty, SpectrumCaptureDeviceMode.SystemDefault)
            };
            foreach (var device in _lastCaptureDevices.DistinctBy(device => device.PolicyId, StringComparer.OrdinalIgnoreCase))
                options.Add(new(device.PolicyId, device.IsActive ? device.DisplayName :
                    Translations.Format("Media.Spectrum.CaptureDevice.Offline", device.DisplayName), device.SystemName, SpectrumCaptureDeviceMode.SpecificDevice));
            var settings = _configuration.Current.SpectrumComponent;
            // 只有指定设备模式才需要把当前离线设备补回列表；其它模式下保存的设备标识不参与选择，
            // 补回来只会多一个永远选不中的幽灵项。
            if (settings.ResolveCaptureDeviceMode() == SpectrumCaptureDeviceMode.SpecificDevice &&
                settings.CaptureDeviceId is { } id && !options.Any(option => string.Equals(option.DeviceId, id, StringComparison.OrdinalIgnoreCase)))
                options.Add(new(id, Translations.Format("Media.Spectrum.CaptureDevice.Offline",
                    string.IsNullOrWhiteSpace(settings.CaptureDeviceName) ? id : settings.CaptureDeviceName), settings.CaptureDeviceName, SpectrumCaptureDeviceMode.SpecificDevice));
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
