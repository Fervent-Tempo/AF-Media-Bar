namespace AFMediaBar.Classes.Models.Layout;

/// <summary>
/// 媒体栏内容尺寸请求：由媒体控件提出，由任务栏或灵动岛宿主执行过渡。
/// Media-bar content size request: raised by the media control and applied by its host.
/// </summary>
/// <param name="IsForcedRefresh">
/// 该请求是否绕过了内容指纹去重。宿主明确刷新布局状态时置为 true，避免宿主尚未就绪期间被丢弃的请求把后续权威刷新去重掉。
/// Whether the request bypassed the content-fingerprint dedupe. The host sets it when it explicitly refreshes its layout
/// state so an earlier request dropped while the host was not ready cannot dedupe away this authoritative refresh.
/// </param>
/// <param name="SkipTransition">
/// 该请求是否必须立即生效。歌词换行是离散内容切换：宿主的长度过渡动画期间新句会按旧宽度渲染、右端被省略号截断，
/// 因此这类请求跳过过渡直接落到目标长度；常规请求保持动画。
/// Whether the request must land immediately. A lyric line change is a discrete content switch: while the host animates the
/// length the new line renders at the previous width and gets clipped with an ellipsis, so these requests skip the transition;
/// regular requests keep it.
/// </param>
public sealed record MediaBarSizeRequest(
    LayoutOrientation Orientation,
    double Width,
    double Height,
    string ContentFingerprint,
    bool IsForcedRefresh,
    bool SkipTransition = false)
{
    /// <summary>获取当前布局主轴目标尺寸。/ Gets the target size along the layout primary axis.</summary>
    public double PrimaryLength => Orientation == LayoutOrientation.Horizontal ? Width : Height;
}

/// <summary>媒体栏尺寸请求事件参数。/ Event arguments for a media-bar size request.</summary>
public sealed class MediaBarSizeRequestEventArgs(MediaBarSizeRequest request) : EventArgs
{
    public MediaBarSizeRequest Request { get; } = request;
}
