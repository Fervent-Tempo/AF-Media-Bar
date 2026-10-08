namespace AFMediaBar.Classes.Services;

/// <summary>任务栏宿主窗口应当的可见性。/ Visibility the taskbar host window should have.</summary>
public enum TaskbarHostVisibility
{
    /// <summary>显示窗口。/ The window is shown.</summary>
    Visible = 0,

    /// <summary>隐藏窗口。/ The window is hidden.</summary>
    Collapsed = 1
}

/// <summary>
/// 任务栏宿主窗口显隐的纯策略：把任务栏运动状态与"静置层是否为空"合成一个结论。
/// Pure policy for the taskbar host window's visibility: it folds the taskbar's motion state and "is the rest layer empty" into one verdict.
/// </summary>
public static class TaskbarHostVisibilityPolicy
{
    /// <summary>
    /// 解析宿主窗口这次应当显示还是隐藏。
    ///
    /// 运动期间只有可复用的稳定几何才允许随父任务栏移动；没有几何时隐藏至定位完成。
    /// 稳定收起必须由宿主自己隐藏，不能依赖父窗口把子窗口带出屏幕。
    /// </summary>
    /// <param name="motion">当前任务栏运动状态。/ Current taskbar motion state.</param>
    /// <param name="restLayerEmpty">静置层是否一个组件都不显示（控件算出的结论）。/ Whether the rest layer shows no component at all, as the control computed it.</param>
    /// <param name="hasReusablePlacement">是否可复用同一任务栏的稳定定位。/ Whether the same taskbar's stable placement can be reused.</param>
    public static TaskbarHostVisibility Resolve(in TaskbarMotionState motion, bool restLayerEmpty, bool hasReusablePlacement = false)
    {
        if (restLayerEmpty)
        {
            return TaskbarHostVisibility.Collapsed;
        }

        if (motion.IsMoving)
            return hasReusablePlacement ? TaskbarHostVisibility.Visible : TaskbarHostVisibility.Collapsed;

        return motion.IsHidden ? TaskbarHostVisibility.Collapsed : TaskbarHostVisibility.Visible;
    }
}
