// 保存实验开关及悬浮宿主几何；不包含宿主句柄或全局服务。
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Settings;

/// <summary>缺字段时保持关闭的实验项目。</summary>
public sealed record ExperimentalSettings
{
    public bool MemoryTools { get; init; }
    public bool SystemMemoryCleanup { get; init; }
    public bool BluetoothBattery { get; init; }
    // v1.5.0 已发布的兼容字段；正式功能不再读取它们作为门禁。
    public bool VerticalTaskbar { get; init; }
    public bool FloatingBar { get; init; }

    public ExperimentalSettings Normalize() => this;
}

/// <summary>几何配置所属宿主，横竖方向不产生额外副本。</summary>
public enum BarConfigurationTarget { Taskbar, FloatingBar }

/// <summary>悬浮窗口的桌面层级。</summary>
public enum FloatingBarLayer { Desktop, Topmost }

/// <summary>以显示器工作区为基准保存的中心位置，单位为 DIP。</summary>
public sealed record FloatingBarPosition(string MonitorDeviceId, double CenterXDip, double CenterYDip)
{
    public FloatingBarDockEdge DockEdge { get; init; }
}

/// <summary>正常位置所附着的工作区边缘；折叠中的屏幕外坐标不保存。</summary>
public enum FloatingBarDockEdge { None, Left, Right, Top, Bottom }

/// <summary>悬浮宿主偏好；Profile 仅保存几何，首次从任务栏几何初始化。</summary>
public sealed record FloatingBarSettings
{
    public BarDisplaySettings? Profile { get; init; }
    public LayoutOrientation Orientation { get; init; }
    public FloatingBarLayer Layer { get; init; }
    public bool EdgeCollapseEnabled { get; init; } = true;

    /// <summary>悬浮条压在任务栏上方；开启后不再受窗口层级限制，任务栏抢回前面时也会重新压住。</summary>
    public bool OverlayTaskbar { get; init; }
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
            .Select(group => group.Last() with { DockEdge = Enum.IsDefined(group.Last().DockEdge) ? group.Last().DockEdge : FloatingBarDockEdge.None }).ToArray(),
        Profile = Profile?.Normalize()
    };
}
