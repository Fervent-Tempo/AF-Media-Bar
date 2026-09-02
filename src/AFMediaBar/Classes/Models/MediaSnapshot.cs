using System.Windows.Media;

namespace AFMediaBar.Classes.Models;

/// <summary>
/// 媒体快照：封装当前播放媒体的完整状态（元数据、播放状态、控制能力、歌词等）。
/// Media snapshot: encapsulates the complete state of the currently playing media
/// (metadata, playback state, control capabilities, lyrics, etc.).
///
/// 职责 Responsibilities:
/// 1. 作为 MediaSessionService 发布的统一数据结构，供 UI 消费
///    Serves as the unified data structure published by MediaSessionService for UI consumption
/// 2. 支持 SMTC 会话和网易云内存轮询两种数据源
///    Supports both SMTC session and NetEase memory polling data sources
/// 3. 携带封面、歌词、播放位置等 UI 呈现所需的全部信息
///    Carries all information needed for UI rendering: artwork, lyrics, position, etc.
///
/// ⚠️ 架构注意 Architecture Note:
/// 这是一个不可变记录类型（record），每次状态变化都会创建新实例。
/// This is an immutable record type; every state change creates a new instance.
/// </summary>
public sealed record MediaSnapshot(
    bool IsConnected,        // 是否连接到媒体会话 Whether connected to a media session
    bool IsPlaying,          // 是否正在播放 Whether currently playing
    bool CanPlayPause,       // 是否支持播放/暂停控制 Whether play/pause control is available
    bool CanSkipPrevious,    // 是否支持上一首控制 Whether skip-previous control is available
    bool CanSkipNext,        // 是否支持下一首控制 Whether skip-next control is available
    string Title,            // 曲目标题 Track title
    string Artist,           // 艺术家名称 Artist name
    string SourceId,         // 来源标识（AppUserModelId）Source identifier (AppUserModelId)
    string SourceName,       // 来源显示名称 Source display name
    ImageSource? Artwork,    // 封面图像 Artwork image
    LyricsResult? Lyrics,    // 歌词数据 Lyrics data
    double Position)         // 播放位置（秒）Playback position (seconds)
{
    /// <summary>
    /// 断开状态的快照：表示没有可用的媒体会话。
    /// Disconnected snapshot: indicates no available media session.
    ///
    /// 断开快照不携带固定语言文本；窗口根据当前语言资源呈现占位符或错误信息。
    /// Disconnected snapshots carry no fixed-language text; windows resolve placeholders or errors from current resources.
    /// </summary>
    public static MediaSnapshot Disconnected { get; } = new(
        false,
        false,
        false,
        false,
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        null,
        null,
        0);
}
