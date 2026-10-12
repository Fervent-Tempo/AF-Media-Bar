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
        if (args.PropertyName is not (null or nameof(AppSettings.AudioDevices) or nameof(AppSettings.Interaction))) return;
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
                options.Add(new AudioDeviceOption(@"\\?\SWD#MMDEVAPI#" + id + "#{" + suffix + "}",
                    id, string.IsNullOrWhiteSpace(name) ? id : name, string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase))
                { Direction = direction, IsActive = device.State == DeviceState.Active });
            }
            catch (COMException) { }
        }
        return options.OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(device => device.PolicyId, StringComparer.OrdinalIgnoreCase).ToArray();
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
                () => !token.IsCancellationRequested && isCurrent?.Invoke() != false && IsAllowed(device.Direction, device.PolicyId));
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
        return device.FriendlyName;
    });

    private static string? GetDefaultId(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        if (!enumerator.HasDefaultAudioEndpoint(flow, role)) return null;
        using var device = enumerator.GetDefaultAudioEndpoint(flow, role);
        return device.ID;
    }

    public void Dispose() => SettingsManager.SettingsChanged -= OnSettingsChanged;
}
