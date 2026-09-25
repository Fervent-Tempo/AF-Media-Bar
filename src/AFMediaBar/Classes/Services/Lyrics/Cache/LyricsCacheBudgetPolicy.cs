using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 歌词缓存的体积预算：按"条数"封顶挡不住真正的大对象。
/// The size budget of the lyric caches: capping by entry count does not hold back the entries that actually cost memory.
///
/// 一条纯文本歌词与一条带逐字时间轴的歌词差着几十倍：前者几十行短字符串，后者每行还有一组音节时间片。
/// 因此缓存同时受"条数"和"估算字节"两个上限约束，估算值只需要**相对**准确——它的用途是排序与淘汰，不是报表。
/// A plain-text lyric and one with a syllable timeline differ by tens of times: the first is a few dozen short strings, while the second carries a group
/// of syllable spans per line. The caches are therefore bounded by both an entry count and an estimated size, and the estimate only has to be *relatively*
/// accurate, because it is used for ordering and eviction rather than for reporting.
/// </summary>
public static class LyricsCacheBudgetPolicy
{
    /// <summary>
    /// 每个歌词缓存的估算字节上限（4 MB）。
    /// 取值依据：一条带逐字时间轴的曲目大约 100–200 KB，4 MB 因此能覆盖"当前曲目 + 前后若干首"这个真正会被回访的窗口；
    /// 而一旦有人连着播放几十首带逐字的曲目，条数上限（64）会比这个预算先触发，也就是说它只在**真正占内存**时才起作用。
    /// The estimated-size ceiling of each lyric cache, 4 MB. The figure comes from a track with a syllable timeline costing roughly 100–200 KB, so 4 MB
    /// covers the window that is genuinely revisited — the current track plus its neighbours — while the entry cap of 64 already triggers first for plain
    /// text, which means this budget only bites when the cache really is large.
    /// </summary>
    public const long DefaultBudgetBytes = 4 * 1024 * 1024;

    // 粗略的对象与容器开销（字节）。它们只需要把量级拉开，不需要精确：字符串是按 UTF-16 估的，容器按每个元素一个引用估。
    // Rough object and container overheads in bytes. They only need to separate the orders of magnitude rather than be exact: strings are estimated as
    // UTF-16 and containers as one reference per element.
    private const int ObjectOverheadBytes = 48;
    private const int StringOverheadBytes = 24;
    private const int BytesPerChar = 2;
    private const int WordSpanBytes = 24;
    private const int ReferenceBytes = 8;

    /// <summary>
    /// 估算一个歌词文档占用的托管内存。
    /// Estimates the managed memory one lyric document occupies.
    /// </summary>
    /// <param name="document">歌词文档；为空时返回 0。/ The lyric document, or 0 when it is null.</param>
    /// <returns>估算字节数。/ The estimated byte count.</returns>
    public static long EstimateBytes(LyricDocument? document)
    {
        if (document is null)
        {
            return 0;
        }

        var lines = document.Lines;
        if (lines is null || lines.Count == 0)
        {
            // 空文档仍然有对象与来源格式字符串，只是可以忽略不计；返回一个小的非零值，保持"缓存里有东西"这一事实。
            // An empty document still has its object and its source-format string, only negligible ones; a small non-zero value keeps the fact that
            // something is cached visible.
            return ObjectOverheadBytes;
        }

        long total = ObjectOverheadBytes + StringLength(document.SourceFormat ?? string.Empty);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line is null)
            {
                continue;
            }

            total += ObjectOverheadBytes + ReferenceBytes + StringLength(line.Text);
            total += StringLength(line.Translation) + StringLength(line.Romanization);

            if (line.Words is { Count: > 0 } words)
            {
                total += ObjectOverheadBytes + (long)words.Count * WordSpanBytes;
                for (var wordIndex = 0; wordIndex < words.Count; wordIndex++)
                {
                    total += StringLength(words[wordIndex].Text);
                }
            }
        }

        return total;
    }

    /// <summary>估算一个字符串占用的字节数（含对象头与长度字段）。/ Estimates the bytes one string occupies, including its header and length field.</summary>
    /// <param name="text">字符串；为空时返回 0。/ The string, or 0 when it is null.</param>
    /// <returns>估算字节数。/ The estimated byte count.</returns>
    private static long StringLength(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : StringOverheadBytes + (long)text.Length * BytesPerChar;
}
