using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 胶囊岛点击绑定的纯逻辑测试：落在媒体动作区且未被合成点击抑制时执行绑定，其余情况才允许"点击 = 固定/取消固定"。
/// Pure-logic tests for the capsule island's click bindings: a press that starts on a media-action area runs the binding unless
/// the click is a chord-wheel synthesis, and only every other case may be read as "click = toggle the pin".
/// </summary>
[TestClass]
public sealed class CapsuleIslandClickPolicyTests
{
    /// <summary>
    /// 只有"按压起点落在媒体动作区"且"这一次点击不是组合滚轮合成的"才执行媒体绑定；
    /// 合成点击必须连绑定一起吞掉，否则按住组合键滚动会顺手切一次歌。
    /// The media binding runs only when the press started on a media-action area **and** the click was not synthesized by a chord
    /// wheel; a synthesized click has to swallow the binding too, or holding the chord key to scroll would also skip a track.
    /// </summary>
    [TestMethod]
    public void ShouldRunMediaAction_只有落在媒体动作区且未被抑制时才执行绑定()
    {
        Assert.IsTrue(CapsuleIslandClickPolicy.ShouldRunMediaAction(suppressedClick: false, pressOnMediaAction: true));
        Assert.IsFalse(CapsuleIslandClickPolicy.ShouldRunMediaAction(suppressedClick: true, pressOnMediaAction: true));
        // 空白处（卡片背景、行间空隙）不是媒体动作区，永远不执行媒体绑定。
        // A blank area (the card background, the gaps between rows) is not a media-action area and never runs a media binding.
        Assert.IsFalse(CapsuleIslandClickPolicy.ShouldRunMediaAction(suppressedClick: false, pressOnMediaAction: false));
        Assert.IsFalse(CapsuleIslandClickPolicy.ShouldRunMediaAction(suppressedClick: true, pressOnMediaAction: false));
    }

    /// <summary>
    /// 固定态只在"空白处的未抑制点击"上切换：媒体动作区的点击把命中吃掉但不钉住岛——固定态下指针离开不再收起，
    /// "点封面切歌/暂停"绝不该把岛钉住，那正是"点一下就卡住不再收起"的来源。
    /// The pin toggles only on an unsuppressed click in a blank area: a media-action hit is consumed without pinning the island,
    /// because a pinned island survives the pointer leaving and "click the cover to skip or pause" must never wedge it open.
    /// </summary>
    [TestMethod]
    public void ShouldTogglePin_媒体动作区与合成点击都不切换固定()
    {
        Assert.IsTrue(CapsuleIslandClickPolicy.ShouldTogglePin(suppressedClick: false, pressOnMediaAction: false));
        Assert.IsFalse(CapsuleIslandClickPolicy.ShouldTogglePin(suppressedClick: false, pressOnMediaAction: true));
        Assert.IsFalse(CapsuleIslandClickPolicy.ShouldTogglePin(suppressedClick: true, pressOnMediaAction: false));
        Assert.IsFalse(CapsuleIslandClickPolicy.ShouldTogglePin(suppressedClick: true, pressOnMediaAction: true));
    }
}

/// <summary>
/// 胶囊岛滚轮面的纯逻辑测试：整块岛都是媒体滚轮面，只有三个自带滚轮语义的控件必须放行。
/// Pure-logic tests for the capsule island's wheel surface: the whole island is a media wheel surface, and only the three
/// controls that carry wheel semantics of their own have to be released.
/// </summary>
[TestClass]
public sealed class CapsuleIslandWheelPolicyTests
{
    /// <summary>
    /// 输出设备、音量与进度条三者各自处理滚轮（切设备 / 调音量 / 拖动进度），悬停其上时媒体滚轮必须放行；
    /// 岛内其余任何位置（胶囊、卡片、封面上）滚轮都是媒体手势。
    /// Output device, volume, and the seek slider each handle the wheel themselves (switch device / adjust volume / scrub), so the
    /// media wheel has to be released while the pointer is over them; anywhere else on the island — capsule, card, artwork — the
    /// wheel is a media gesture.
    /// </summary>
    [TestMethod]
    public void ShouldHandleMediaWheel_三个自带滚轮语义的控件必须放行()
    {
        Assert.IsTrue(CapsuleIslandWheelPolicy.ShouldHandleMediaWheel(isOverOutputDevice: false, isOverVolume: false, isOverSeek: false));
        Assert.IsFalse(CapsuleIslandWheelPolicy.ShouldHandleMediaWheel(isOverOutputDevice: true, isOverVolume: false, isOverSeek: false));
        Assert.IsFalse(CapsuleIslandWheelPolicy.ShouldHandleMediaWheel(isOverOutputDevice: false, isOverVolume: true, isOverSeek: false));
        Assert.IsFalse(CapsuleIslandWheelPolicy.ShouldHandleMediaWheel(isOverOutputDevice: false, isOverVolume: false, isOverSeek: true));
        Assert.IsFalse(CapsuleIslandWheelPolicy.ShouldHandleMediaWheel(isOverOutputDevice: true, isOverVolume: true, isOverSeek: true));
    }

