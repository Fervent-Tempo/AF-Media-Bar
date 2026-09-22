using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>静置层一个组件在某次布局里的横向位置与宽度（均为 DIP，原点在媒体栏左缘）。 / One rest-layer component's horizontal position and width in a single layout, in DIP from the bar's left edge.</summary>
/// <param name="Component">组件。/ The component.</param>
/// <param name="Left">左缘。/ Left edge.</param>
/// <param name="Width">宽度。/ Width.</param>
public readonly record struct TaskbarRestPlacement(TaskbarRestComponent Component, double Left, double Width)
{
    /// <summary>右缘。/ Right edge.</summary>
    public double Right => Left + Width;
}

/// <summary>一次静置层布局的结果：每个可见组件的位置与宽度，以及媒体文字区的范围。 / The result of one rest-layer layout: the position and width of every visible component plus the extent of the media text region.</summary>
/// <param name="Placements">按主轴从左到右排列的可见组件。/ Visible components ordered from left to right along the primary axis.</param>
/// <param name="ContentWidth">这次布局需要的媒体栏长度。/ The bar length this layout needs.</param>
public readonly record struct TaskbarRestLayout(
    IReadOnlyList<TaskbarRestPlacement> Placements,
    double ContentWidth)
{
    /// <summary>媒体文字区的左缘；文字不在布局里时返回 0。 / Left edge of the media text region, or 0 when the text is not part of the layout.</summary>
    public double TextLeft => Find(TaskbarRestComponent.MediaText)?.Left ?? 0;

    /// <summary>媒体文字区的宽度；文字不在布局里时为 0。 / Width of the media text region, or 0 when the text is not part of the layout.</summary>
    public double TextWidth => Find(TaskbarRestComponent.MediaText)?.Width ?? 0;

    /// <summary>取某个组件的位置；它不可见时返回 <see langword="null"/>。 / Returns one component's placement, or <see langword="null"/> when it is not visible.</summary>
    /// <param name="component">组件。/ The component.</param>
    public TaskbarRestPlacement? Find(TaskbarRestComponent component)
    {
        foreach (var placement in Placements)
        {
            if (placement.Component == component)
            {
                return placement;
            }
        }

        return null;
    }

    /// <summary>媒体栏上是否一个组件都没有：没有媒体且用户一个组件都没保留时就是这种状态，宿主据此隐藏整条媒体栏。 / Whether the bar carries no component at all, which is the state with no media and nothing kept; the host hides the whole bar from it.</summary>
    public bool IsEmpty => Placements.Count == 0;
}

/// <summary>
/// 静置层组件的顺序、显隐与横向排布的纯策略：封面与媒体文字固定在最前面，其余组件由用户排序；宽度由各自组件决定，媒体文字吃掉剩余长度。
/// Pure policy for the rest-layer components' order, visibility, and horizontal arrangement: the artwork and the media text are pinned to the front
/// while the user orders the rest; every other component brings its own width and the media text absorbs whatever is left.
/// </summary>
public static class TaskbarRestLayoutPolicy
{
    /// <summary>
    /// 固定在媒体栏最前面的组件，顺序不可更改：封面，然后是媒体文字。
    ///
    /// 这两个是静置层的主干（"谁在放"与"放的是什么"），把它们排到别处只会让这条媒体栏读不出来；用户报告的实际表现是
    /// 顺序一变界面就错乱，因此它们不再参与排序，设置里也不存它们的顺序。
    /// The components pinned to the front of the bar, in a fixed order: the artwork, then the media text.
    ///
    /// These two are the backbone of the rest layer ("who is playing" and "what is playing"), and moving them elsewhere only makes the bar
    /// unreadable; the user reported that reordering them broke the layout, so they no longer take part in ordering and their order is not stored.
    /// </summary>
    public static IReadOnlyList<TaskbarRestComponent> FixedOrder { get; } =
    [
        TaskbarRestComponent.Artwork,
        TaskbarRestComponent.MediaText
    ];

