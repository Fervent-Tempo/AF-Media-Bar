// 保存实验开关及独立悬浮配置；不包含宿主句柄或全局服务。
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Settings;

/// <summary>缺字段时保持关闭的实验项目。</summary>
public sealed record ExperimentalSettings
{
    public bool NewAudioControl { get; init; }
    public bool VerticalTaskbar { get; init; }
    public bool FloatingBar { get; init; }
}

/// <summary>配置所属宿主，横竖方向共享同一份配置。</summary>
public enum BarConfigurationTarget { Taskbar, FloatingBar }

/// <summary>悬浮窗口的桌面层级。</summary>
public enum FloatingBarLayer { Desktop, Topmost }

/// <summary>以显示器工作区为基准保存的中心位置，单位为 DIP。</summary>
public sealed record FloatingBarPosition(string MonitorDeviceId, double CenterXDip, double CenterYDip);

/// <summary>悬浮宿主偏好；Profile 仅保存几何，首次从任务栏几何初始化。</summary>
public sealed record FloatingBarSettings
{
    public BarDisplaySettings? Profile { get; init; }
    public LayoutOrientation Orientation { get; init; }
    public FloatingBarLayer Layer { get; init; }
    public IReadOnlyList<string> MonitorDeviceIds { get; init; } = [];
    public IReadOnlyList<FloatingBarPosition> Positions { get; init; } = [];

    public FloatingBarSettings Normalize() => this with
    {
        Orientation = Enum.IsDefined(Orientation) ? Orientation : LayoutOrientation.Horizontal,
        Layer = Enum.IsDefined(Layer) ? Layer : FloatingBarLayer.Desktop,
        MonitorDeviceIds = (MonitorDeviceIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        Positions = (Positions ?? []).Where(position => position is not null &&
            !string.IsNullOrWhiteSpace(position.MonitorDeviceId) && double.IsFinite(position.CenterXDip) &&
            double.IsFinite(position.CenterYDip)).GroupBy(position => position.MonitorDeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last()).ToArray(),
        Profile = Profile?.Normalize()
    };
}
