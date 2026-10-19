// 音频设备允许列表编辑器；仅在 UI 线程写入设置，后台枚举经配置上下文取消。
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Input;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>输入/输出黑名单和通信角色的全局设置编辑器。</summary>
public sealed class AudioDeviceSettingsViewModel : ObservableObject, IDisposable
{
    private readonly AudioDeviceHotkeyService? _hotkeys;
    private readonly IAudioDeviceCatalog _devices;
    private readonly ISettingsConfiguration _configuration;
    private bool _disposed;
    private IReadOnlyList<AudioDeviceOption> _allDevices = [];
    public bool IsHotkeysEnabled => _configuration.Current.Experiments.NewAudioControl && _configuration.Current.Experiments.DeviceHotkeys;
    public string OutputCycleHotkey { get => _configuration.Current.AudioDevices.OutputCycleHotkey; set => SetCycleHotkey(AudioDeviceDirection.Output, value); }
    public string InputCycleHotkey { get => _configuration.Current.AudioDevices.InputCycleHotkey; set => SetCycleHotkey(AudioDeviceDirection.Input, value); }
    public string OutputCycleStatus => _hotkeys?.GetStatus(AudioDeviceDirection.Output, null) ?? string.Empty;
    public string InputCycleStatus => _hotkeys?.GetStatus(AudioDeviceDirection.Input, null) ?? string.Empty;
    internal string HotkeyStatus(AudioDeviceOption device) => _hotkeys?.GetStatus(device.Direction, device.PolicyId) ?? string.Empty;
    private void SetCycleHotkey(AudioDeviceDirection direction, string value)
    {
        if (_disposed || !_configuration.IsActive || !IsHotkeysEnabled) return;
        var settings = _configuration.Current.AudioDevices;
        _configuration.SetAudioDeviceSettings(direction == AudioDeviceDirection.Output ? settings with { OutputCycleHotkey = value } : settings with { InputCycleHotkey = value });
        OnHotkeysChanged(null, EventArgs.Empty);
    }
    private void OnHotkeysChanged(object? sender, EventArgs args)
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(OutputCycleStatus)); OnPropertyChanged(nameof(InputCycleStatus));
        foreach (var row in OutputDevices.Concat(InputDevices).Concat(OtherOutputDevices).Concat(OtherInputDevices)) row.RefreshHotkeyStatus();
    }
    public bool IsPersonalizationEnabled => _configuration.Current.Experiments.NewAudioControl && _configuration.Current.Experiments.DevicePersonalization;
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
    public AudioDeviceSettingsViewModel(IAudioDeviceCatalog devices, ISettingsConfiguration configuration, AudioDeviceHotkeyService? hotkeys = null)
    {
        _hotkeys = hotkeys;
        if (_hotkeys is not null) _hotkeys.Changed += OnHotkeysChanged;
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
            _allDevices = output.Concat(input).ToArray();
            var settings = _configuration.Current.AudioDevices;
            if (IsPersonalizationEnabled || IsHotkeysEnabled)
            {
                var preferences = settings.Preferences.ToList();
                foreach (var device in _allDevices)
                {
                    var index = preferences.FindIndex(entry => entry.Direction == device.Direction && entry.EndpointId.Equals(device.PolicyId, StringComparison.OrdinalIgnoreCase));
                    if (index < 0) preferences.Add(new(device.Direction, device.PolicyId, device.SystemName));
                    else preferences[index] = preferences[index] with { LastKnownName = device.SystemName };
                }
                if (!settings.Preferences.SequenceEqual(preferences))
                {
                    settings = settings with { Preferences = preferences.ToArray() };
                    _configuration.SetAudioDeviceSettings(settings);
                }
            }
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
        var preferences = settings.Preferences.Where(entry => entry.Direction == direction).ToArray();
        var known = blocked.Concat(preferences.Select(entry => new AudioDeviceExclusion(entry.EndpointId, entry.LastKnownName)))
            .DistinctBy(entry => entry.EndpointId, StringComparer.OrdinalIgnoreCase);
        var all = devices.Concat(known.Where(entry => !devices.Any(device => device.PolicyId.Equals(entry.EndpointId, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => new AudioDeviceOption(entry.EndpointId, entry.EndpointId, entry.DisplayName, false) { Direction = direction, IsActive = false, Availability = AudioDeviceAvailability.Unavailable }));
        rows.Clear();
        otherRows.Clear();
        foreach (var device in all.GroupBy(device => device.PolicyId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(device => device.IsActive).First())
            .OrderByDescending(device => IsPersonalizationEnabled && settings.FindPreference(direction, device.PolicyId)?.IsFavorite == true)
            .ThenBy(device => IsPersonalizationEnabled ? Array.FindIndex(preferences, entry => entry.EndpointId.Equals(device.PolicyId, StringComparison.OrdinalIgnoreCase)) is var index && index >= 0 ? index : int.MaxValue : 0)
            .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(device => device.PolicyId, StringComparer.OrdinalIgnoreCase))
        {
            var destination = device.IsActive ? rows : otherRows;
            destination.Add(new(device, settings.IsAllowed(direction, device.PolicyId), allowed => SetAllowed(device, allowed), this));
        }
    }
    internal AudioDevicePreference Preference(AudioDeviceOption device) => _configuration.Current.AudioDevices.FindPreference(device.Direction, device.PolicyId)
        ?? new(device.Direction, device.PolicyId, device.SystemName);
    internal IReadOnlyList<AudioDeviceOption> Replacements(AudioDeviceOption device) => _allDevices.Where(item => item.IsActive && item.Direction == device.Direction &&
        !item.PolicyId.Equals(device.PolicyId, StringComparison.OrdinalIgnoreCase)).ToArray();
    internal void UpdatePreference(AudioDeviceOption device, Func<AudioDevicePreference, AudioDevicePreference> update)
    {
        if (_disposed || !_configuration.IsActive || (!IsPersonalizationEnabled && !IsHotkeysEnabled)) return;
        var settings = _configuration.Current.AudioDevices;
        var preferences = settings.Preferences.ToList();
        var index = preferences.FindIndex(entry => entry.Direction == device.Direction && entry.EndpointId.Equals(device.PolicyId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) preferences.Add(update(Preference(device))); else preferences[index] = update(preferences[index]);
        _configuration.SetAudioDeviceSettings(settings with { Preferences = preferences.ToArray() });
    }
    internal void Move(AudioDeviceOption device, int step)
    {
        if (_disposed || !_configuration.IsActive || !IsPersonalizationEnabled) return;
        var settings = _configuration.Current.AudioDevices;
        var entries = settings.Preferences.ToList();
        var candidates = entries.Where(entry => entry.Direction == device.Direction && entry.IsFavorite == Preference(device).IsFavorite).ToList();
        var index = candidates.FindIndex(entry => entry.EndpointId.Equals(device.PolicyId, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + step < 0 || index + step >= candidates.Count) return;
        var a = entries.IndexOf(candidates[index]); var b = entries.IndexOf(candidates[index + step]);
        (entries[a], entries[b]) = (entries[b], entries[a]);
        _configuration.SetAudioDeviceSettings(settings with { Preferences = entries.ToArray() });
        _ = RefreshAsync();
    }
    internal void RemoveOrRebind(AudioDeviceOption old, AudioDeviceOption? replacement)
    {
        if (_disposed || !_configuration.IsActive || !IsPersonalizationEnabled || old.IsActive) return;
        var settings = _configuration.Current.AudioDevices;
        var entries = settings.Preferences.Where(entry => entry.Direction != old.Direction ||
            (!entry.EndpointId.Equals(old.PolicyId, StringComparison.OrdinalIgnoreCase) &&
             (replacement is null || !entry.EndpointId.Equals(replacement.PolicyId, StringComparison.OrdinalIgnoreCase)))).ToList();
        if (replacement is not null) entries.Add(Preference(old) with { EndpointId = replacement.PolicyId, LastKnownName = replacement.SystemName });
        var blacklist = (old.Direction == AudioDeviceDirection.Output ? settings.OutputBlacklist : settings.InputBlacklist)
            .Where(entry => !entry.EndpointId.Equals(old.PolicyId, StringComparison.OrdinalIgnoreCase) &&
                (replacement is null || !entry.EndpointId.Equals(replacement.PolicyId, StringComparison.OrdinalIgnoreCase))).ToList();
        if (replacement is not null && !settings.IsAllowed(old.Direction, old.PolicyId)) blacklist.Add(new(replacement.PolicyId, replacement.SystemName));
        _configuration.SetAudioDeviceSettings(old.Direction == AudioDeviceDirection.Output
            ? settings with { Preferences = entries.ToArray(), OutputBlacklist = blacklist.ToArray() }
            : settings with { Preferences = entries.ToArray(), InputBlacklist = blacklist.ToArray() });
        _ = RefreshAsync();
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
        if (_hotkeys is not null) _hotkeys.Changed -= OnHotkeysChanged;
        _devices.Changed -= OnDevicesChanged;
        _configuration.Activated -= OnActivated;
    }
}

/// <summary>允许列表中的一个端点，断开设备仍可解除隐藏。</summary>
public sealed partial class AudioDevicePermissionItemViewModel(AudioDeviceOption device, bool allowed, Action<bool> changed, AudioDeviceSettingsViewModel owner) : ObservableObject
{
    public bool IsPersonalizationEnabled => owner.IsPersonalizationEnabled;
    public bool IsHotkeysEnabled => owner.IsHotkeysEnabled;
    public string Hotkey { get => owner.Preference(device).Hotkey; set { owner.UpdatePreference(device, entry => entry with { Hotkey = value }); OnPropertyChanged(); OnPropertyChanged(nameof(HotkeyStatus)); } }
    public string HotkeyStatus => owner.HotkeyStatus(device);
    internal void RefreshHotkeyStatus() => OnPropertyChanged(nameof(HotkeyStatus));
    public bool IsOffline => !device.IsActive;
    public string Alias { get => owner.Preference(device).Alias; set { owner.UpdatePreference(device, entry => entry with { Alias = value }); OnPropertyChanged(); } }
    public bool IsFavorite { get => owner.Preference(device).IsFavorite; set { owner.UpdatePreference(device, entry => entry with { IsFavorite = value }); OnPropertyChanged(); } }
    public IReadOnlyList<AudioDeviceOption> Replacements => owner.Replacements(device);
    public AudioDeviceOption? Replacement { get; set; }
    [RelayCommand] private void MoveUp() => owner.Move(device, -1);
    [RelayCommand] private void MoveDown() => owner.Move(device, 1);
    [RelayCommand] private void Forget() => owner.RemoveOrRebind(device, null);
    [RelayCommand] private void Rebind() { if (Replacement is not null) owner.RemoveOrRebind(device, Replacement); }
    private bool _isAllowed = allowed;
    public string DisplayName => device.SystemName;
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
