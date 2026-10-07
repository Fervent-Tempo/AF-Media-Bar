using System;
using System.Collections.Generic;
using System.Linq;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 设置页标识。搜索索引必须在不引用任何 View 类型的前提下指出目标页面，
/// 因此这里用与视图解耦的稳定键，由设置窗口在视图层把它映射到具体页面类型。
/// Settings page identity. The search index has to name a target page without referencing any view type,
/// so it uses a stable key that the settings window maps to a concrete page in the view layer.
/// </summary>
public enum SettingsPageKey
{
    /// <summary>显示模式页。/ Display modes page.</summary>
    DisplayModes,

    /// <summary>媒体与通知页。/ Media and notifications page.</summary>
    MediaAndNotifications,

    /// <summary>静置层组件设置页。/ Rest-layer component settings page.</summary>
    Components,

    /// <summary>交互页。/ Interaction page.</summary>
    Interaction,

    /// <summary>歌词页。/ Lyrics page.</summary>
    Lyrics,

    /// <summary>外观页。/ Appearance page.</summary>
    Appearance,

    /// <summary>应用页：版本、更新、界面语言、开机自启、设置文件与诊断日志。/ Application page: version, updates, interface language, run-at-startup, settings file, and diagnostics.</summary>
    Application,

    /// <summary>关于页：开发人员、赞助者、赞助入口、开源许可与项目信息。/ About page: developers, sponsors, the support entry, open-source licenses, and project information.</summary>
    About,

    /// <summary>Read-only latest and historical release highlights.</summary>
    ReleaseHighlights,
    /// <summary>Monitor selection and taskbar placement, independent of display-mode previews.</summary>
    ScreenAndPlacement,
}

/// <summary>
/// 搜索命中所属的显示模式分区。显示模式页按模式切换内容，因此分组序号只在该模式内有意义：
/// 不带上模式，一次命中就会跳到另一个模式里同序号的分组上。
/// The display-mode section a hit belongs to. The display-mode page swaps its content by mode, so a group index
/// is only meaningful inside its own mode; without the mode, a hit would jump to that index in the wrong section.
/// </summary>
public enum SettingsSearchMode
{
    /// <summary>任务栏模式的设置区。/ The taskbar mode's settings section.</summary>
    Taskbar,

    /// <summary>灵动岛模式的设置区。/ The dynamic island mode's settings section.</summary>
    DynamicIsland,

    /// <summary>桌面卡片模式的设置区，当前没有设置。/ The desktop card mode's section, which currently has no settings.</summary>
    DesktopCard,

    /// <summary>悬浮球模式的设置区，当前没有设置。/ The floating ball mode's section, which currently has no settings.</summary>
    FloatingBall,
}

/// <summary>
/// 搜索索引条目：一个分组在页面内的位置，以及可被搜索到的标题、说明和关键词。
/// One search index entry: where a group lives and the title, description, and keywords it answers to.
/// </summary>
/// <param name="Page">目标页面键。/ Target page key.</param>
/// <param name="GroupIndex">分组序号，与分组容器的声明顺序一致；显示模式页上它指的是所属模式分区内的序号。/ Group index matching declaration order; on the display-mode page it counts within the entry's own mode section.</param>
/// <param name="PageTitle">页面在导航栏里的名称；用它搜索会列出该页的全部分组。/ The page's navigation label; searching it lists every group on that page.</param>
/// <param name="Title">分组名称，也是结果的主文案。/ Group name, and the primary text of a result.</param>
/// <param name="Description">一句话说明该分组能改什么。/ One line describing what the group can change.</param>
/// <param name="Keywords">同名选项、同义说法和英文标识，让用户用自己记得的词也能搜到。/ Option names, synonyms, and English identifiers so users can search with whatever word they remember.</param>
public readonly record struct SettingsSearchEntry(
    SettingsPageKey Page,
    int GroupIndex,
    string PageTitle,
    string Title,
    string Description,
    IReadOnlyList<string> Keywords)
{
    /// <summary>该分组所属的显示模式分区；只有显示模式页使用，其它页保持默认值。/ The display-mode section this group belongs to; only the display-mode page uses it and other pages keep the default.</summary>
    public SettingsSearchMode Mode { get; init; } = SettingsSearchMode.Taskbar;
}

