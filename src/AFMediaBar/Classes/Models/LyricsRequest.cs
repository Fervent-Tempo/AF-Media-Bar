using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Models;

/// <summary>
/// 歌词检索请求：曲目元数据加上两个取词选项。
/// A lyric lookup request: track metadata plus the two retrieval options.
///
/// 两个选项由 <c>LyricsService</c> 在发起取词前按当前设置填入，因此提供器不需要读设置，保持无状态且可单测；
/// 直接构造时它们是默认值（均衡匹配、过滤信息行），与升级前的行为一致。
/// The service fills both options from the current settings before it starts retrieving, so providers never read the settings and
/// stay stateless and testable; a request built directly keeps the defaults (balanced matching, filtered info lines), which match
/// the behaviour that was in effect before these options existed.
/// </summary>
/// <param name="Title">曲名 / Track title.</param>
/// <param name="Artist">歌手 / Artist.</param>
/// <param name="Album">专辑 / Album.</param>
/// <param name="DurationSeconds">曲目时长（秒），未知为 null / Track duration in seconds, null when unknown.</param>
/// <param name="NetEaseSongId">网易云歌曲 id；非空时按 id 精确取词 / NetEase song id; a non-null value retrieves by id.</param>
/// <param name="SourceAppId">正在播放的来源应用的 SMTC 标识（SourceAppUserModelId 原文），未知为 null。
/// 链路用它挑出该播放器的默认取词来源；原文保留，识别交给链路层的映射策略，本模型不做解释。
/// The SMTC identifier (raw SourceAppUserModelId) of the playing source application, null when unknown. The retrieval chain uses it to
/// pick that player's default lyric source; the raw value is kept as-is and interpretation belongs to the chain's mapping policy, not this model.</param>
/// <param name="MatchStrictness">搜索型来源的匹配严格度 / Match strictness for search-based sources.</param>
/// <param name="FilterInfoLines">是否丢弃作者、作曲等信息行 / Whether credit lines such as writer and composer are dropped.</param>
public sealed record LyricsRequest(
    string Title,
    string Artist,
    string Album,
    double? DurationSeconds,
    string? NetEaseSongId,
    string? SourceAppId = null,
    LyricsMatchStrictness MatchStrictness = LyricsMatchStrictness.Balanced,
    bool FilterInfoLines = true);
