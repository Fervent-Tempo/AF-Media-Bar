// Resolves horizontal presentation anchors without changing text, icons or stored component order.
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>横向任务栏组件和悬停裁剪的锚点策略，不拥有窗口或设置。</summary>
public static class TaskbarArrangementPolicy
{
    /// <summary>自动排布跟随任务栏位置；无效设置回退自动。</summary>
    public static TaskbarArrangement Resolve(TaskbarArrangement arrangement, TaskbarBarPosition position) =>
        Enum.IsDefined(arrangement) && arrangement != TaskbarArrangement.Automatic
            ? arrangement
            : position switch
            {
                TaskbarBarPosition.End => TaskbarArrangement.Right,
                TaskbarBarPosition.Center => TaskbarArrangement.Center,
                _ => TaskbarArrangement.Left
            };

    /// <summary>计算按钮组在文字区中的左缘；居中以整条媒体栏为基准，受文字区边界限制。</summary>
    public static double ActionsLeft(double barWidth, double textLeft, double textWidth,
        double actionsWidth, TaskbarArrangement arrangement)
    {
        var minimum = Math.Min(5, Math.Max(0, textWidth));
        var maximum = Math.Max(minimum, textWidth - 6 - Math.Max(0, actionsWidth));
        var desired = arrangement switch
        {
            TaskbarArrangement.Right => maximum,
            TaskbarArrangement.Center => (barWidth - actionsWidth) / 2 - textLeft,
            _ => minimum
        };
        return Math.Clamp(desired, minimum, maximum);
    }

    /// <summary>按锚点计算悬停揭示矩形的左缘；收起与展开使用同一基准。</summary>
    public static double RevealLeft(double width, double revealedWidth, TaskbarArrangement arrangement) =>
        arrangement switch
        {
            TaskbarArrangement.Right => Math.Max(0, width - revealedWidth),
            TaskbarArrangement.Center => Math.Max(0, (width - revealedWidth) / 2),
            _ => 0
        };
}
