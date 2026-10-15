// 独立保存悬浮工作区预算，避免覆盖任务栏图标避让预算。
namespace AFMediaBar.Classes.Services;

/// <summary>复用长度聚合策略，为悬浮编辑器提供独立的宿主范围。</summary>
public sealed class FloatingBarLengthConstraints
{
    public TaskbarLengthConstraintsService Range { get; } = new();
}
