namespace AFMediaBar.Classes.Services;

/// <summary>当前会话实际可执行的播放/暂停操作。/ Play/pause action actually supported by the active session.</summary>
public enum MediaPlayPauseAction
{
    None,
    Toggle,
    Play,
    Pause
}

/// <summary>
/// 同步快照的按钮能力与命令发送方式，不把只读元数据当作可控会话。
/// Keeps snapshot capabilities and command dispatch consistent without treating read-only metadata as transport support.
/// </summary>
public static class MediaTransportPolicy
{
    /// <summary>优先使用切换命令，否则按当前状态选择明确的播放或暂停命令。/ Prefers toggle, then selects a supported explicit command for the current playback state.</summary>
    public static MediaPlayPauseAction ResolvePlayPause(bool isPlaying, bool canToggle, bool canPlay, bool canPause)
    {
        if (canToggle)
            return MediaPlayPauseAction.Toggle;
        if (isPlaying)
            return canPause ? MediaPlayPauseAction.Pause : MediaPlayPauseAction.None;
        return canPlay ? MediaPlayPauseAction.Play : MediaPlayPauseAction.None;
    }
}