    /// <summary>
    /// 可排序组件的默认顺序：频谱 → 性能 → 输出设备 → 音量。
    /// 设备与音量排在最后是刻意的：它们默认不显示，一旦打开就落在最右侧，与悬停层里它们的位置一致。
    /// The default order of the reorderable components: spectrum, performance, output device, volume.
    /// The device and volume buttons sit last on purpose: they are hidden by default, and once switched on they land on the far right, matching
    /// where they sit in the hover layer.
    /// </summary>
    public static IReadOnlyList<TaskbarRestComponent> DefaultTailOrder { get; } =
    [
        TaskbarRestComponent.Spectrum,
        TaskbarRestComponent.Performance,
        TaskbarRestComponent.OutputDevice,
        TaskbarRestComponent.Volume
    ];

    /// <summary>默认的完整顺序（固定头 + 可排序段）。 / The full default order: the pinned head followed by the reorderable tail.</summary>
    public static IReadOnlyList<TaskbarRestComponent> DefaultOrder { get; } =
        [.. FixedOrder, .. DefaultTailOrder];

    /// <summary>
    /// 没有媒体时默认保留的组件：只有快速启动小音符。
    ///
    /// 没有媒体时封面与媒体文字都没有内容可显示（它们永远不出现在那一份列表里），默认留下的是音符——它既是"当前没有媒体"的
    /// 状态说明，也是无媒体时挑选播放器的入口。
    /// The component kept by default while there is no media: the quick-launch note alone.
    ///
    /// Without media the artwork and the media text have nothing to show (they never appear in that list), and what stays is the note: it
    /// states "nothing is playing" and is the entry that picks a player while there is no media.
    /// </summary>
    public static IReadOnlyList<TaskbarRestComponent> DefaultIdleComponents { get; } = [TaskbarRestComponent.Artwork];

    /// <summary>
    /// 解析生效的组件顺序：封面与媒体文字固定在前，其后是用户给定的可排序段（原样保留、去重），未出现的可排序组件按默认顺序补在后面。
    ///
    /// 补在后面而不是丢弃，是为了让"旧设置文件 + 新版本新增的组件"仍然能用上那个组件；MUST NOT 对用户给定的相对顺序做排序。
    /// 混进列表里的固定头组件会被忽略：它们的位置不参与排序。
    /// Resolves the effective component order: the artwork and the media text come first, followed by the reorderable part the user gave (kept
    /// exactly, duplicates removed), and a reorderable component missing from it is appended in the default order.
    ///
    /// Appending rather than dropping is what lets "an older settings file plus a component a newer version added" still use that component;
    /// the relative order the user gave MUST NOT be sorted. A pinned component appearing in the list is ignored, because its position is not
    /// part of the ordering.
    /// </summary>
    /// <param name="configured">设置文件里的可排序段顺序；<see langword="null"/> 或空表示默认顺序。/ Reorderable order from the settings file; null or empty means the default order.</param>
    public static IReadOnlyList<TaskbarRestComponent> ResolveOrder(IReadOnlyList<TaskbarRestComponent>? configured)
    {
        var order = new List<TaskbarRestComponent>(DefaultOrder.Count);
        order.AddRange(FixedOrder);

        foreach (var component in configured ?? [])
        {
            if (Enum.IsDefined(component) && !order.Contains(component))
            {
                order.Add(component);
            }
        }

        foreach (var component in DefaultTailOrder)
        {
            if (!order.Contains(component))
            {
                order.Add(component);
            }
        }

        return order;
    }

    /// <summary>
    /// 解析"没有媒体时保留哪些组件"：从未配置时为 <see cref="DefaultIdleComponents"/>（快速启动小音符），
    /// 空列表表示用户关掉了全部（一个都不留）。
    /// Resolves which components are kept while there is no media: never configured means <see cref="DefaultIdleComponents"/> (the quick-launch
    /// note), while an empty list means the user turned everything off and nothing is kept.
    /// </summary>
    /// <param name="configured">设置文件里的取值。/ The value from the settings file.</param>
    public static IReadOnlyList<TaskbarRestComponent> ResolveIdleComponents(IReadOnlyList<TaskbarRestComponent>? configured)
    {
        if (configured is null)
        {
            return DefaultIdleComponents;
        }

        var kept = new List<TaskbarRestComponent>(configured.Count);
        foreach (var component in configured)
        {
            if (Enum.IsDefined(component) && !kept.Contains(component))
            {
                kept.Add(component);
            }
        }

        return kept;
    }

