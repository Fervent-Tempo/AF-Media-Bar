namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// WebView2 WPF 合成控件的图形层故障识别与重建预算。
///
/// WebView2 的 WPF 合成控件在 D3D 设备不可用时会在"尺寸变化"里空引用崩溃
/// （`Direct3DHelper.CreateD3D11Texture` ← `GraphicsItemD3DImage.SetGraphicItem`），真机日志里出现过两次整应用闪退。
/// 这是 WebView2 内部没有防护的缺陷，应用层能做的只有：识别这一类故障、拦下它、重建歌词视图；
/// 重建预算用完后退回元数据显示，保证应用不再被同一个缺陷反复击穿。
/// Graphics-fault identification and rebuild budget for the WebView2 WPF composition control.
///
/// When its D3D device is unavailable the control dereferences null while resizing (`Direct3DHelper.CreateD3D11Texture`
/// from `GraphicsItemD3DImage.SetGraphicItem`), which crashed the whole application twice in the field log. It is an
/// unguarded WebView2 defect, so the application can only identify the fault, swallow it, and rebuild the lyrics view;
/// once the rebuild budget is spent the bar falls back to the metadata display so the same defect cannot keep taking the
/// whole app down.
/// </summary>
public static class WebView2GraphicsFaultPolicy
{
    /// <summary>同一进程内允许的歌词视图重建次数；超过后退回元数据，不再触发同一故障。
    /// Number of lyric-view rebuilds allowed per process; past it the bar falls back to metadata and stops hitting the fault.</summary>
    public const int MaximumRecoveries = 3;

    private const string WebView2WpfFrame = "Microsoft.Web.WebView2.Wpf";

    /// <summary>
    /// 判断一次未处理异常是否为"WebView2 图形层在尺寸变化中空引用"这一类可拦截故障。
    /// Whether an unhandled exception is the interceptable "WebView2 graphics layer dereferenced null while resizing" fault.
    /// </summary>
    /// <param name="exception">待判定的异常 / The exception to classify.</param>
    public static bool IsGraphicsFault(Exception? exception) =>
        exception is NullReferenceException && IsGraphicsFault(exception.StackTrace);

    /// <summary>按堆栈文本判定；拆出来便于单测覆盖真实堆栈形状。/ Stack-text overload, split out so tests can use a real-shaped stack.</summary>
    /// <param name="stackTrace">异常堆栈文本 / The exception stack text.</param>
    public static bool IsGraphicsFault(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace) || !stackTrace.Contains(WebView2WpfFrame, StringComparison.Ordinal))
        {
            return false;
        }

        return stackTrace.Contains("CreateD3D11Texture", StringComparison.Ordinal) ||
               stackTrace.Contains("GraphicsItemD3DImage", StringComparison.Ordinal);
    }

    /// <summary>重建预算是否还允许再来一次。/ Whether the rebuild budget allows one more attempt.</summary>
    /// <param name="recoveriesAlreadyDone">已经重建过的次数 / Rebuilds already performed.</param>
    public static bool CanRecover(int recoveriesAlreadyDone) => recoveriesAlreadyDone < MaximumRecoveries;
}
