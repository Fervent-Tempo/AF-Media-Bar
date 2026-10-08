// Decides whether the taskbar lyrics view needs a browser; the owning control performs all UI and WebView2 operations.
// A short grace period avoids rebuilding the browser for every track change while still releasing it during idle time.
namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>Action for the owner of a WebView2 lyrics control. / WebView2 歌词控件所有者应执行的动作。</summary>
public enum LyricsWebViewLifetimeAction
{
    /// <summary>No browser exists and none is needed. / 没有也不需要浏览器。</summary>
    None,
    /// <summary>Create a browser for available lyrics. / 为已有歌词创建浏览器。</summary>
    Create,
    /// <summary>Keep the browser for active lyrics. / 保留浏览器显示歌词。</summary>
    Retain,
    /// <summary>Release the browser after the no-lyrics grace period. / 无歌词宽限期后释放。</summary>
    ReleaseAfterGrace,
    /// <summary>Release immediately because the host or feature is disabled. / 宿主或功能关闭，立即释放。</summary>
    ReleaseNow
}

/// <summary>Pure lifetime decisions for WebView2 lyrics. / WebView2 歌词视图的纯生命周期决策。</summary>
public static class LyricsWebViewLifetimePolicy
{
    /// <summary>How long an existing browser survives a track without lyrics. / 无歌词曲目期间保留已有浏览器的时间。</summary>
    public static readonly TimeSpan NoLyricsGracePeriod = TimeSpan.FromSeconds(30);

    /// <summary>Resolve the next ownership action from current presentation state. / 根据当前呈现状态决定所有权动作。</summary>
    public static LyricsWebViewLifetimeAction Resolve(
        bool hostLoaded,
        bool lyricsEnabled,
        bool graphicsDisabled,
        bool mediaConnected,
        int lyricLineCount,
        bool hasRenderer)
    {
        if (!hostLoaded || !lyricsEnabled || graphicsDisabled)
            return hasRenderer ? LyricsWebViewLifetimeAction.ReleaseNow : LyricsWebViewLifetimeAction.None;

        if (mediaConnected && lyricLineCount > 0)
            return hasRenderer ? LyricsWebViewLifetimeAction.Retain : LyricsWebViewLifetimeAction.Create;

        return hasRenderer ? LyricsWebViewLifetimeAction.ReleaseAfterGrace : LyricsWebViewLifetimeAction.None;
    }
}
