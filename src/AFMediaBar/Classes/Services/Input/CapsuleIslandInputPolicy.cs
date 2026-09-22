using System;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 胶囊岛媒体动作区的点击判定：落在媒体动作区上的单击执行绑定，其余位置的单击才是"固定/取消固定"。
/// Click decisions for the capsule island's media-action areas: a click that starts on one of them runs the binding, and only a
/// click anywhere else is read as "toggle the pin".
///
/// 两条判定互斥，因此"点封面切歌/暂停"绝不会顺手把岛钉住（固定态下指针离开不再收起，那正是"点一下就卡住不再收起"的来源），
/// 而卡片空白处仍然保留原来的固定/取消固定语义。组合滚轮合成的那次点击两条都不成立：它只被吞掉。
/// The two decisions are mutually exclusive, so "click the cover to skip or pause" can never pin the island open — a pinned island
/// survives the pointer leaving, which is exactly what wedges it open — while a blank part of the card keeps its original pin
/// semantics. The click synthesized by a chord wheel satisfies neither: it is merely swallowed.
/// </summary>
public static class CapsuleIslandClickPolicy
{
    /// <summary>
    /// 这一次单击是否要执行媒体绑定：按压起点落在媒体动作区，且它不是组合滚轮合成的那次点击。
    /// Whether this click runs the media binding: the press started on a media-action area and it is not the click synthesized by a
    /// chord wheel.
    /// </summary>
    /// <param name="suppressedClick">本次抬起是否是组合滚轮合成的点击（一次性抑制标记的取值）。/ Whether this release is a chord-wheel synthesized click, as read from the one-shot suppression flag.</param>
    /// <param name="pressOnMediaAction">按压起点是否落在媒体动作区。/ Whether the press started on a media-action area.</param>
    public static bool ShouldRunMediaAction(bool suppressedClick, bool pressOnMediaAction) =>
        pressOnMediaAction && !suppressedClick;

    /// <summary>
    /// 这一次单击是否切换固定态：只有既不在媒体动作区、也不被抑制的点击才切换。
    /// Whether this click toggles the pin: only a click that is neither on a media-action area nor suppressed does.
    /// </summary>
    /// <param name="suppressedClick">本次抬起是否是组合滚轮合成的点击（一次性抑制标记的取值）。/ Whether this release is a chord-wheel synthesized click, as read from the one-shot suppression flag.</param>
    /// <param name="pressOnMediaAction">按压起点是否落在媒体动作区。/ Whether the press started on a media-action area.</param>
    public static bool ShouldTogglePin(bool suppressedClick, bool pressOnMediaAction) =>
        !suppressedClick && !pressOnMediaAction;
}

/// <summary>
/// 胶囊岛滚轮面的判定：整座岛（胶囊态与卡片态）都是媒体滚轮面，只有三个自带滚轮语义的控件必须放行。
/// Wheel-surface decision for the capsule island: the whole island — capsule and card — is a media wheel surface, and only the
/// three controls that carry wheel semantics of their own have to be released.
///
/// 这与任务栏静置层同源：那里也把设备、音量与进度条放行，因为"按住 Shift 滚动"在这些控件上应当仍然是它们自己的手势。
/// This mirrors the taskbar rest layer, which releases the device, volume, and seek widgets for the same reason: a chord wheel over
/// those controls has to stay their own gesture.
/// </summary>
public static class CapsuleIslandWheelPolicy
{
    /// <summary>
    /// 这一次滚轮是否按媒体手势处理：指针不在输出设备、音量与进度条三者之上时成立。
    /// Whether this wheel is handled as a media gesture: it holds while the pointer is over none of the output-device, volume, and
    /// seek controls.
    /// </summary>
    /// <param name="isOverOutputDevice">指针是否在输出设备按钮上。/ Whether the pointer is over the output-device button.</param>
    /// <param name="isOverVolume">指针是否在音量按钮上。/ Whether the pointer is over the volume button.</param>
    /// <param name="isOverSeek">指针是否在进度条上。/ Whether the pointer is over the seek slider.</param>
    public static bool ShouldHandleMediaWheel(bool isOverOutputDevice, bool isOverVolume, bool isOverSeek) =>
        !(isOverOutputDevice || isOverVolume || isOverSeek);

