// 枚举及切换输入/输出设备；平台执行器拥有 COM 线程，快照不持有原生对象。
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using NAudio.CoreAudioApi;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>设备编辑器的只读目录边界，可列出隐藏和断开设备。</summary>
public interface IAudioDeviceCatalog
{
    event EventHandler? Changed;
    Task<IReadOnlyList<AudioDeviceOption>> GetDevicesAsync(AudioDeviceDirection direction, bool includeHidden = false, CancellationToken cancellationToken = default);
}

/// <summary>统一设备选择过滤及提交校验；底层会话和频谱枚举不经过黑名单。</summary>
public sealed class AudioDeviceService : IAudioDeviceCatalog, IDisposable
{
    private readonly AudioPlatformService _platform;
    private AudioDeviceSettings _settings;
    private readonly Dictionary<AudioDeviceDirection, List<string>> _observedOrder = [];
    private volatile bool _disposed;
    private int _policyVersion;
    public int PolicyVersion => Volatile.Read(ref _policyVersion);
    public int Version => _platform.Version;
    public event EventHandler? Changed { add => _platform.Changed += value; remove => _platform.Changed -= value; }

    public AudioDeviceService(AudioPlatformService platform)
    {
        _platform = platform;
        _settings = SettingsManager.Current.AudioDevices;
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }


    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (args.PropertyName is not (null or nameof(AppSettings.AudioDevices) or nameof(AppSettings.Interaction) or nameof(AppSettings.Experiments))) return;
        Volatile.Write(ref _settings, SettingsManager.Current.AudioDevices);
        Interlocked.Increment(ref _policyVersion);
        _platform.Invalidate(cancelPending: true);
    }

    public bool IsAllowed(AudioDeviceDirection direction, string id) => Volatile.Read(ref _settings).IsAllowed(direction, id);
    public Task<IReadOnlyList<AudioDeviceOption>> GetRenderDevicesAsync() => GetDevicesAsync(AudioDeviceDirection.Output);
    public Task<IReadOnlyList<AudioDeviceOption>> GetDevicesAsync(AudioDeviceDirection direction,
        bool includeHidden = false, CancellationToken cancellationToken = default) =>
        _platform.RunAsync(() => Enumerate(direction, includeHidden), cancellationToken);

    private IReadOnlyList<AudioDeviceOption> Enumerate(AudioDeviceDirection direction, bool includeHidden)
    {
        using var enumerator = new MMDeviceEnumerator();
        var flow = direction == AudioDeviceDirection.Output ? DataFlow.Render : DataFlow.Capture;
        var defaultId = GetDefaultId(enumerator, flow, Role.Console);
        using var devices = enumerator.EnumerateAudioEndPoints(flow, includeHidden ? DeviceState.All : DeviceState.Active);
        var options = new List<AudioDeviceOption>();
        for (var index = 0; index < devices.Count; index++)
        {
            try
            {
                using var device = devices[index];
                var id = device.ID;
                if (!includeHidden && !IsAllowed(direction, id)) continue;
                var suffix = direction == AudioDeviceDirection.Output ? "e6327cad-dcec-4949-ae8a-991e976a79d2" : "2eef81be-33fa-4800-9670-1cd474972c3f";
                var name = device.FriendlyName;
                var containerId = SettingsManager.Current.Experiments.BluetoothBattery ? ReadContainerId(device) : null;
                options.Add(new AudioDeviceOption(@"\\?\SWD#MMDEVAPI#" + id + "#{" + suffix + "}",
                    id, string.IsNullOrWhiteSpace(name) ? id : name, string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase))
                { Direction = direction, IsActive = device.State == DeviceState.Active,
                    ContainerId = containerId,
                    Availability = device.State switch
                    {
                        DeviceState.Active => AudioDeviceAvailability.Active,
                        DeviceState.Disabled => AudioDeviceAvailability.Disabled,
                        DeviceState.Unplugged => AudioDeviceAvailability.Unplugged,
                        _ => AudioDeviceAvailability.Unavailable
                    } });
            }
            catch (COMException) { }
        }
        if (!_observedOrder.TryGetValue(direction, out var observed)) _observedOrder[direction] = observed = [];
        foreach (var option in options.OrderBy(device => device.SystemName, StringComparer.CurrentCultureIgnoreCase))
            if (!observed.Contains(option.PolicyId, StringComparer.OrdinalIgnoreCase)) observed.Add(option.PolicyId);
        var preferences = Volatile.Read(ref _settings).Preferences.Where(entry => entry.Direction == direction).ToArray();
        if (options.Any(device => !preferences.Any(entry => entry.EndpointId.Equals(device.PolicyId, StringComparison.OrdinalIgnoreCase))))
        {
            var discovered = options.OrderBy(device => observed.IndexOf(device.PolicyId)).ToArray();
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                var settings = SettingsManager.Current.AudioDevices;
                var entries = settings.Preferences.ToList();
                foreach (var device in discovered)
                    if (!entries.Any(entry => entry.Direction == direction && entry.EndpointId.Equals(device.PolicyId, StringComparison.OrdinalIgnoreCase)))
                        entries.Add(new(direction, device.PolicyId, device.SystemName));
                if (entries.Count != settings.Preferences.Count) SettingsManager.Current.AudioDevices = settings with { Preferences = entries.ToArray() };
            });
        }
        return options.Select(device =>
        {
            var entry = preferences.FirstOrDefault(item => item.EndpointId.Equals(device.PolicyId, StringComparison.OrdinalIgnoreCase));
            return device with { DisplayName = string.IsNullOrWhiteSpace(entry?.Alias) ? device.SystemName : entry.Alias };
        }).OrderBy(device => { var index = Array.FindIndex(preferences, entry => entry.EndpointId.Equals(device.PolicyId, StringComparison.OrdinalIgnoreCase)); return index < 0 ? preferences.Length + observed.IndexOf(device.PolicyId) : index; })
            .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(device => device.PolicyId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Guid? ReadContainerId(MMDevice device)
    {
        try
        {
            return device.Properties.TryGetValue<Guid>(new PropertyKey(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2), out var id) ? AudioDeviceIdentity.PhysicalContainer(id) : null;
        }
        catch (COMException) { return null; }
    }

    public Task<AudioDeviceOperationResult> SetDefaultAsync(AudioDeviceOption device, CancellationToken token = default,
        Func<bool>? isCurrent = null) => _platform.RunAsync(() =>
    {
        if (isCurrent?.Invoke() == false) return new AudioDeviceOperationResult(AudioDeviceOperationStatus.Cancelled);
        if (!IsAllowed(device.Direction, device.PolicyId)) return new AudioDeviceOperationResult(AudioDeviceOperationStatus.Unavailable);
        using var enumerator = new MMDeviceEnumerator();
        using var endpoint = enumerator.GetDevice(device.PolicyId);
        var flow = device.Direction == AudioDeviceDirection.Output ? DataFlow.Render : DataFlow.Capture;
        if (endpoint.State != DeviceState.Active || endpoint.DataFlow != flow)
            return new AudioDeviceOperationResult(AudioDeviceOperationStatus.Unavailable);
        var operationPolicyVersion = PolicyVersion;
        var settings = Volatile.Read(ref _settings);
        var communications = device.Direction == AudioDeviceDirection.Output ? settings.SwitchOutputCommunications : settings.SwitchInputCommunications;
        var roles = communications ? new[] { Role.Console, Role.Multimedia, Role.Communications } : new[] { Role.Console, Role.Multimedia };
        if (roles.All(role => string.Equals(GetDefaultId(enumerator, flow, role), device.PolicyId, StringComparison.OrdinalIgnoreCase)))
            return new AudioDeviceOperationResult(AudioDeviceOperationStatus.Unchanged);
        token.ThrowIfCancellationRequested();
        if (isCurrent?.Invoke() == false || !IsAllowed(device.Direction, device.PolicyId))
            return new AudioDeviceOperationResult(AudioDeviceOperationStatus.Cancelled);
        try
        {
            AudioPolicyConfig.SetDefaultDevice(device.PolicyId, communications,
                () => operationPolicyVersion == PolicyVersion && !token.IsCancellationRequested && isCurrent?.Invoke() != false && IsAllowed(device.Direction, device.PolicyId));
            _platform.Invalidate();
            return new AudioDeviceOperationResult(AudioDeviceOperationStatus.Applied);
        }
        catch (Exception exception)
        {
            var matches = roles.Count(role => string.Equals(GetDefaultId(enumerator, flow, role), device.PolicyId, StringComparison.OrdinalIgnoreCase));
            _platform.Invalidate();
            return new AudioDeviceOperationResult(matches > 0 ? AudioDeviceOperationStatus.Partial :
                exception is OperationCanceledException ? AudioDeviceOperationStatus.Cancelled : AudioDeviceOperationStatus.Failed, exception.Message);
        }
    }, token);

    public Task<string> GetDefaultDescriptionAsync(AudioDeviceDirection direction) => _platform.RunAsync(() =>
    {
        using var enumerator = new MMDeviceEnumerator();
        var id = GetDefaultId(enumerator, direction == AudioDeviceDirection.Output ? DataFlow.Render : DataFlow.Capture, Role.Console);
        if (id is null) return Translations.Get("Audio.Device.Unavailable");
        if (!IsAllowed(direction, id)) return Translations.Get("Audio.Device.Hidden");
        using var device = enumerator.GetDevice(id);
        var preference = Volatile.Read(ref _settings).FindPreference(direction, id);
        var name = !string.IsNullOrWhiteSpace(preference?.Alias)
            ? preference.Alias : device.FriendlyName;

        return name;
    });

    private static string? GetDefaultId(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        if (!enumerator.HasDefaultAudioEndpoint(flow, role)) return null;
        using var device = enumerator.GetDefaultAudioEndpoint(flow, role);
        return device.ID;
    }

    public void Dispose() { if (_disposed) return; _disposed = true; SettingsManager.SettingsChanged -= OnSettingsChanged;  }
}
