using AFMediaBar.Classes.Interop;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 集中定义任务栏宿主应暂停环境敏感布局和吞掉的窗口消息。
/// Centralizes messages that taskbar hosts use to pause environment-sensitive layout or stop propagation.
/// </summary>
public static class TaskbarHostMessagePolicy
{
    /// <summary>判断消息是否表示 DPI 或显示环境正在变化。/ Determines whether a message represents a DPI or display-environment change.</summary>
    public static bool IsEnvironmentChange(int message) =>
        message is NativeMethods.WM_DPICHANGED or NativeMethods.WM_DPICHANGED_AFTERPARENT or NativeMethods.WM_DISPLAYCHANGE;

    /// <summary>判断消息是否应由任务栏宿主拦截。/ Determines whether a message should be intercepted by the taskbar host.</summary>
    public static bool ShouldSuppressPropagation(int message) => message is
        NativeMethods.WM_GETOBJECT or NativeMethods.WM_SHOWWINDOW or NativeMethods.WM_WINDOWPOSCHANGING or
        NativeMethods.WM_NCCALCSIZE or NativeMethods.WM_IME_SETCONTEXT or NativeMethods.WM_IME_NOTIFY;
}
