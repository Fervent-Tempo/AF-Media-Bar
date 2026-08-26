namespace AFMediaBar.Models;

/// <summary>
/// 已拉取的歌词。Source 标识来源，Lrc 为 LRC 格式文本，Translation 为可选译文。
/// Fetched lyrics: Source names the provider, Lrc holds LRC text, and Translation is an optional translation.
/// </summary>
public sealed record LyricsResult(string Source, string Lrc, string? Translation);