    /// <summary>
    /// 切歌的结果细节会随（异步到达的）新媒体变化，因此气泡打开期间必须允许改写一次；
    /// 其余结果（媒体源、设备、音量）在写入那一刻就已经是终值，改写只会写回同一段文字，因此一律不动。
    /// A skip's detail follows the (asynchronously arriving) new media, so one rewrite is allowed while the bubble is open; every other
    /// result (media source, device, volume) is already final when it is written, so a rewrite would only write the same text back and is
    /// never attempted.
    /// </summary>
    [TestMethod]
    public void ShouldRefreshWheelResultDetail_只有切歌结果且曲名变化时才改写()
    {
        var skip = new WheelTooltipResult("下一首", "旧曲名", DetailFollowsMedia: true);
        Assert.IsTrue(CapsuleIslandWheelPolicy.ShouldRefreshWheelResultDetail(skip, "新曲名"));
        // 曲名还没更新（新会话尚未发布）时不要改写，否则会把旧曲名再写一遍并打断细节跟随。
        // While the title has not moved yet (the new session is not published) nothing is rewritten, or the old title would be written
        // again and the detail would stop following.
        Assert.IsFalse(CapsuleIslandWheelPolicy.ShouldRefreshWheelResultDetail(skip, "旧曲名"));
        // 新标题为空（切歌途中读不到）也算变化：气泡应当从"旧曲名"退回"只有动作名"。
        // An empty new title (unreadable mid-skip) counts as a change too: the bubble falls back from the old title to the action name
        // alone.
        Assert.IsTrue(CapsuleIslandWheelPolicy.ShouldRefreshWheelResultDetail(skip, null));
        Assert.IsTrue(CapsuleIslandWheelPolicy.ShouldRefreshWheelResultDetail(skip, "  "));
    }

    /// <summary>
    /// 没有结果（气泡尚未写过内容）或结果不跟随媒体时一次都不改写——后者包括"细节本来就是空的"。
    /// Nothing is rewritten without a result (the bubble has shown nothing yet) or while the result does not follow the media — the
    /// latter includes a detail that was empty to begin with.
    /// </summary>
    [TestMethod]
    public void ShouldRefreshWheelResultDetail_结果为空或不跟随媒体时都不改写()
    {
        Assert.IsFalse(CapsuleIslandWheelPolicy.ShouldRefreshWheelResultDetail(null, "新曲名"));
        Assert.IsFalse(CapsuleIslandWheelPolicy.ShouldRefreshWheelResultDetail(
            new WheelTooltipResult("输出设备", "扬声器", DetailFollowsMedia: false), "新曲名"));
        Assert.IsFalse(CapsuleIslandWheelPolicy.ShouldRefreshWheelResultDetail(
            new WheelTooltipResult("切换媒体源", null, DetailFollowsMedia: false), "新曲名"));
    }
}

/// <summary>
/// 一次按压的起点区域的状态机测试：按下记录、抬起**读一次并清零**。
/// State-machine tests for one press's starting area: the press records it and the release **takes it once and clears it**.
///
/// 这条清零路径是必需的：起点字段若在抬起后留着，下一次"没有按下记录"的抬起（例如按下事件落在按钮上被早退、
/// 或者捕获在按下途中丢失）就会拿上一次的起点去执行切歌/暂停/激活来源——残留状态被放大成了用户看得见的误动作。
/// The clearing path is required: if the starting area survived the release, the next release that has no press of its own — the press
/// landed on a button and exited early, or capture was lost mid-press — would run skip/pause/activate-source from the previous press,
/// turning leftover state into a visible, unintended action.
/// </summary>
[TestClass]
public sealed class CapsuleIslandPressSurfaceTests
{
    /// <summary>按下落在封面区 → 抬起读出"媒体动作区 + 封面"，且同一次按压只能读一次（第二次已经是空白处）。/ A press on a cover answers "media-action area + artwork" once and only once; the second take is already blank.</summary>
    [TestMethod]
    public void TakePressSurface_读一次即清零()
    {
        var surface = new CapsuleIslandPressSurface();
        surface.Capture(artwork: true);

        Assert.IsTrue(surface.IsOnMediaAction);
        Assert.IsTrue(surface.IsOnArtwork);

        var (onMediaAction, onArtwork) = surface.Take();
        Assert.IsTrue(onMediaAction);
        Assert.IsTrue(onArtwork);
        Assert.IsFalse(surface.IsOnMediaAction);
        Assert.IsFalse(surface.IsOnArtwork);

        var (againMediaAction, againArtwork) = surface.Take();
        Assert.IsFalse(againMediaAction);
        Assert.IsFalse(againArtwork);
    }

    /// <summary>文字/歌词区（<c>artwork: false</c>）是媒体动作区但不是封面；空白处（<c>null</c>）两者都不成立。/ The text/lyric region (artwork false) is a media-action area but not a cover; a blank area (null) satisfies neither.</summary>
    [TestMethod]
    public void CapturePressSurface_文字区与空白处分别落位()
    {
        var surface = new CapsuleIslandPressSurface();

        surface.Capture(artwork: false);
        Assert.IsTrue(surface.IsOnMediaAction);
        Assert.IsFalse(surface.IsOnArtwork);

        surface.Capture(artwork: null);
        Assert.IsFalse(surface.IsOnMediaAction);
        Assert.IsFalse(surface.IsOnArtwork);
    }

    /// <summary>捕获丢失（<c>LostMouseCapture</c>）与窗口关闭时的复位：两者都要求立刻回到空白处，残留值一次都不允许被读出。/ The reset used on capture loss (<c>LostMouseCapture</c>) and on window close: both must return to blank at once, so a leftover value can never be read once.</summary>
    [TestMethod]
    public void ResetPressSurface_回到空白处()
    {
        var surface = new CapsuleIslandPressSurface();
        surface.Capture(artwork: true);
        surface.Reset();

        Assert.IsFalse(surface.IsOnMediaAction);
        Assert.IsFalse(surface.IsOnArtwork);

        var (onMediaAction, onArtwork) = surface.Take();
        Assert.IsFalse(onMediaAction);
        Assert.IsFalse(onArtwork);
    }
}
