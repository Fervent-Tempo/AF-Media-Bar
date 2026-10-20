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
    private IReadOnlyList<AudioDeviceOption> _allDevices = [];
    public bool IsHotkeysEnabled => !_disposed && _configuration.IsActive;
    public IRelayCommand<SettingsReorderRequest> ReorderDevicesCommand { get; }
    public bool IsOrderingEnabled => IsPersonalizationEnabled || IsHotkeysEnabled;
    public bool IsPersonalizationEnabled => !_disposed && _configuration.IsActive;
    public bool IsEnabled => !_disposed && _configuration.IsActive;
    public bool CanEdit => !_disposed && _configuration.IsActive;
    public void Reset() => _configuration.Reset(SettingsResetScope.DeviceManagement);
    public CancellationToken CancellationToken => _configuration.CancellationToken;
    private bool _refreshQueued;
    private int _version;
    public ObservableCollection<AudioDevicePermissionItemViewModel> OutputDevices { get; } = [];
    public ObservableCollection<AudioDevicePermissionItemViewModel> InputDevices { get; } = [];
    public ObservableCollection<AudioDevicePermissionItemViewModel> DisabledOutputDevices { get; } = [];
    public ObservableCollection<AudioDevicePermissionItemViewModel> DisabledInputDevices { get; } = [];
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
        ReorderDevicesCommand = new RelayCommand<SettingsReorderRequest>(Reorder);
        _devices = devices;
        _configuration = configuration;
        devices.Changed += OnDevicesChanged;
        configuration.Activated += OnActivated;
        SettingsManager.SettingsChanged += OnSettingsChanged;
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
            Synchronize(OutputDevices, DisabledOutputDevices, OtherOutputDevices, output, AudioDeviceDirection.Output);
            Synchronize(InputDevices, DisabledInputDevices, OtherInputDevices, input, AudioDeviceDirection.Input);
            StatusText = string.Empty;
            OnPropertyChanged(string.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_disposed && version == _version) StatusText = Translations.Get("Audio.Route.ReadFailed"); }
    }
    private static IReadOnlyList<AudioDeviceExclusion> UpdateNames(IReadOnlyList<AudioDeviceExclusion> entries, IReadOnlyList<AudioDeviceOption> devices) =>
        entries.Select(entry => devices.FirstOrDefault(device => device.PolicyId.Equals(entry.EndpointId, StringComparison.OrdinalIgnoreCase)) is { } device ?
            entry with { DisplayName = device.SystemName } : entry).ToArray();

    private void Synchronize(ObservableCollection<AudioDevicePermissionItemViewModel> rows,
        ObservableCollection<AudioDevicePermissionItemViewModel> disabledRows,
        ObservableCollection<AudioDevicePermissionItemViewModel> otherRows,
        IReadOnlyList<AudioDeviceOption> devices, AudioDeviceDirection direction)
    {
        var settings = _configuration.Current.AudioDevices;
        var blocked = direction == AudioDeviceDirection.Output ? settings.OutputBlacklist : settings.InputBlacklist;
        var preferences = settings.Preferences.Where(entry => entry.Direction == direction).ToArray();
        var known = preferences.Select(entry => new AudioDeviceExclusion(entry.EndpointId, entry.LastKnownName)).Concat(blocked)
            .DistinctBy(entry => entry.EndpointId, StringComparer.OrdinalIgnoreCase);
        var all = devices.Concat(known.Where(entry => !devices.Any(device => device.PolicyId.Equals(entry.EndpointId, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => new AudioDeviceOption(entry.EndpointId, entry.EndpointId, entry.DisplayName, false) { Direction = direction, IsActive = false, Availability = AudioDeviceAvailability.Unavailable }));
        var existing = rows.Concat(disabledRows).Concat(otherRows).ToDictionary(row => row.EndpointId, StringComparer.OrdinalIgnoreCase);
        var active = new List<AudioDevicePermissionItemViewModel>();
        var inactive = new List<AudioDevicePermissionItemViewModel>();
        var disabled = new List<AudioDevicePermissionItemViewModel>();
        foreach (var device in all.GroupBy(device => device.PolicyId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(device => device.IsActive).First())
            .OrderBy(device => IsOrderingEnabled ? Array.FindIndex(preferences, entry => entry.EndpointId.Equals(device.PolicyId, StringComparison.OrdinalIgnoreCase)) is var index && index >= 0 ? index : int.MaxValue : 0)
            .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(device => device.PolicyId, StringComparer.OrdinalIgnoreCase))
        {
            if (!existing.TryGetValue(device.PolicyId, out var row)) row = new(device, settings.IsAllowed(direction, device.PolicyId), this);
            else row.Synchronize(device, settings.IsAllowed(direction, device.PolicyId));
            (!device.IsActive ? inactive : row.IsAllowed ? active : disabled).Add(row);
        }
        SynchronizeRows(rows, active);
        SynchronizeRows(disabledRows, disabled);
        SynchronizeRows(otherRows, inactive);
    }
    private static void SynchronizeRows(ObservableCollection<AudioDevicePermissionItemViewModel> rows, IReadOnlyList<AudioDevicePermissionItemViewModel> desired)
    {
        for (var index = rows.Count - 1; index >= 0; index--) if (!desired.Contains(rows[index])) rows.RemoveAt(index);
        for (var index = 0; index < desired.Count; index++)
        {
            var current = rows.IndexOf(desired[index]);
            if (current < 0) rows.Insert(index, desired[index]);
            else if (current != index) rows.Move(current, index);
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
    private void Reorder(SettingsReorderRequest? request)
    {
        if (!CanEdit || !IsOrderingEnabled || request?.Item is not AudioDevicePermissionItemViewModel source) return;
        var rows = source.Direction == AudioDeviceDirection.Output ? OutputDevices : InputDevices;
        if (!request.TryResolve(rows, row => row.EndpointId, out var from, out var to)) return;
        var settings = _configuration.Current.AudioDevices;
        var entries = settings.Preferences.ToList();
        var oldIndex = entries.FindIndex(entry => entry.Direction == source.Direction && entry.EndpointId.Equals(rows[from].EndpointId, StringComparison.OrdinalIgnoreCase));
        var newIndex = entries.FindIndex(entry => entry.Direction == source.Direction && entry.EndpointId.Equals(rows[to].EndpointId, StringComparison.OrdinalIgnoreCase));
        if (oldIndex < 0 || newIndex < 0 || rows[from].IsOffline || !rows[from].IsAllowed) return;
        var moving = entries[oldIndex]; entries.RemoveAt(oldIndex); entries.Insert(newIndex, moving);
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
    internal void SetAllowed(AudioDeviceOption device, bool allowed)
    {
        if (_disposed || !_configuration.IsActive) return;
        var settings = _configuration.Current.AudioDevices;
        var entries = (device.Direction == AudioDeviceDirection.Output ? settings.OutputBlacklist : settings.InputBlacklist)
            .Where(entry => !entry.EndpointId.Equals(device.PolicyId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!allowed) entries.Add(new(device.PolicyId, device.SystemName));
        _configuration.SetAudioDeviceSettings(device.Direction == AudioDeviceDirection.Output ?
            settings with { OutputBlacklist = entries.ToArray() } : settings with { InputBlacklist = entries.ToArray() });
    }
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_disposed || args.PropertyName is not (null or nameof(AppSettings.Experiments) or nameof(AppSettings.AudioDevices))) return;
        QueueRefresh();
    }
    private void QueueRefresh()
    {
        if (_disposed || _refreshQueued) return;
        var dispatcher = Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread);
        if (dispatcher is null) return;
        _refreshQueued = true;
        dispatcher.BeginInvoke(() => { _refreshQueued = false; if (CanEdit) _ = RefreshAsync(); });
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
        SettingsManager.SettingsChanged -= OnSettingsChanged;
    }
}

/// <summary>允许列表中的一个端点，断开设备仍可解除隐藏。</summary>
public sealed partial class AudioDevicePermissionItemViewModel : ObservableObject
{
    private AudioDeviceOption _device;
    private readonly AudioDeviceSettingsViewModel _owner;
    private bool _isAllowed;
    public AudioDevicePermissionItemViewModel(AudioDeviceOption device, bool allowed, AudioDeviceSettingsViewModel owner)
    { _device = device; _isAllowed = allowed; _owner = owner; }
    internal void Synchronize(AudioDeviceOption device, bool allowed)
    { _device = device; _isAllowed = allowed; if (!IsPersonalizationEnabled) IsRenaming = false; OnPropertyChanged(string.Empty); }
    public bool IsPersonalizationEnabled => _owner.IsPersonalizationEnabled;
    public bool CanReorder => _owner.IsOrderingEnabled && IsAllowed && !IsOffline;
    public AudioDeviceDirection Direction => _device.Direction;
    public bool IsOffline => !_device.IsActive;
    public bool CanRebind => IsOffline && IsPersonalizationEnabled;
    public string Alias { get => _owner.Preference(_device).Alias; set { _owner.UpdatePreference(_device, entry => entry with { Alias = value }); OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
    private bool _isRenaming;
    private string _aliasDraft = string.Empty;
    public bool IsRenaming { get => _isRenaming; private set => SetProperty(ref _isRenaming, value); }
    public string AliasDraft { get => _aliasDraft; set => SetProperty(ref _aliasDraft, value); }
    [RelayCommand] private void BeginRename()
    {
        if (!_owner.CanEdit || !IsPersonalizationEnabled) return;
        AliasDraft = DisplayName;
        IsRenaming = true;
    }
    [RelayCommand] private void CommitRename()
    {
        if (!IsRenaming) return;
        if (_owner.CanEdit && IsPersonalizationEnabled)
            Alias = AliasDraft.Trim() == SystemName ? string.Empty : AliasDraft;
        IsRenaming = false;
    }
    [RelayCommand] private void CancelRename() => IsRenaming = false;
    public string SummaryText => StateText;
    public IReadOnlyList<AudioDeviceOption> Replacements => _owner.Replacements(_device);
    public AudioDeviceOption? Replacement { get; set; }
    [RelayCommand] private void Forget() => _owner.RemoveOrRebind(_device, null);
    [RelayCommand] private void Rebind() { if (Replacement is not null) _owner.RemoveOrRebind(_device, Replacement); }
    public string DisplayName => IsPersonalizationEnabled && !string.IsNullOrWhiteSpace(Alias) ? Alias : _device.SystemName;
    public string SystemName => _device.SystemName;
    public string EndpointId => _device.PolicyId;
    public AudioDeviceAvailability Availability => _device.IsActive ? AudioDeviceAvailability.Active :
        _device.Availability == AudioDeviceAvailability.Active ? AudioDeviceAvailability.Unavailable : _device.Availability;
    public string StateText => Availability switch
    {
        AudioDeviceAvailability.Active => _device.IsDefault ? Translations.Get("Device.Default") : string.Empty,
        AudioDeviceAvailability.Disabled => Translations.Get("Audio.Device.SystemDisabled"),
        AudioDeviceAvailability.Unplugged => Translations.Get("Audio.Device.Unplugged"),
        _ => Translations.Get("Audio.Device.NotPresent")
    };
    public bool IsAllowed
    {
        get => _isAllowed;
        set { if (SetProperty(ref _isAllowed, value)) _owner.SetAllowed(_device, value); }
    }
}
