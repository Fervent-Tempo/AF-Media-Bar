// 设备与路由的不可变 UI 契约，不持有原生资源。
namespace AFMediaBar.Classes.Models;

/// <summary>Windows 音频端点方向。</summary>
public enum AudioDeviceDirection { Output = 0, Input = 1 }

/// <summary>端点当前可用状态，不参与设置持久化。</summary>
public enum AudioDeviceAvailability { Active, Disabled, Unplugged, Unavailable }

/// <summary>可用的 Windows 音频设备；Id 保留现有输出接口标识。</summary>
public sealed record AudioDeviceOption(string Id, string PolicyId, string DisplayName, bool IsDefault)
{
    public Guid? ContainerId { get; init; }
    public string MenuLabel => DisplayName;
    public string DeviceHint => DisplayName;
    public string SystemName { get; init; } = DisplayName;
    public AudioDeviceDirection Direction { get; init; }
    public bool IsActive { get; init; } = true;
    public AudioDeviceAvailability Availability { get; init; } = AudioDeviceAvailability.Active;
    public bool IsSystemDefaultChoice { get; init; }
}

/// <summary>会话进程的可验证身份，创建时间用于防止 PID 复用。</summary>
public sealed record AudioProcessIdentity(int ProcessId, long StartedAtUtcTicks, string ExecutablePath, string? PackageIdentity);

/// <summary>有播放或录音会话的应用。</summary>
public sealed record ApplicationAudioSnapshot(string Identity, string ProcessName, string DisplayName,
    IReadOnlyList<AudioProcessIdentity> Processes, bool HasOutputSessions, bool HasInputSessions);

/// <summary>设备操作目标；应用为空时表示系统默认设备。</summary>
public sealed record AudioDeviceTarget(AudioDeviceDirection Direction, ApplicationAudioSnapshot? Application = null)
{
    public string Key => $"{Direction}:{Application?.Identity ?? "system"}";
}

/// <summary>分别表达未变化、失败和部分成功，避免把接口成功误报为音频流迁移。</summary>
public enum AudioDeviceOperationStatus { Applied, Unchanged, Unavailable, Unsupported, Failed, Partial, Cancelled }

/// <summary>设备操作与路由读取结果。</summary>
public sealed record AudioDeviceOperationResult(AudioDeviceOperationStatus Status, string? Message = null);

/// <summary>应用持久化路由状态；不表示当前会话已经换设备。</summary>
public sealed record ApplicationAudioRoute(string? EndpointId, bool IsMixed, bool IsSupported, string? Error = null);
