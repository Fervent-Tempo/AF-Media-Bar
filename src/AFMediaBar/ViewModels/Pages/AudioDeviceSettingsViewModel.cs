// 音频设备允许列表编辑器；仅在 UI 线程写入设置，后台枚举经配置上下文取消。
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>输入/输出黑名单和通信角色的全局设置编辑器。</summary>
public sealed class AudioDeviceSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IAudioDeviceCatalog _devices;
    private readonly ISettingsConfiguration _configuration;
    private bool _disposed;
    private int _version;
    public ObservableCollection<AudioDevicePermissionItemViewModel> OutputDevices { get; } = [];
    public ObservableCollection<AudioDevicePermissionItemViewModel> InputDevices { get; } = [];
    public ObservableCollection<AudioDevicePermissionItemViewModel> OtherOutputDevices { get; } = [];
    public ObservableCollection<AudioDevicePermissionItemViewModel> OtherInputDevices { get; } = [];
    private string _statusText = string.Empty;
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool SwitchOutputCommunications
    {
        get => _configuration.Current.AudioDevices.SwitchOutputCommunications;
        set => _configuration.SetAudioDeviceSettings(_configuration.Current.AudioDevices with { SwitchOutputCommunications = value });
    }
    public bool SwitchInputCommunications
    {
        get => _configuration.Current.AudioDevices.SwitchInputCommunications;
        set => _configuration.SetAudioDeviceSettings(_configuration.Current.AudioDevices with { SwitchInputCommunications = value });
    }
    public AudioDeviceSettingsViewModel(IAudioDeviceCatalog devices, ISettingsConfiguration configuration)
    {
        _devices = devices;
        _configuration = configuration;
        devices.Changed += OnDevicesChanged;
        configuration.Activated += OnActivated;
    }
    public async Task RefreshAsync()
    {
        if (_disposed || !_configuration.IsActive) return;
        var version = ++_version;
        var token = _configuration.CancellationToken;
        try
        {
            var output = await _devices.GetDevicesAsync(AudioDeviceDirection.Output, true, token);
            var input = await _devices.GetDevicesAsync(AudioDeviceDirection.Input, true, token);
            if (_disposed || token.IsCancellationRequested || !_configuration.IsActive || version != _version) return;
            var settings = _configuration.Current.AudioDevices;
            var outputNames = UpdateNames(settings.OutputBlacklist, output);
            var inputNames = UpdateNames(settings.InputBlacklist, input);
            if (!settings.OutputBlacklist.SequenceEqual(outputNames) || !settings.InputBlacklist.SequenceEqual(inputNames))
                _configuration.SetAudioDeviceSettings(settings with { OutputBlacklist = outputNames, InputBlacklist = inputNames });
            Synchronize(OutputDevices, OtherOutputDevices, output, AudioDeviceDirection.Output);
            Synchronize(InputDevices, OtherInputDevices, input, AudioDeviceDirection.Input);
            StatusText = string.Empty;
            OnPropertyChanged(string.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_disposed && version == _version) StatusText = Translations.Get("Audio.Route.ReadFailed"); }
    }
    private static IReadOnlyList<AudioDeviceExclusion> UpdateNames(IReadOnlyList<AudioDeviceExclusion> entries, IReadOnlyList<AudioDeviceOption> devices) =>
        entries.Select(entry => devices.FirstOrDefault(device => device.PolicyId.Equals(entry.EndpointId, StringComparison.OrdinalIgnoreCase)) is { } device ?
            entry with { DisplayName = device.DisplayName } : entry).ToArray();

    private void Synchronize(ObservableCollection<AudioDevicePermissionItemViewModel> rows,
        ObservableCollection<AudioDevicePermissionItemViewModel> otherRows,
        IReadOnlyList<AudioDeviceOption> devices, AudioDeviceDirection direction)
    {
        var settings = _configuration.Current.AudioDevices;
        var blocked = direction == AudioDeviceDirection.Output ? settings.OutputBlacklist : settings.InputBlacklist;
        var all = devices.Concat(blocked.Where(entry => !devices.Any(device => device.PolicyId.Equals(entry.EndpointId, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => new AudioDeviceOption(entry.EndpointId, entry.EndpointId, entry.DisplayName, false) { Direction = direction, IsActive = false, Availability = AudioDeviceAvailability.Unavailable }));
        rows.Clear();
        otherRows.Clear();
        foreach (var device in all.GroupBy(device => device.PolicyId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(device => device.IsActive).First())
            .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(device => device.PolicyId, StringComparer.OrdinalIgnoreCase))
        {
            var destination = device.IsActive ? rows : otherRows;
            destination.Add(new(device, settings.IsAllowed(direction, device.PolicyId), allowed => SetAllowed(device, allowed)));
        }
    }
    private void SetAllowed(AudioDeviceOption device, bool allowed)
    {
        if (_disposed || !_configuration.IsActive) return;
        var settings = _configuration.Current.AudioDevices;
        var entries = (device.Direction == AudioDeviceDirection.Output ? settings.OutputBlacklist : settings.InputBlacklist)
            .Where(entry => !entry.EndpointId.Equals(device.PolicyId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!allowed) entries.Add(new(device.PolicyId, device.DisplayName));
        _configuration.SetAudioDeviceSettings(device.Direction == AudioDeviceDirection.Output ?
            settings with { OutputBlacklist = entries.ToArray() } : settings with { InputBlacklist = entries.ToArray() });
    }
    private void OnActivated(object? sender, EventArgs args) => _ = RefreshAsync();
    private void OnDevicesChanged(object? sender, EventArgs args) =>
        Application.Current?.Dispatcher.BeginInvoke(() => { if (!_disposed && _configuration.IsActive) _ = RefreshAsync(); });
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _version++;
        _devices.Changed -= OnDevicesChanged;
        _configuration.Activated -= OnActivated;
    }
}

/// <summary>允许列表中的一个端点，断开设备仍可解除隐藏。</summary>
public sealed class AudioDevicePermissionItemViewModel(AudioDeviceOption device, bool allowed, Action<bool> changed) : ObservableObject
{
    private bool _isAllowed = allowed;
    public string DisplayName => device.DisplayName;
    public string EndpointId => device.PolicyId;
    public AudioDeviceAvailability Availability => device.IsActive ? AudioDeviceAvailability.Active :
        device.Availability == AudioDeviceAvailability.Active ? AudioDeviceAvailability.Unavailable : device.Availability;
    public string StateText => Availability switch
    {
        AudioDeviceAvailability.Active => string.Empty,
        AudioDeviceAvailability.Disabled => Translations.Get("Audio.Device.SystemDisabled"),
        AudioDeviceAvailability.Unplugged => Translations.Get("Audio.Device.Unplugged"),
        _ => Translations.Get("Audio.Device.NotPresent")
    };
    public bool IsAllowed
    {
        get => _isAllowed;
        set { if (SetProperty(ref _isAllowed, value)) changed(value); }
    }
}
