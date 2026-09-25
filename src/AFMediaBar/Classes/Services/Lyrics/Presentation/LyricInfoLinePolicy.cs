namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 信息行过滤策略：决定哪些被判为信息行的歌词行不参与呈现。
/// Info-line filter policy: decides which lines classified as info lines are kept out of the presentation.
///
/// 静置层只有两行位置，作者、作曲、制作人等标注行会把正文挤出可视区，因此默认丢弃它们；但分类是启发式的，
/// 因此当被判为信息行的比例过高、或过滤后会一行不剩时，策略整体放弃过滤，宁可多显示一行也不清空歌词。
/// The rest layer has room for two lines only, so credits such as writer and producer would push the actual lyrics out
/// of view and are dropped by default. The classification is a heuristic, so when too large a share is flagged, or when
/// dropping would leave nothing, the policy drops everything back to "keep all": one extra line beats an empty display.
/// </summary>
public static class LyricInfoLinePolicy
{
    /// <summary>
    /// 信息行占比超过该比例时放弃过滤（严格大于，即恰好一半仍然过滤）。
    /// Above this share of flagged lines the filter is abandoned (strictly greater, so exactly half is still filtered).
    /// </summary>
    public const double MaximumDropRatio = 0.5;

    /// <summary>
    /// 计算每一行是否应被丢弃。
    /// Computes whether each line should be dropped.
    /// </summary>
    /// <param name="flaggedLines">逐行的信息行判定结果 / Per-line info-line classification.</param>
    /// <returns>与输入等长的丢弃掩码；输入为空时返回空数组 / A drop mask as long as the input, empty for empty input.</returns>
    public static bool[] ResolveDropMask(IReadOnlyList<bool> flaggedLines)
    {
        if (flaggedLines.Count == 0)
        {
            return [];
        }

        var flagged = 0;
        foreach (var isInfoLine in flaggedLines)
        {
            if (isInfoLine)
            {
                flagged++;
            }
        }

        // 没有命中、命中过多、或过滤后会清空时都不过滤。
        // Nothing flagged, too much flagged, or everything flagged all mean "keep the lines".
        if (flagged == 0 ||
            flagged == flaggedLines.Count ||
            flagged > flaggedLines.Count * MaximumDropRatio)
        {
            return new bool[flaggedLines.Count];
        }

        var mask = new bool[flaggedLines.Count];
        for (var i = 0; i < flaggedLines.Count; i++)
        {
            mask[i] = flaggedLines[i];
        }

        return mask;
    }
}
