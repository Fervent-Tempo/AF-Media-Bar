using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>
/// 在后台探测 Windows 任务栏占用区域的基础设施边界。
/// Infrastructure boundary that probes Windows taskbar occupancy in the background.
/// </summary>
public interface ITaskbarOccupiedAreaProbe
{
    /// <summary>
    /// 启动一次异步平台探测，并在后台线程结束时调用一个完成回调。
    /// Starts one asynchronous platform probe and invokes exactly one completion callback when its background thread exits.
    /// </summary>
    /// <param name="taskbarHandle">任务栏窗口句柄 / Taskbar window handle.</param>
    /// <param name="taskbarRect">任务栏屏幕矩形 / Taskbar screen rectangle.</param>
    /// <param name="orientation">布局主轴方向 / Layout primary-axis orientation.</param>
    /// <param name="dpiScale">任务栏 DPI 缩放 / Taskbar DPI scale.</param>
    /// <param name="edgePaddingPixels">媒体栏边缘留白 / Media-bar edge padding.</param>
    /// <param name="succeeded">成功时接收安全主轴区间的回调 / Callback receiving safe primary-axis ranges on success.</param>
    /// <param name="failed">线程启动或探测失败时的回调 / Callback invoked when thread startup or probing fails.</param>
    void Start(
        IntPtr taskbarHandle,
        NativeMethods.RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels,
        Action<IReadOnlyList<TaskbarPrimaryRange>> succeeded,
        Action failed);
}
