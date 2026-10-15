// 固定取词阶段与同分选择的纯策略；扩展顺序只改此策略/目录，不改 UI 或网络协调者。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>优先源与并发备用源的执行计划。/ Preferred-source and parallel-fallback plan.</summary>
internal sealed record LyricsRetrievalPlan(ILyricsProvider? Preferred, IReadOnlyList<ILyricsProvider> Fallbacks);

/// <summary>构造取词阶段；备用阶段优先当前播放器对应结果，再按匹配分选择。</summary>
internal static class LyricsRetrievalPolicy
{
    // 默认规则：QQ 优质歌词库优先；备用源并发后按分数采纳。手动来源顺序不改变这条策略。
    internal const string PreferredSource = LyricsSourceCatalog.QQMusic;
    // QQ 达到此分数直接结束优先阶段；低分结果保留，与备用源按真实元数据评分比较。
    internal const int PreferredMinimumScore = 80;

    public static LyricsRetrievalPlan Plan(IReadOnlyList<ILyricsProvider> providers, IReadOnlyList<ILyricsProvider> enabled)
    {
        var active = providers.Where(provider => enabled.Contains(provider)).ToArray();
        var preferred = active.FirstOrDefault(provider => provider.SourceName == PreferredSource);
        return new(preferred, active.Where(provider => !ReferenceEquals(provider, preferred)).ToArray());
    }

    // Current-player results override scores; otherwise stable ordering makes QQ win ties.
    public static LyricsResult? Select(IEnumerable<LyricsResult?> results, LyricsResult? preferred = null,
        LyricsResult? currentPlayback = null) => currentPlayback ?? results.Prepend(preferred)
        .Where(result => result is not null).OrderByDescending(result => result!.MatchScore).FirstOrDefault();
}
