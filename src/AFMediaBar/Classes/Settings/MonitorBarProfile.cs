// 保存单个模式和逻辑显示器的组件布局及正常位置，不包含运行时窗口或避让结果。
namespace AFMediaBar.Classes.Settings;

/// <summary>独立组件的正常位置；任务栏为主轴/横轴起点，悬浮为相对工作区的中心，单位 DIP。</summary>
public sealed record DetachedComponentPosition(TaskbarRestComponent Component, double PrimaryDip, double CrossDip);

/// <summary>每屏常驻/无媒体内容与摆放；悬停内容和组件参数仍由应用共用。</summary>
public sealed record MonitorBarProfile(BarConfigurationTarget Target, string MonitorDeviceId)
{
    public IReadOnlyList<ComponentLayoutEntry> Resident { get; init; } = [];
    public IReadOnlyList<ComponentLayoutEntry> Idle { get; init; } = [];
    public IReadOnlyList<DetachedComponentPosition> Detached { get; init; } = [];
    public double? BodyPrimaryDip { get; init; }
    public TaskbarBarPosition? BodyAlignment { get; init; }

    public MonitorBarProfile Normalize() => this with
    {
        MonitorDeviceId = MonitorDeviceId.Trim(),
        Resident = Copy(Resident),
        Idle = Copy(Idle),
        Detached = (Detached ?? []).Where(position => position is not null && Enum.IsDefined(position.Component) &&
            position.Component is not (TaskbarRestComponent.Artwork or TaskbarRestComponent.MediaText or TaskbarRestComponent.DragHandle) &&
            double.IsFinite(position.PrimaryDip) && double.IsFinite(position.CrossDip))
            .DistinctBy(position => position.Component).Select(position => position with { }).ToArray(),
        BodyPrimaryDip = BodyPrimaryDip is { } value && double.IsFinite(value) ? value : null,
        BodyAlignment = BodyAlignment is { } alignment && Enum.IsDefined(alignment) ? alignment : null
    };

    private static ComponentLayoutEntry[] Copy(IReadOnlyList<ComponentLayoutEntry>? entries) => (entries ?? [])
        .Where(entry => entry is not null && Enum.IsDefined(entry.Component) && entry.Component != TaskbarRestComponent.DragHandle)
        .DistinctBy(entry => entry.Component).Select(entry => entry with { }).ToArray();
}
