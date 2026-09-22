using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 胶囊文字槽的模式：歌名（旋转式跑马灯）或当前歌词行（跟随式擦亮滚动）。
/// The capsule text slot's mode: the title (a rotating marquee) or the active lyric line (a follow reveal scroll).
///
/// 这是一个**显式状态**，不是从 <c>CapsuleTitle.Text</c> 反推出来的：两个槽都会往同一格文字里写，靠读文本猜"现在是谁在管"
/// 正是接缝 S3/S4 描述的那种错配（歌词窗口会被当成歌名去量宽、再被整体位移顶掉）。
/// This is an **explicit state**, never inferred back out of <c>CapsuleTitle.Text</c>: both slots write into the same text cell, and guessing
/// "who owns it now" by reading the string is exactly the mismatch seams S3/S4 describe (a lyric window gets measured as if it were a title
/// and then pushed out by the whole-track translate).
/// </summary>
public enum CapsuleSlotMode
{
    /// <summary>歌名槽：显示"歌名 - 作者"，过长时走既有的旋转式跑马灯。/ The title slot: shows "title - artist" and takes the existing rotation marquee when it overflows.</summary>
    Title = 0,

    /// <summary>歌词槽：显示当前歌词行，过长时走跟随式擦亮滚动（编号 106）。/ The lyric slot: shows the active lyric line and takes the follow reveal scroll when it overflows (item 106).</summary>
    Lyric = 1
}

/// <summary>
/// 胶囊文字槽用哪一种模式、以及该写什么文本。
/// Which mode the capsule text slot takes and what text it should show.
///
/// **本策略不判断"有没有歌词"。** 那一条判定整条复用卡片那条既有路径
/// （<see cref="CardLyricPresentationPolicy.Resolve"/> 的"方案一"），胶囊只是把同一个结果用不同的槽渲染出来：
/// <see cref="CardLyricPresentationPolicy.PresentationState.FirstLine"/> 的 <c>ShowActiveLine</c> 为真就进歌词槽，
/// 否则进歌名槽——回落文本本身也是同一次解算的产物（它已经由 <c>fallbackText</c> 参与过判定）。
/// **This policy never decides "are there lyrics".** That decision goes entirely through the card's existing path (the "scheme one"
/// semantics of <see cref="CardLyricPresentationPolicy.Resolve"/>), and the capsule merely renders the same result in a different slot: a true
/// <c>ShowActiveLine</c> on <see cref="CardLyricPresentationPolicy.PresentationState.FirstLine"/> takes the lyric slot and everything else takes
/// the title slot — the fallback text being the product of that very same resolution, since <c>fallbackText</c> already took part in it.
/// </summary>
public static class CapsuleLyricSlotPolicy
{
    /// <summary>
    /// 胶囊文字槽这一帧的状态。
    /// The capsule text slot's state for one frame.
    /// </summary>
    /// <param name="Mode">该用哪一个槽 / Which slot is in charge.</param>
    /// <param name="Text">该写进这个槽的文本 / The text to write into that slot.</param>
    public readonly record struct SlotState(CapsuleSlotMode Mode, string Text);

    /// <summary>
    /// 解算胶囊文字槽的模式与文本。
    /// Resolves the capsule text slot's mode and text.
    /// </summary>
    /// <param name="settings">应用设置（歌词总开关等）。/ Application settings, including the lyrics master switch.</param>
    /// <param name="update">歌词取词结果（<see cref="LyricLinePresenter"/> 产出）。/ The lyric selection produced by <see cref="LyricLinePresenter"/>.</param>
    /// <param name="fallbackText">没有可用歌词时的回落文本；胶囊传"歌名 - 作者"，与 <c>ApplySnapshot</c> 的拼法一致。/ Text to fall back to without a usable lyric; the capsule passes "title - artist", the same way ApplySnapshot composes it.</param>
    /// <param name="capsuleVisible">胶囊是否可见（<c>!_isExpanded</c>）；不可见时一律走歌名槽，与胶囊侧时间轴的 <c>lineVisible</c> 同源。/ Whether the capsule is visible (<c>!_isExpanded</c>); while invisible the title slot always wins, from the same source as the capsule timeline's <c>lineVisible</c>.</param>
    public static SlotState Resolve(AppSettings settings, LyricLineUpdate update, string fallbackText, bool capsuleVisible)
    {
        var presentation = CardLyricPresentationPolicy.Resolve(settings, update, fallbackText);
        var lyric = capsuleVisible && presentation.FirstLine.ShowActiveLine;

        // 文本一律取同一次解算的第一行结果：它是歌词就是歌词，回落时它**就是** fallbackText，因此胶囊里不存在第二份"没有歌词"的判断，
        // 也不可能与卡片显示出不同的字。
        // The text always comes from that one resolution's first row: a lyric is a lyric, and on the fallback path it **is** fallbackText, so the
        // capsule owns no second "are there lyrics" decision and can never disagree with the card about the characters.
        return new SlotState(
            lyric ? CapsuleSlotMode.Lyric : CapsuleSlotMode.Title,
            presentation.FirstLine.Text);
    }
}
