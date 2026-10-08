namespace AFMediaBar.Classes.Models;

/// <summary>
/// 歌词检索请求，保留播放器原始元数据；过滤选项由协调器在取词前填入。
/// Retains original player metadata; filtering is supplied by the coordinator and scoring belongs to the fixed policy.
/// </summary>
/// <param name="Title">曲名 / Track title.</param>
/// <param name="Artist">歌手 / Artist.</param>
/// <param name="Album">专辑 / Album.</param>
/// <param name="DurationSeconds">曲目时长（秒），未知为 null / Track duration in seconds, null when unknown.</param>
/// <param name="NetEaseSongId">网易云歌曲 id；非空时按 id 精确取词 / NetEase song id; a non-null value retrieves by id.</param>
/// <param name="FilterInfoLines">是否丢弃作者、作曲等信息行 / Whether credit lines such as writer and composer are dropped.</param>
/// <param name="PlaybackSourceId">当前播放器来源标识，用于选择其对应的已启用歌词源。</param>
public sealed record LyricsRequest(
    string Title,
    string Artist,
    string Album,
    double? DurationSeconds,
    string? NetEaseSongId,
    bool FilterInfoLines = true,
    string? PlaybackSourceId = null)
{
    /// <summary>本次取词固定的自定义分隔符，每行一项；不改变播放器原始艺术家文本。</summary>
    public string ArtistSeparators { get; init; } = string.Empty;
}
