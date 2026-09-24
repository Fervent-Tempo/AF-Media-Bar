using AFMediaBar.Classes.Settings;
using Lyricify.Lyrics.Searchers.Helpers;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 把用户选择的匹配严格度换算成库要求的匹配等级。
/// Converts the user's match strictness into the match level the library expects.
///
/// 只有搜索型来源用得到它：按曲名与歌手搜索总会返回结果，区别在于"哪一个才真的是这首歌"，等级就是那道闸门。
/// Only the search-based sources use it: a search by title and artist always returns something, and the question is which of
/// those really is the same song, so the level is that gate.
/// </summary>
public static class LyricsMatchPolicy
{
    /// <summary>
    /// 换算匹配等级。
    /// Converts the match level.
    /// </summary>
    /// <param name="strictness">用户选择的严格度 / The strictness the user chose.</param>
    /// <returns>搜索结果必须达到的最低等级 / The minimum level a search result has to reach.</returns>
    public static CompareHelper.MatchType ToMinimumMatch(LyricsMatchStrictness strictness) => strictness switch
    {
        LyricsMatchStrictness.Exact => CompareHelper.MatchType.Perfect,
        LyricsMatchStrictness.Strict => CompareHelper.MatchType.VeryHigh,
        _ => CompareHelper.MatchType.High
    };
}
