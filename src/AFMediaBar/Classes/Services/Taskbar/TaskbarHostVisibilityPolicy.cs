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
    /// 任务栏移动或稳定收起时，宿主 MUST 自己隐藏窗口：跨进程子窗口不会与 Shell 的合成动画保持逐帧同步，
    /// 在收起期间继续显示会留角，在展开的第一帧就恢复又会把隐藏位置的旧画面卡到屏幕边缘。只有可见任务栏的矩形重新稳定后，
    /// 才能按静置层判据显示。
    /// Decides whether the host window should be shown or hidden this time.
    ///
    /// While the taskbar is moving or settled at the hidden edge, the host MUST hide its own window: a cross-process child does not stay in
    /// frame-by-frame lockstep with the Shell composition animation. Keeping it visible during the hide leaves a corner behind, while restoring it
    /// on the first reveal frame paints the old hidden position at the screen edge. The rest-layer verdict is applied only after the visible taskbar
    /// rectangle has settled again.
    /// </summary>
    /// <param name="motion">当前任务栏运动状态。/ Current taskbar motion state.</param>
    /// <param name="restLayerEmpty">静置层是否一个组件都不显示（控件算出的结论）。/ Whether the rest layer shows no component at all, as the control computed it.</param>
    public static TaskbarHostVisibility Resolve(in TaskbarMotionState motion, bool restLayerEmpty)
    {
        if (motion.IsMoving || motion.IsHidden)
        {
            return TaskbarHostVisibility.Collapsed;
        }

        return restLayerEmpty ? TaskbarHostVisibility.Collapsed : TaskbarHostVisibility.Visible;
    }
}
