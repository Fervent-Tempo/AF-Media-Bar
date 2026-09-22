namespace AFMediaBar.Classes.Services;

/// <summary>
/// 判定"这次会话要不要走在线取词兜底"的纯策略。
/// Pure policy that decides whether a session needs the online-lyric fallback.
/// </summary>
public static class MediaEnrichmentFallbackPolicy
{
    /// <summary>
    /// 是否应当为当前会话发起一次在线取词。
    ///
    /// 网易云一类的来源本来由来源提供器（进程内存读取）供词：它给的是逐字歌词与精确进度，比在线搜索准得多，
    /// 因此常规路径对这类来源**根本不取词**。问题在于提供器读不出东西时那条路径完全没有兜底——客户端改了内存
    /// 布局、私人FM 的队列认不出来、进程刚起来——界面就只剩 SMTC 给的标题、歌手与封面，进度与时长恒为 0，
    /// 而且没有任何一条日志说明取词被跳过了。
    ///
    /// 因此兜底的条件是"提供器**明确报过**它没有可读的媒体"，而不是"来源属于网易云"：提供器正常工作时它的快照会
    /// 覆盖基线，这里再取一次只会每一首歌白发一次请求（用户看不到这份结果，因为提供器的快照赢）。
    /// Whether an online retrieval should be started for the current session.
    ///
    /// A source such as NetEase is normally served by its source provider (process-memory reading), which returns word-level lyrics and
    /// an exact position that an online search cannot match, so the ordinary path fetches nothing for those sources at all. The problem is
    /// that once the provider cannot read anything — the client changed its memory layout, the private-FM queue is not recognised, the
    /// process just started — that path has no fallback whatsoever: only the title, artist, and artwork SMTC reports remain, position and
    /// duration stay at zero, and no log line says that retrieval was skipped.
    ///
    /// The condition is therefore "the provider has **explicitly reported** that it has no readable media", not "the source belongs to
    /// NetEase": while the provider works, its snapshot overrides the baseline, and fetching here would send one useless request per track
    /// (the user never sees that result, because the provider's snapshot wins).
    /// </summary>
    /// <param name="mediaConnected">SMTC 基线快照是否已连接。/ Whether the SMTC baseline snapshot is connected.</param>
    /// <param name="hasSourceProvider">该来源是否有提供器。/ Whether this source has a provider.</param>
    /// <param name="providerPublishedNothing">提供器是否已经报过"没有可读的媒体"（发布过快照且值为空）。/ Whether the provider has reported having no readable media (it published, and the value was empty).</param>
    /// <param name="title">基线快照的曲名；为空时无从取词。/ Title of the baseline snapshot; nothing can be fetched without one.</param>
    public static bool ShouldRequestOnlineLyrics(
        bool mediaConnected,
        bool hasSourceProvider,
        bool providerPublishedNothing,
        string? title) =>
        mediaConnected &&
        hasSourceProvider &&
        providerPublishedNothing &&
        !string.IsNullOrWhiteSpace(title);
}
