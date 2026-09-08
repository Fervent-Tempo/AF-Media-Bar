namespace AFMediaBar.Classes.Models.Layout;

/// <summary>
/// 媒体栏内容尺寸请求：由媒体控件提出，由任务栏或灵动岛宿主执行过渡。
/// Media-bar content size request: raised by the media control and applied by its host.
/// </summary>
public sealed record MediaBarSizeRequest(
    LayoutOrientation Orientation,
    double Width,
    double Height,
    string ContentFingerprint,
    bool IsResetToPreset)
{
    /// <summary>获取当前布局主轴目标尺寸。/ Gets the target size along the layout primary axis.</summary>
    public double PrimaryLength => Orientation == LayoutOrientation.Horizontal ? Width : Height;
}

/// <summary>媒体栏尺寸请求事件参数。/ Event arguments for a media-bar size request.</summary>
public sealed class MediaBarSizeRequestEventArgs(MediaBarSizeRequest request) : EventArgs
{
    public MediaBarSizeRequest Request { get; } = request;
}
