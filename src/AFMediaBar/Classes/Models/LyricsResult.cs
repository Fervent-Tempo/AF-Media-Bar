namespace AFMediaBar.Classes.Models;

/// <summary>
/// 歌词结果：封装从歌词提供者获取的歌词数据。
/// Lyrics result: encapsulates lyrics data fetched from a lyrics provider.
///
/// 职责 Responsibilities:
/// 1. 存储 LRC 格式的歌词文本
///    Store lyrics text in LRC format
/// 2. 标识歌词来源（网易云、Lrclib 等）
///    Identify lyrics source (NetEase, Lrclib, etc.)
/// 3. 可选的歌词翻译（如有）
///    Optional lyrics translation (if available)
///
/// ⚠️ 注意 Note:
/// 这是一个不可变记录类型（record），每次修改都会创建新实例。
/// This is an immutable record type; modifications create new instances.
/// </summary>
/// <param name="Source">歌词来源标识（如 "NetEase", "Lrclib"）Source identifier (e.g. "NetEase", "Lrclib")</param>
/// <param name="Lrc">LRC 格式的歌词文本 Lyrics text in LRC format</param>
/// <param name="Translation">可选的歌词翻译 Optional lyrics translation</param>
public sealed record LyricsResult(string Source, string Lrc, string? Translation);
