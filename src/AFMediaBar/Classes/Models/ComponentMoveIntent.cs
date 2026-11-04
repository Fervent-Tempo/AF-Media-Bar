// 描述一次组件拖动的呈现意图；屏幕坐标为物理像素，平台宿主计算安全落点。
using System.Windows;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Models;

/// <summary>拖动预览或结束；取消不改变配置，合并时保留来源层的插入索引。</summary>
public sealed class ComponentMoveIntentEventArgs(TaskbarRestComponent component, Rect screenBounds,
    BarLayoutState state, IReadOnlyList<ComponentLayoutEntry> originalEntries, int targetIndex,
    bool dock, bool finished, bool cancelled = false) : EventArgs
{
    public TaskbarRestComponent Component { get; } = component;
    public Rect ScreenBounds { get; } = screenBounds;
    public BarLayoutState State { get; } = state;
    public IReadOnlyList<ComponentLayoutEntry> OriginalEntries { get; } = originalEntries;
    public int TargetIndex { get; } = targetIndex;
    public bool Dock { get; } = dock;
    public bool CanDock { get; set; } = dock;
    public bool Finished { get; } = finished;
    public bool Cancelled { get; } = cancelled;
}

/// <summary>宿主完成坐标和生命周期验证后交由视图模型提交的摆放意图。</summary>
public sealed record ComponentPlacementRequest(BarConfigurationTarget Target, string MonitorDeviceId,
    TaskbarRestComponent Component, DetachedComponentPosition? Position, BarLayoutState State,
    IReadOnlyList<ComponentLayoutEntry> OriginalEntries, int TargetIndex);

/// <summary>独立组件请求原有上下文菜单，位置使用屏幕物理像素。</summary>
public sealed class ComponentContextMenuRequestEventArgs(TaskbarRestComponent component, Point position) : EventArgs
{
    public TaskbarRestComponent Component { get; } = component;
    public Point Position { get; } = position;
}
