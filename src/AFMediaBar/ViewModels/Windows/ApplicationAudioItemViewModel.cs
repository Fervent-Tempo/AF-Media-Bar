// 应用音量行与路由编辑状态；平台算法由注入服务处理，释放时取消所属设备请求。
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>应用面板中的音量及输入/输出路由，不为仅录音应用伪造音量。</summary>
public partial class ApplicationAudioItemViewModel : ObservableObject, IDisposable
{
    private readonly AudioDeviceSwitchCoordinator _switches;
    private bool _loading;
    private bool _disposed;
    private int _version;
    public ApplicationAudioSnapshot? Application { get; private set; }
    public ApplicationVolumeItemViewModel? Volume { get; private set; }
    public string Key => Application?.Identity ?? "volume:" + Volume?.ProcessName;
    public string DisplayName => Application?.DisplayName ?? Volume?.DisplayName ?? string.Empty;
    public byte[]? IconData => Volume?.IconData;
    public string Initial => string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName[..1].ToUpperInvariant();
    public bool IsCurrentMedia => Volume?.IsCurrentMedia == true;
    public bool CanAdjustVolume => Volume is not null && Application?.HasOutputSessions != false;
    public bool CanRoute => Application is not null;
    public string IconToolTip => CanAdjustVolume ? Volume!.MuteToolTip : DisplayName + " · " + Translations.Get("Audio.Route.NoPlayback");
    public ObservableCollection<AudioDeviceOption> OutputDevices { get; } = [];
    public ObservableCollection<AudioDeviceOption> InputDevices { get; } = [];
    [ObservableProperty] private bool _isDetailSelected;
    [ObservableProperty] private bool _isRouteSupported = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(OutputDeviceToolTip))] private AudioDeviceOption? _selectedOutputDevice;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(InputDeviceToolTip))] private AudioDeviceOption? _selectedInputDevice;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(OutputDeviceToolTip)), NotifyPropertyChangedFor(nameof(InputDeviceToolTip))]
    private string _statusText = string.Empty;
    public string OutputDeviceToolTip => DeviceToolTip(SelectedOutputDevice);
    public string InputDeviceToolTip => DeviceToolTip(SelectedInputDevice);
    private string DeviceToolTip(AudioDeviceOption? device) => string.IsNullOrEmpty(StatusText)
        ? device?.DisplayName ?? Translations.Get("Audio.Route.HiddenOrDisconnected")
        : (device is null ? string.Empty : device.DisplayName + "\n") + StatusText;

    public ApplicationAudioItemViewModel(ApplicationAudioSnapshot? application, ApplicationVolumeItemViewModel? volume,
        AudioDeviceSwitchCoordinator switches)
    {
        Application = application;
        Volume = volume;
        if (Volume is not null) Volume.PropertyChanged += OnVolumeChanged;
        _switches = switches;
        _switches.Updated += OnSwitchUpdated;
    }
    public void Synchronize(ApplicationAudioSnapshot? application, ApplicationVolumeItemViewModel? volume)
    {
        Application = application;
        if (Volume is not null) Volume.PropertyChanged -= OnVolumeChanged;
        Volume = volume;
        if (Volume is not null) Volume.PropertyChanged += OnVolumeChanged;
        OnPropertyChanged(string.Empty);
        if (IsDetailSelected) _ = RefreshAsync();
    }
    private void OnVolumeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => OnPropertyChanged(nameof(IconToolTip));
    public void RefreshLabels()
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            foreach (var direction in Enum.GetValues<AudioDeviceDirection>())
            {
                var choices = direction == AudioDeviceDirection.Output ? OutputDevices : InputDevices;
                for (var index = 0; index < choices.Count; index++)
                {
                    var old = choices[index];
                    if (!old.IsSystemDefaultChoice) continue;
                    var replacement = old with { DisplayName = Translations.Get("Audio.Route.FollowDefault") };
                    choices[index] = replacement;
                    if (direction == AudioDeviceDirection.Output && ReferenceEquals(SelectedOutputDevice, old)) SelectedOutputDevice = replacement;
                    if (direction == AudioDeviceDirection.Input && ReferenceEquals(SelectedInputDevice, old)) SelectedInputDevice = replacement;
                }
            }
            OnPropertyChanged(nameof(IconToolTip));
        }
        finally { _loading = wasLoading; }
    }
    partial void OnSelectedOutputDeviceChanged(AudioDeviceOption? value) { if (!_loading && value is not null) _ = ApplyAsync(AudioDeviceDirection.Output, value); }
    partial void OnSelectedInputDeviceChanged(AudioDeviceOption? value) { if (!_loading && value is not null) _ = ApplyAsync(AudioDeviceDirection.Input, value); }

    public async Task RefreshAsync()
    {
        if (_disposed || Application is not { } app) return;
        var version = ++_version;
        try
        {
            var output = await _switches.GetChoicesAsync(new(AudioDeviceDirection.Output, app));
            var input = await _switches.GetChoicesAsync(new(AudioDeviceDirection.Input, app));
            var outputRoute = await _switches.ReadRouteAsync(new(AudioDeviceDirection.Output, app));
            var inputRoute = await _switches.ReadRouteAsync(new(AudioDeviceDirection.Input, app));
            if (_disposed || version != _version) return;
            _loading = true;
            OutputDevices.Clear(); foreach (var device in output) OutputDevices.Add(device);
            InputDevices.Clear(); foreach (var device in input) InputDevices.Add(device);
            var outputPreview = _switches.GetPreview(new(AudioDeviceDirection.Output, app));
            var inputPreview = _switches.GetPreview(new(AudioDeviceDirection.Input, app));
            SelectedOutputDevice = outputPreview is null ? FindChoice(outputRoute, output) : output.FirstOrDefault(device => device.PolicyId == outputPreview.PolicyId);
            SelectedInputDevice = inputPreview is null ? FindChoice(inputRoute, input) : input.FirstOrDefault(device => device.PolicyId == inputPreview.PolicyId);
            IsRouteSupported = outputRoute.IsSupported && inputRoute.IsSupported;
            StatusText = !IsRouteSupported ? Translations.Get("Audio.Route.Unsupported") :
                outputRoute.Error is not null || inputRoute.Error is not null ? Translations.Get("Audio.Route.ReadFailed") :
                outputRoute.IsMixed || inputRoute.IsMixed ? Translations.Get("Audio.Route.Mixed") :
                SelectedOutputDevice is null || SelectedInputDevice is null ? Translations.Get("Audio.Route.HiddenOrDisconnected") : string.Empty;
        }
        catch (Exception) { if (!_disposed && version == _version) StatusText = Translations.Get("Audio.Route.ReadFailed"); }
        finally { _loading = false; }
    }
    private static AudioDeviceOption? FindChoice(ApplicationAudioRoute route, IReadOnlyList<AudioDeviceOption> choices) =>
        !route.IsSupported || route.IsMixed || route.Error is not null ? null :
        choices.FirstOrDefault(choice => route.EndpointId is null ? choice.IsSystemDefaultChoice :
            string.Equals(choice.PolicyId, route.EndpointId, StringComparison.OrdinalIgnoreCase));
    public async Task PreviewDeviceAsync(AudioDeviceDirection direction, int steps)
    {
        if (_disposed || Application is not { } app) return;
        try { await _switches.PreviewAsync(new(direction, app), steps, this); }
        catch (Exception) { if (!_disposed) StatusText = Translations.Get("Audio.Route.Failed"); }
    }

    private void OnSwitchUpdated(AudioDeviceSwitchUpdate update)
    {
        if (_disposed || update.Target.Application?.Identity != Application?.Identity) return;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { if (!_disposed) _ = ApplyUpdateAsync(update); });
    }
    private async Task ApplyUpdateAsync(AudioDeviceSwitchUpdate update)
    {
        try
        {
            if (_disposed || update.Target.Application?.Identity != Application?.Identity) return;
            if (update.Result is null)
            {
                _loading = true;
                var choices = update.Target.Direction == AudioDeviceDirection.Output ? OutputDevices : InputDevices;
                var choice = choices.FirstOrDefault(device => device.PolicyId == update.Candidate?.PolicyId);
                if (update.Target.Direction == AudioDeviceDirection.Output) SelectedOutputDevice = choice;
                else SelectedInputDevice = choice;
                _loading = false;
                StatusText = update.Candidate?.DisplayName ?? Translations.Get("Audio.Device.Unavailable");
            }
            else
            {
                await RefreshAsync();
                if (!_disposed) StatusText = AudioDevicePresentation.GetResultText(update.Result, true);
            }
        }
        catch (Exception) { if (!_disposed) StatusText = Translations.Get("Audio.Route.Failed"); }
        finally { _loading = false; }
    }

    private async Task ApplyAsync(AudioDeviceDirection direction, AudioDeviceOption choice)
    {
        if (_disposed || Application is not { } app) return;
        try
        {
            var result = await _switches.SelectAsync(new(direction, app), choice, this);
            if (_disposed) return;
            await RefreshAsync();
            if (!_disposed) StatusText = AudioDevicePresentation.GetResultText(result, application: true);
        }
        catch (Exception) { if (!_disposed) StatusText = Translations.Get("Audio.Route.Failed"); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _version++;
        if (Volume is not null) Volume.PropertyChanged -= OnVolumeChanged;
        _switches.Updated -= OnSwitchUpdated;
        _switches.CancelOwner(this);
    }
}
