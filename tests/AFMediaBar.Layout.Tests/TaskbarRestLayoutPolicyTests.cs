using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 静置层组件顺序、显隐与横向排布的纯策略测试。
/// Pure policy tests for the rest-layer components' order, visibility, and horizontal arrangement.
///
/// 这些断言守的是"看得见的错误"：固定头被排到别处或用户排出来的顺序被重排、新增组件在旧设置文件里消失、
/// 没有媒体时该留的组件没留或该隐藏的没隐藏，以及媒体栏长度与组件位置互相矛盾（后者表现为最后一个组件被窗口裁掉）。
/// These assertions guard visible defects: a pinned component moved or a user order rearranged, a newly added component vanishing from an older
/// settings file, components that are not kept (or not hidden) while there is no media, and a bar length that contradicts the component positions,
/// which shows up as the last component being clipped by the window.
/// </summary>
[TestClass]
public sealed class TaskbarRestLayoutPolicyTests
{
    private static double WidthOf(TaskbarRestComponent component) => component switch
    {
        TaskbarRestComponent.Artwork => 40,
        TaskbarRestComponent.Spectrum => 30,
        TaskbarRestComponent.Performance => 76,
        TaskbarRestComponent.OutputDevice => 26,
        TaskbarRestComponent.Volume => 26,
        _ => 0
    };

    private static TaskbarRestLayoutPolicy.Visibility Connected(
        bool spectrum = true,
        bool performance = true,
        bool outputDevice = false,
        bool volume = false,
        IReadOnlyList<TaskbarRestComponent>? idleComponents = null) =>
        new(true, idleComponents, spectrum, performance, outputDevice, volume);

    private static TaskbarRestLayoutPolicy.Visibility Idle(IReadOnlyList<TaskbarRestComponent>? idleComponents) =>
        new(false, idleComponents, true, true, true, true);

    /// <summary>
    /// 封面与媒体文字固定在媒体栏最前面，用户的排序只能作用在其余四个组件上；未列出的可排序组件按默认顺序补在后面
    /// （新增组件因此不会在旧设置文件里消失），而混进列表里的固定头组件被忽略。
    /// The artwork and the media text are pinned to the front and the user's ordering only reaches the other four components; a reorderable
    /// component missing from the list is appended in the default order (so a newly added one never vanishes from an older settings file), and a
    /// pinned component that sneaks into the list is ignored.
    /// </summary>
    [TestMethod]
    public void FixedHeadLeadsAndOnlyTheTailIsReorderable()
    {
        var resolved = TaskbarRestLayoutPolicy.ResolveOrder(
        [
            TaskbarRestComponent.MediaText,
            TaskbarRestComponent.Volume,
            TaskbarRestComponent.Artwork,
            TaskbarRestComponent.Spectrum
        ]);

        CollectionAssert.AreEqual(
            new[]
            {
                TaskbarRestComponent.Artwork,
                TaskbarRestComponent.MediaText,
                TaskbarRestComponent.Volume,
                TaskbarRestComponent.Spectrum,
                TaskbarRestComponent.Performance,
                TaskbarRestComponent.OutputDevice
            },
            resolved.ToArray());

        CollectionAssert.AreEqual(
            TaskbarRestLayoutPolicy.DefaultOrder.ToArray(),
            TaskbarRestLayoutPolicy.ResolveOrder(null).ToArray());
        CollectionAssert.AreEqual(
            TaskbarRestLayoutPolicy.DefaultOrder.ToArray(),
            TaskbarRestLayoutPolicy.ResolveOrder([]).ToArray());
    }

