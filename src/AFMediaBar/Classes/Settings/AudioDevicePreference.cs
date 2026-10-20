// 设备偏好绑定端点与方向，不用名称推断身份；列表顺序是用户顺序。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Settings;

/// <summary>端点失联后仍保留的展示偏好。</summary>
public sealed record AudioDevicePreference(AudioDeviceDirection Direction, string EndpointId, string LastKnownName)
{
    public string Alias { get; init; } = string.Empty;
}