    /// <summary>
    /// 滚轮气泡里的结果细节是否要改写一次：只有"细节会随媒体变化"的结果（切歌）且新曲名与写入时不同才改写。
    ///
    /// 切歌是异步的：滚动那一刻读到的是**切歌前**的曲名（见 <c>GlobalInteractionRouter.ExecuteWheelAsync</c> 里
    /// <c>DetailFollowsMedia</c> 的注释），因此气泡要先显示当时的结果，等新会话到达后再改写成真正的曲名；
    /// 其余结果（媒体源、设备、音量）写入时就是终值，改写只会写回同一段文字。
    /// Whether the wheel bubble's detail has to be rewritten once: only a result whose detail follows the media (a skip) does, and only
    /// while the new title differs from the one written.
    ///
    /// A skip is asynchronous — the title read at the moment of scrolling is the one from **before** the skip (see the
    /// <c>DetailFollowsMedia</c> remark in <c>GlobalInteractionRouter.ExecuteWheelAsync</c>) — so the bubble shows that provisional result
    /// first and is rewritten with the real title once the new session arrives. Every other result (media source, device, volume) is
    /// final when written, so a rewrite would only put the same text back.
    /// </summary>
    /// <param name="result">气泡当前显示的结果；从未写过内容时为 null。/ The result the bubble currently shows, or null when nothing has been written yet.</param>
    /// <param name="currentTitle">当前快照里的曲名（读不到时为 null）。/ The title in the current snapshot, or null when unreadable.</param>
    public static bool ShouldRefreshWheelResultDetail(WheelTooltipResult? result, string? currentTitle) =>
        result is { DetailFollowsMedia: true } shown &&
        !string.Equals(shown.Detail, currentTitle, StringComparison.Ordinal);
}

/// <summary>
/// 一次按压的起点区域：按下时记录，抬起时**读一次并立刻清零**，捕获丢失与窗口关闭时复位。
/// One press's starting area: recorded on the press, **taken once and cleared at once** on the release, and reset on capture loss and on
/// window close.
///
/// 为什么必须有清零路径：这个值决定"抬起是不是媒体动作"，而媒体动作会真的切歌/暂停/激活来源。若它跨按压残留，
/// 下一次没有自己按下记录的抬起（按下事件落在按钮上被早退、捕获在按下途中丢失、抬起落在岛外）就会拿上一次的起点去执行动作——
/// 残留状态被放大成用户看得见的误动作。因此"读"与"清"必须是同一个操作，且复位点不止一处。
/// Why a clearing path is required: this value decides whether the release is a media action, and a media action really skips, pauses, or
/// activates a source. Left over across presses, the next release that has no press of its own — the press landed on a button and exited
/// early, capture was lost mid-press, the release landed outside the island — would run the previous press's action, turning leftover
/// state into a visible, unintended action. Reading and clearing are therefore one operation, and there is more than one reset point.
/// </summary>
public struct CapsuleIslandPressSurface
{
    /// <summary>本次按压是否落在媒体动作区。/ Whether this press landed on a media-action area.</summary>
    public bool IsOnMediaAction { get; private set; }

    /// <summary>本次按压是否落在封面区（仅当它同时是媒体动作区时才有意义）。/ Whether this press landed on a cover area, meaningful only while it is on a media-action area too.</summary>
    public bool IsOnArtwork { get; private set; }

    /// <summary>
    /// 记录一次按压的起点区域。
    /// Records one press's starting area.
    /// </summary>
    /// <param name="artwork">祖先链上最近的媒体动作区是不是封面区；不是媒体动作区（空白处）时为 null。/ Whether the nearest media-action area on the ancestor chain is a cover, or null for a blank area.</param>
    public void Capture(bool? artwork)
    {
        IsOnMediaAction = artwork is not null;
        IsOnArtwork = artwork == true;
    }

    /// <summary>把起点区域复位成"空白处"。捕获丢失与窗口关闭各调一次，避免任何残留值被下一次抬起读到。/ Resets the starting area to "blank", called once on capture loss and once on window close so no leftover value can be read by a later release.</summary>
    public void Reset()
    {
        IsOnMediaAction = false;
        IsOnArtwork = false;
    }

    /// <summary>
    /// 读一次并清零：抬起只消费一次起点，消费后立刻复位，因此同一次按压不可能被执行两次、也不会漏到下一次按压。
    /// Takes the starting area once and clears it: a release consumes the press exactly once and the state is blank immediately
    /// afterwards, so one press can never run twice nor leak into the next one.
    /// </summary>
    public (bool OnMediaAction, bool OnArtwork) Take()
    {
        var taken = (IsOnMediaAction, IsOnArtwork);
        Reset();
        return taken;
    }
}
