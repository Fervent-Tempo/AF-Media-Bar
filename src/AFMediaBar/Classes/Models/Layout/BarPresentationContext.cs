// 描述宿主与朝向能力，不保存窗口、设置或平台资源。
namespace AFMediaBar.Classes.Models.Layout;

/// <summary>任务栏与独立悬浮条共用的呈现上下文。</summary>
public readonly record struct BarPresentationContext(WindowMode Host, LayoutOrientation Orientation)
{
    public bool SupportsBarInteractions => Host is WindowMode.Taskbar or WindowMode.FloatingBar;
    public bool IsVertical => Orientation == LayoutOrientation.Vertical;
}