    /// <summary>
    /// 排布按生效顺序从左到右落地，媒体文字吃掉剩余长度；组件之间只留一个间距，总长度（ContentWidth）与最后一个组件的右缘一致。
    /// 两者不一致时最后一个组件会被窗口裁掉。
    /// The arrangement lands left to right in the effective order with the media text absorbing what is left; adjacent components are separated by one
    /// gap, and the total length (ContentWidth) matches the last component's right edge. When the two disagree, the last component is clipped by
    /// the window.
    /// </summary>
    [TestMethod]
    public void ComponentsAreArrangedLeftToRightAndTheLengthMatchesTheLastEdge()
    {
        var layout = TaskbarRestLayoutPolicy.Arrange(
            TaskbarRestLayoutPolicy.ResolveOrder(
                [TaskbarRestComponent.Spectrum, TaskbarRestComponent.Volume]),
            WidthOf,
            component => TaskbarRestLayoutPolicy.IsVisible(component, Connected(spectrum: true, performance: false, volume: true)),
            leadingInset: 4,
            availableWidth: 300,
            sectionGap: 8,
            trailingMargin: 4);

        var placements = layout.Placements;
        // 封面 → 媒体文字 → 频谱 → 音量：用户把音量排到频谱之后，它就落在最右边。
        // Artwork, media text, spectrum, volume: the user put volume after the spectrum, so it lands on the far right.
        Assert.AreEqual(TaskbarRestComponent.Artwork, placements[0].Component);
        Assert.AreEqual(4, placements[0].Left, 0.001);
        Assert.AreEqual(TaskbarRestComponent.MediaText, placements[1].Component);
        Assert.AreEqual(52, placements[1].Left, 0.001);
        Assert.AreEqual(TaskbarRestComponent.Spectrum, placements[2].Component);
        Assert.AreEqual(TaskbarRestComponent.Volume, placements[3].Component);
        // 300 − 4（前留白）− 40（封面）− 30（频谱）− 26（音量）− 4（尾留白）− 3×8（间距）= 172
        Assert.AreEqual(172, placements[1].Width, 0.001);
        Assert.AreEqual(300, layout.ContentWidth, 0.001);
        Assert.AreEqual(placements[3].Right + 4, layout.ContentWidth, 0.001);
        Assert.AreEqual(52, layout.TextLeft, 0.001);
        Assert.AreEqual(172, layout.TextWidth, 0.001);
    }

    /// <summary>媒体文字不在顺序里时其余组件仍然排得出来，且没有文字宽度可言。/ When the media text is not part of the order the other components still arrange, and there is no text width.</summary>
    [TestMethod]
    public void ArrangementWorksWithoutTheMediaText()
    {
        var layout = TaskbarRestLayoutPolicy.Arrange(
            [TaskbarRestComponent.Artwork, TaskbarRestComponent.Performance],
            WidthOf,
            component => TaskbarRestLayoutPolicy.IsVisible(component, Connected(spectrum: false)),
            leadingInset: 4,
            availableWidth: 300,
            sectionGap: 8,
            trailingMargin: 4);

        Assert.AreEqual(0, layout.TextLeft, 0.001);
        Assert.AreEqual(0, layout.TextWidth, 0.001);
        Assert.AreEqual(4 + 40 + 8 + 76 + 4, layout.ContentWidth, 0.001);
    }

    /// <summary>
    /// 一个组件都不可见时布局为空：没有媒体又没有保留任何组件时就是这种状态，宿主据此隐藏整条媒体栏。
    /// With nothing visible the layout is empty, which is the state with no media and nothing kept; the host hides the whole bar from it.
    /// </summary>
    [TestMethod]
    public void NothingVisibleProducesAnEmptyLayout()
    {
        var layout = TaskbarRestLayoutPolicy.Arrange(
            TaskbarRestLayoutPolicy.DefaultOrder,
            WidthOf,
            component => TaskbarRestLayoutPolicy.IsVisible(component, Idle([])),
            leadingInset: 4,
            availableWidth: 300,
            sectionGap: 8,
            trailingMargin: 4);

        Assert.IsTrue(layout.IsEmpty);
        Assert.AreEqual(0, layout.ContentWidth, 0.001);
    }