/// <summary>
/// 一条搜索命中。/ A single search hit.
/// </summary>
/// <param name="Page">目标页面键。/ Target page key.</param>
/// <param name="GroupIndex">目标分组序号，含义见 <see cref="SettingsSearchEntry.GroupIndex"/>。/ Target group index; see <see cref="SettingsSearchEntry.GroupIndex"/> for what it counts within.</param>
/// <param name="PageTitle">页面名称。/ Page title.</param>
/// <param name="Title">分组名称。/ Group name.</param>
/// <param name="Description">分组说明。/ Group description.</param>
public readonly record struct SettingsSearchHit(
    SettingsPageKey Page,
    int GroupIndex,
    string PageTitle,
    string Title,
    string Description)
{
    /// <summary>该命中所属的显示模式分区。/ The display-mode section this hit belongs to.</summary>
    public SettingsSearchMode Mode { get; init; } = SettingsSearchMode.Taskbar;
}

/// <summary>
/// 设置搜索策略：把用户输入解析成按相关度排序的分组命中。
/// 纯逻辑，不引用 View、ViewModel 或设置文件，因此可以直接单元测试。
/// Settings search policy: resolves user input into relevance-ordered group hits. Pure logic: it references
/// no view, view model, or settings file, so it is unit-testable.
///
/// 排序理由：完全匹配和前缀匹配说明用户已经知道自己在找什么，应当排在最前；页面名命中排在分组名之后、
/// 说明与关键词之前，因为页面名只是把范围缩小到一页，而说明与关键词是维护者写的同义词，证据更弱。
/// Ranking rationale: an exact or prefix match means the user already knows what to look for and belongs at
/// the top; a page-name hit ranks below a group-name hit but above description and keyword hits, because a page
/// name only narrows the scope to one page while synonyms written by the maintainer are weaker evidence still.
/// </summary>
public static class SettingsSearchPolicy
{
    /// <summary>最多返回的命中数量；超过这个数量列表本身就变成需要再读一遍的负担。/ Maximum number of hits; beyond this the result list becomes something to read rather than to choose from.</summary>
    public const int MaxResults = 8;

    /// <summary>
    /// 按相关度搜索分组。空输入、只有空白的输入或空索引都返回空结果，不做“显示全部”的回退。
    /// Searches groups by relevance. An empty query, a whitespace-only query, or an empty index returns no
    /// results rather than falling back to "show everything".
    /// </summary>
    /// <param name="query">用户输入。/ User input.</param>
    /// <param name="entries">搜索索引。/ Search index.</param>
    public static IReadOnlyList<SettingsSearchHit> Search(string? query, IReadOnlyList<SettingsSearchEntry> entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return Array.Empty<SettingsSearchHit>();
        }

        var term = query?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return Array.Empty<SettingsSearchHit>();
        }

        var scored = new List<(int Rank, int Order, SettingsSearchHit Hit)>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var rank = Rank(entry, term);
            if (rank >= 0)
            {
                scored.Add((rank, index, new SettingsSearchHit(
                    entry.Page,
                    entry.GroupIndex,
                    entry.PageTitle,
                    entry.Title,
                    entry.Description)
                {
                    Mode = entry.Mode,
                }));
            }
        }

        return scored
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.Order)
            // 同名分组只留最靠前的一条。显示模式页的模式选择分组在四种模式下各有一条索引
            // （分组序号只在各自模式内成立），但候选列表里重复四次只是噪音。
            // Only the best-ranked entry per group title survives. The display-mode picker has one index entry per
            // mode, because a group index only holds inside its own mode, but repeating it four times in the
            // suggestion list is nothing but noise.
            .DistinctBy(candidate => (candidate.Hit.Page, candidate.Hit.Title))
            .Take(MaxResults)
            .Select(candidate => candidate.Hit)
            .ToArray();
    }

    /// <summary>
    /// 给一条索引对输入打分；不命中返回 -1。分数越小越靠前。
    /// Scores one entry against the query, returning -1 when it does not match. Lower scores sort first.
    /// </summary>
    private static int Rank(SettingsSearchEntry entry, string term)
    {
        if (string.Equals(entry.Title, term, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (entry.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (entry.PageTitle.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (entry.Description.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        foreach (var keyword in entry.Keywords)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                continue;
            }

            if (keyword.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            {
                return 5;
            }

            if (keyword.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return 6;
            }
        }

        return -1;
    }
}