    /// <summary>静置层显隐判定所需的输入。 / Inputs the rest-layer visibility decision needs.</summary>
    /// <param name="MediaConnected">是否有已连接的 SMTC 来源。/ Whether a connected SMTC source exists.</param>
    /// <param name="IdleComponents">没有媒体时保留的组件；<see langword="null"/> 表示从未配置（用默认值）。/ Components kept without media; null means never configured (the default applies).</param>
    /// <param name="SpectrumEnabled">用户是否打开了频谱。/ Whether the user enabled the spectrum.</param>
    /// <param name="PerformanceEnabled">用户是否打开了性能组件。/ Whether the user enabled the performance component.</param>
    /// <param name="OutputDeviceEnabled">用户是否把设备按钮放进了静置层。/ Whether the user put the device button into the rest layer.</param>
    /// <param name="VolumeEnabled">用户是否把音量按钮放进了静置层。/ Whether the user put the volume button into the rest layer.</param>
    public readonly record struct Visibility(
        bool MediaConnected,
        IReadOnlyList<TaskbarRestComponent>? IdleComponents,
        bool SpectrumEnabled,
        bool PerformanceEnabled,
        bool OutputDeviceEnabled,
        bool VolumeEnabled);

    /// <summary>
    /// 判定一个静置层组件这次是否可见。
    ///
    /// 有媒体时封面与媒体文字始终在（它们是这条媒体栏存在的理由，因此不提供开关），其余按各自开关。没有媒体时由
    /// "没有媒体时显示"列表唯一决定，且媒体文字那时一定不显示——没有媒体时它没有任何内容可写。
    ///
    /// 没有媒体时 `Artwork` 这一项指的是**快速启动小音符**而不是曲目封面：封面那个框在无媒体时画的正是音符
    /// （`UpdateSongInfo` 的断开分支），所以"封面一定不能显示"与"列表里可以勾选小音符"并不矛盾——它们是同一个框的两种内容。
    /// Decides whether one rest-layer component is visible this time.
    ///
    /// While media is connected the artwork and the media text are always there — they are the reason this bar exists, so they carry no switch —
    /// and the rest follow their own switches. Without media the "shown without media" list decides alone, and the media text is never shown then:
    /// with no media it has nothing to write.
    ///
    /// Without media the `Artwork` entry means the **quick-launch note** rather than a track's cover: that box draws the note while disconnected
    /// (the disconnected branch of `UpdateSongInfo`), so "a cover is never shown" and "the note can be checked in the list" are not in conflict —
    /// they are two contents of one box.
    /// </summary>
    /// <param name="component">组件。/ The component.</param>
    /// <param name="visibility">当前输入。/ The current inputs.</param>
    public static bool IsVisible(TaskbarRestComponent component, in Visibility visibility)
    {
        if (!visibility.MediaConnected)
        {
            // 没有媒体时媒体文字没有任何内容可写，因此它不在保留列表的可选项里；列表即便写了它也不算数。
            // Without media the media text has nothing to write, so it is not an option in the kept list; naming it there has no effect.
            if (component == TaskbarRestComponent.MediaText)
            {
                return false;
            }

            return ResolveIdleComponents(visibility.IdleComponents).Contains(component);
        }

        return component switch
        {
            TaskbarRestComponent.Artwork => true,
            TaskbarRestComponent.MediaText => true,
            TaskbarRestComponent.Spectrum => visibility.SpectrumEnabled,
            TaskbarRestComponent.Performance => visibility.PerformanceEnabled,
            TaskbarRestComponent.OutputDevice => visibility.OutputDeviceEnabled,
            TaskbarRestComponent.Volume => visibility.VolumeEnabled,
            _ => false
        };
    }

