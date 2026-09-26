namespace AFMediaBar.Components;

/// <summary>
/// "WebView2 图形层故障，重建歌词视图"的进程内广播。
///
/// 触发方是应用的全局未处理异常处理（见 <c>App.OnDispatcherUnhandledException</c>）：WebView2 合成控件的
/// 图形层空引用发生在 WPF 布局内部，应用层没有调用点可以包住它，只能在全局拦下后再通知持有歌词视图的媒体控件重建。
/// In-process broadcast for "WebView2 graphics-layer fault: rebuild the lyrics view".
///
/// The trigger is the application's global unhandled-exception handler (see <c>App.OnDispatcherUnhandledException</c>):
/// the composition control's graphics fault is thrown inside WPF layout, so there is no application call site to wrap it;
/// the app swallows it globally and then asks the media controls that own a lyrics view to rebuild.
/// </summary>
internal static class WebLyricsGraphicsRecovery
{
    /// <summary>媒体控件订阅；每个持有 Web 歌词视图的控件各接一次。/ Subscribed by media controls; one subscription per control that owns a web lyrics view.</summary>
    public static event Action? RecoveryRequested;

    /// <summary>请求一次重建；在没有订阅者时是空操作。/ Requests one rebuild; a no-op without subscribers.</summary>
    public static void Request() => RecoveryRequested?.Invoke();
}