    /// <summary>
    /// 显隐规则：有媒体时封面与媒体文字始终在（它们是这条媒体栏存在的理由，因此不提供开关），其余按各自开关；
    /// 没有媒体时由"没有媒体时显示"列表唯一决定，且封面与媒体文字那时一定不显示。
    /// The visibility rule: while media is connected the artwork and the media text are always there (they are the reason the bar exists, so they carry
    /// no switch) and the rest follow their own switches; without media the "shown without media" list decides alone, and the artwork and the media text
    /// are never shown then.
    /// </summary>
    [TestMethod]
    public void VisibilityFollowsTheConnectedStateAndTheIdleList()
    {
        // 有媒体：封面与文字无视任何开关，其余按各自开关。
        // Connected: the artwork and the text ignore every switch, the rest follow their own.
        var connected = Connected(spectrum: true, performance: false, outputDevice: true, volume: false);
        Assert.IsTrue(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Artwork, connected));
        Assert.IsTrue(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.MediaText, connected));
        Assert.IsTrue(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Spectrum, connected));
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Performance, connected));
        Assert.IsTrue(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.OutputDevice, connected));
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Volume, connected));

        // 没有媒体、从未配置：默认只留快速启动小音符；封面与文字一定不显示，其余也不显示。
        // No media and never configured: only the quick-launch note stays by default; the artwork and the text are never shown, nor is anything else.
        var idleDefault = Idle(null);
        Assert.IsTrue(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Artwork, idleDefault));
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.MediaText, idleDefault));
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Spectrum, idleDefault));
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Performance, idleDefault));
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.OutputDevice, idleDefault));
        CollectionAssert.AreEqual(
            new[] { TaskbarRestComponent.Artwork },
            TaskbarRestLayoutPolicy.ResolveIdleComponents(null).ToArray());

        // 没有媒体且用户勾了几个：只留勾选的组件；列表里写了媒体文字也不算数（无媒体时它没有内容可写）。
        // 列表里那一项 `Artwork` 指的是快速启动小音符——封面那个框在无媒体时画的正是音符，不是曲目封面。
        // No media with some components checked: only those stay, and naming the media text changes nothing (with no media it has nothing to write).
        // The `Artwork` entry there means the quick-launch note: that box draws the note while disconnected, not a track's cover.
        var idleKept = Idle([TaskbarRestComponent.Performance, TaskbarRestComponent.Artwork, TaskbarRestComponent.MediaText]);
        Assert.IsTrue(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Artwork, idleKept));
        Assert.IsTrue(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Performance, idleKept));
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.MediaText, idleKept));
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Spectrum, idleKept));

        // 只留性能监控时音符也收起：无媒体时"封面框"整块不显示，而不是留一个空白框。
        // Keeping only the performance monitor also hides the note: without media that box is gone entirely rather than left blank.
        var idleWithoutNote = Idle([TaskbarRestComponent.Performance]);
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Artwork, idleWithoutNote));

        // 没有媒体且全部关掉：一个都不显示，整条媒体栏隐藏。
        // No media with everything off: nothing is shown and the whole bar is hidden.
        var idleNothing = Idle([]);
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Artwork, idleNothing));
        Assert.IsFalse(TaskbarRestLayoutPolicy.IsVisible(TaskbarRestComponent.Performance, idleNothing));
        Assert.AreEqual(0, TaskbarRestLayoutPolicy.ResolveIdleComponents([]).Count);
    }

    /// <summary>媒体栏长度只由"可见组件集合"决定，与顺序无关：改顺序不该改变需要的长度，否则排一次序媒体栏就会变宽或变窄。
    /// The bar length depends only on the set of visible components, never on the order: reordering must not change the length needed, otherwise the
    /// bar would grow or shrink every time it is rearranged.</summary>
    [TestMethod]
    public void RequiredLengthDoesNotDependOnTheOrder()
    {
        var forward = new[]
        {
            TaskbarRestComponent.Artwork,
            TaskbarRestComponent.MediaText,
            TaskbarRestComponent.Spectrum,
            TaskbarRestComponent.Performance
        };
        var reversed = forward.Reverse().ToArray();

        Assert.AreEqual(
            TaskbarExperiencePolicy.CalculateRestWidth(forward, 4, 120, 4, 8, WidthOf, double.PositiveInfinity),
            TaskbarExperiencePolicy.CalculateRestWidth(reversed, 4, 120, 4, 8, WidthOf, double.PositiveInfinity),
            0.001);

        // 断开的默认行为必须与改动前逐像素一致：封面 + 尾留白。
        // The default disconnected behaviour has to match the previous one pixel for pixel: the artwork plus the trailing margin.
        Assert.AreEqual(48, TaskbarExperiencePolicy.CalculateWidth(
            120, 44, 38, 4, false, false, true, true, true, TaskbarInformationDensity.Balanced, double.PositiveInfinity), 0.001);
    }

    /// <summary>
    /// 设置归一化：非法枚举与重复项被丢掉，顺序保留，而 <c>null</c>（从未配置）不会被写成空列表——
    /// 对"没有媒体时显示"来说这两者含义不同（前者是默认的小音符，后者是全部关掉）。
    /// Settings normalization: undefined members and duplicates are dropped while the order is kept, and null (never configured) is never rewritten as an
    /// empty list — for "shown without media" the two mean different things (the default note versus everything turned off).
    /// </summary>
    [TestMethod]
    public void SettingsNormalizationKeepsTheOrderAndTheNullMeaning()
    {
        var normalized = (TaskbarExperienceSettings.Default with
        {
            RestComponentOrder = [TaskbarRestComponent.Volume, TaskbarRestComponent.Volume, (TaskbarRestComponent)99, TaskbarRestComponent.Spectrum],
            IdleComponents = null,
            OutputDeviceVisible = true,
            VolumeVisible = true
        }).Normalize();

        CollectionAssert.AreEqual(
            new[] { TaskbarRestComponent.Volume, TaskbarRestComponent.Spectrum },
            normalized.RestComponentOrder!.ToArray());
        Assert.IsNull(normalized.IdleComponents);
        Assert.IsTrue(normalized.OutputDeviceVisible);
        Assert.IsTrue(normalized.VolumeVisible);
        CollectionAssert.AreEqual(
            new[] { TaskbarRestComponent.Artwork },
            TaskbarRestLayoutPolicy.ResolveIdleComponents(null).ToArray());

        // 默认值必须保持改动前的行为：新组件默认不显示，没有媒体时的默认保留是小音符。
        // The defaults must keep the previous behaviour: the new components are off, and the default kept component without media is the note.
        var defaults = TaskbarExperienceSettings.Default;
        Assert.IsFalse(defaults.OutputDeviceVisible);
        Assert.IsFalse(defaults.VolumeVisible);
        Assert.IsNull(defaults.RestComponentOrder);
        Assert.IsNull(defaults.IdleComponents);
    }

    /// <summary>
    /// 只有来源提供器明确报过"没有可读的媒体"时才走在线取词兜底。
    /// 网易云正常工作时由内存读取供词（逐字歌词 + 精确进度），此时再取一次只会每首歌白发一次请求。
    /// The online-lyric fallback applies only once the source provider has explicitly reported having no readable media.
    /// While NetEase works, memory reading supplies the lyrics (word level, plus an exact position) and fetching again would waste one request per track.
    /// </summary>
    [TestMethod]
    public void OnlineLyricFallbackOnlyAppliesWhenTheProviderReportsNothing()
    {
        Assert.IsTrue(MediaEnrichmentFallbackPolicy.ShouldRequestOnlineLyrics(true, true, true, "Get High"));
        Assert.IsFalse(MediaEnrichmentFallbackPolicy.ShouldRequestOnlineLyrics(true, true, false, "Get High"));
        Assert.IsFalse(MediaEnrichmentFallbackPolicy.ShouldRequestOnlineLyrics(true, true, true, "  "));
        Assert.IsFalse(MediaEnrichmentFallbackPolicy.ShouldRequestOnlineLyrics(false, true, true, "Get High"));
        Assert.IsFalse(MediaEnrichmentFallbackPolicy.ShouldRequestOnlineLyrics(true, false, true, "Get High"));
    }
}
