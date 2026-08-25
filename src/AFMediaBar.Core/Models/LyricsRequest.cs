namespace AFMediaBar.Models;

/// <summary>
/// 歌词检索请求。NetEaseSongId 命中时精确取词，否则各源按元数据搜索兜底。
/// A lyric lookup request: NetEaseSongId enables exact retrieval, otherwise providers search by metadata.
/// </summary>
public sealed record LyricsRequest(
    string Title,
    string Artist,
    string Album,
    double? DurationSeconds,
    string? NetEaseSongId);
