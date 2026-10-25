// 三种呈现层的有序组件偏好；null 沿用旧配置，空列表表示没有可选组件。
namespace AFMediaBar.Classes.Settings;

/// <summary>组件所属布局；悬停控制与当前常驻层可以同时呈现。</summary>
public enum BarLayoutState { Resident, Idle, Hover }

/// <summary>稳定组件标识和显隐；列表位置即逻辑顺序。</summary>
public sealed record ComponentLayoutEntry(TaskbarRestComponent Component, bool IsVisible);

/// <summary>任务栏与悬浮栏共用的三套独立布局。</summary>
public sealed record ComponentLayoutsSettings
{
    public IReadOnlyList<ComponentLayoutEntry>? Resident { get; init; }
    public IReadOnlyList<ComponentLayoutEntry>? Idle { get; init; }
    public IReadOnlyList<ComponentLayoutEntry>? Hover { get; init; }
    public IReadOnlyList<ComponentLayoutEntry>? Get(BarLayoutState state) => state switch
    { BarLayoutState.Idle => Idle, BarLayoutState.Hover => Hover, _ => Resident };
    public ComponentLayoutsSettings With(BarLayoutState state, IReadOnlyList<ComponentLayoutEntry> entries) => state switch
    {
        BarLayoutState.Idle => this with { Idle = entries.ToArray() },
        BarLayoutState.Hover => this with { Hover = entries.ToArray() },
        _ => this with { Resident = entries.ToArray() }
    };
    public ComponentLayoutsSettings Normalize() => new()
    { Resident = Copy(Resident), Idle = Copy(Idle), Hover = Copy(Hover) };
    private static ComponentLayoutEntry[]? Copy(IReadOnlyList<ComponentLayoutEntry>? entries) => entries?
        .Where(entry => entry is not null && Enum.IsDefined(entry.Component) && entry.Component != TaskbarRestComponent.DragHandle)
        .DistinctBy(entry => entry.Component).Select(entry => entry with { }).ToArray();
}
