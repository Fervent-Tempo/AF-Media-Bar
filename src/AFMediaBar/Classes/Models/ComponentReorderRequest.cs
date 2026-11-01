// 栏上排序的一次输入意图；携带起始快照，拒绝状态变化后的旧操作。
using AFMediaBar.Classes.Settings;
namespace AFMediaBar.Classes.Models;

public sealed record ComponentReorderRequest(BarLayoutState State, ComponentKey Key,
    int TargetIndex, IReadOnlyList<ComponentLayoutEntry> OriginalEntries)
{
    public TaskbarRestComponent Component => Key.Component;
}