    /// <summary>
    /// 按生效顺序排布可见组件。除媒体文字外的每个组件都用调用方给出的固定宽度，媒体文字吃掉剩余长度（不小于 0）；
    /// 组件之间统一留一个 <paramref name="sectionGap"/>，尾部再留 <paramref name="trailingMargin"/>。
    ///
    /// 排布只依赖"哪些组件可见"，与顺序无关的总宽度则由 <see cref="TaskbarExperiencePolicy.CalculateRestWidth"/> 单独计算：
    /// 两处的间距条数一致（可见组件数 − 1），因此媒体栏长度与组件位置不会互相矛盾。
    /// Arranges the visible components in the effective order. Every component except the media text uses the fixed width the caller supplies,
    /// while the media text absorbs the remaining length (never below zero); one <paramref name="sectionGap"/> separates adjacent components and
    /// <paramref name="trailingMargin"/> closes the bar.
    ///
    /// The arrangement only depends on *which* components are visible, while the order-independent total width is computed separately by
    /// <see cref="TaskbarExperiencePolicy.CalculateRestWidth"/>: both use the same number of gaps (visible components minus one), so the bar length
    /// and the component positions can never contradict each other.
    /// </summary>
    /// <param name="order">生效顺序（<see cref="ResolveOrder"/> 的结果）。/ The effective order, as returned by <see cref="ResolveOrder"/>.</param>
    /// <param name="widthOf">取某个组件固定宽度的函数；媒体文字不会被问到。/ Supplies one component's fixed width; the media text is never asked.</param>
    /// <param name="isVisible">某个组件这次是否可见。/ Whether one component is visible this time.</param>
    /// <param name="leadingInset">第一个组件之前的留白（DIP）。/ Padding before the first component, in DIP.</param>
    /// <param name="availableWidth">媒体栏长度（DIP）。/ Bar length in DIP.</param>
    /// <param name="sectionGap">组件间距（DIP）。/ Gap between components, in DIP.</param>
    /// <param name="trailingMargin">尾部留白（DIP）。/ Trailing margin in DIP.</param>
    public static TaskbarRestLayout Arrange(
        IReadOnlyList<TaskbarRestComponent> order,
        Func<TaskbarRestComponent, double> widthOf,
        Func<TaskbarRestComponent, bool> isVisible,
        double leadingInset,
        double availableWidth,
        double sectionGap,
        double trailingMargin)
    {
        var visible = new List<TaskbarRestComponent>(order.Count);
        foreach (var component in order)
        {
            if (isVisible(component))
            {
                visible.Add(component);
            }
        }

        if (visible.Count == 0)
        {
            return new TaskbarRestLayout([], 0);
        }

        var fixedWidth = 0.0;
        var hasText = false;
        foreach (var component in visible)
        {
            if (component == TaskbarRestComponent.MediaText)
            {
                hasText = true;
                continue;
            }

            fixedWidth += Math.Max(0, widthOf(component));
        }

        var gapTotal = Math.Max(0, visible.Count - 1) * Math.Max(0, sectionGap);
        var textWidth = hasText
            ? Math.Max(0, availableWidth - leadingInset - trailingMargin - fixedWidth - gapTotal)
            : 0;

        var placements = new List<TaskbarRestPlacement>(visible.Count);
        var cursor = Math.Max(0, leadingInset);
        for (var index = 0; index < visible.Count; index++)
        {
            var component = visible[index];
            var width = component == TaskbarRestComponent.MediaText ? textWidth : Math.Max(0, widthOf(component));
            placements.Add(new TaskbarRestPlacement(component, cursor, width));
            cursor += width;
            if (index < visible.Count - 1)
            {
                cursor += Math.Max(0, sectionGap);
            }
        }

        return new TaskbarRestLayout(placements, cursor + Math.Max(0, trailingMargin));
    }
}
