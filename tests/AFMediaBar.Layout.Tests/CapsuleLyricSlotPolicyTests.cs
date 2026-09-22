using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 胶囊文字槽的模式与回落判定：有歌词显示当前歌词行，没有歌词（或总开关关掉）回落"歌名 - 作者"。
/// The capsule text slot's mode and fallback decision: a real lyric line shows while lyrics are usable, and everything else falls back to
/// "title - artist".
///
/// 这一层**不重新实现**"有没有歌词"：判定整条走卡片那条既有路径
/// （<see cref="CardLyricPresentationPolicy.Resolve"/>），本策略只是从它的结果里挑出胶囊该用哪一种槽。
/// This layer deliberately **does not re-implement** "are there lyrics": the decision goes through the card's existing path
/// (<see cref="CardLyricPresentationPolicy.Resolve"/>), and the policy only picks which slot the capsule should use from that result.
/// </summary>
[TestClass]
public sealed class CapsuleLyricSlotPolicyTests
{
    /// <summary>回落文本的取样值：胶囊里它就是 <c>ApplySnapshot</c> 拼出来的"歌名 - 作者"。/ The sample fallback text: on the capsule this is the "title - artist" ApplySnapshot composes.</summary>
    private const string Fallback = "歌名 - 作者";

    /// <summary>一次取词结果，带一行真实歌词。/ One lyric selection carrying a real line.</summary>
    private static LyricLineUpdate RealLine(string text) =>
        new(text, "下一句", "译文", "yinyi", true, new LyricLine(10, 14, text));

    /// <summary>
    /// 歌词总开关打开且当前句可用时，胶囊文字槽进入歌词模式并显示**当前歌词行**。
    /// With the master switch on and a usable active line the capsule text slot takes lyric mode and shows the **active lyric line**.
    /// </summary>
    [TestMethod]
    public void UsableLyricLinePutsTheCapsuleSlotInLyricMode()
    {
        var state = CapsuleLyricSlotPolicy.Resolve(
            new AppSettings { LyricsEnabled = true },
            RealLine("当前句"),
            Fallback,
            capsuleVisible: true);

        Assert.AreEqual(CapsuleSlotMode.Lyric, state.Mode);
        Assert.AreEqual("当前句", state.Text);
    }

    /// <summary>
    /// 无歌词曲目（来源用"[00:00.00]暂无歌词"整行表示）不算歌词：胶囊回落"歌名 - 作者"，与任务栏静置层同语义。
    /// A track without lyrics — the source answers "[00:00.00]暂无歌词" as one whole line — is not a lyric: the capsule falls back to
    /// "title - artist", the same semantics the taskbar's rest layer uses.
    /// </summary>
    [TestMethod]
    public void PlaceholderLyricFallsBackToTitleAndArtist()
    {
        var placeholder = new LyricLine(0, 3, "暂无歌词");
        var state = CapsuleLyricSlotPolicy.Resolve(
            new AppSettings { LyricsEnabled = true },
            new LyricLineUpdate("暂无歌词", string.Empty, string.Empty, string.Empty, true, placeholder),
            Fallback,
            capsuleVisible: true);

        Assert.AreEqual(CapsuleSlotMode.Title, state.Mode);
        Assert.AreEqual(Fallback, state.Text);
    }

    /// <summary>
    /// 歌词总开关关掉时**即使有真实歌词行**也回落"歌名 - 作者"：这是任务书 105 的第三条判据。
    /// With the master switch off the capsule falls back to "title - artist" **even with a real lyric line**, which is the third acceptance
    /// criterion of item 105.
    /// </summary>
    [TestMethod]
    public void MasterSwitchOffFallsBackEvenWithARealLine()
    {
        var state = CapsuleLyricSlotPolicy.Resolve(
            new AppSettings { LyricsEnabled = false },
            RealLine("当前句"),
            Fallback,
            capsuleVisible: true);

        Assert.AreEqual(CapsuleSlotMode.Title, state.Mode);
        Assert.AreEqual(Fallback, state.Text);
    }

    /// <summary>
    /// 没有命中任何歌词行（还没有歌词文档、位置早于第一行）时回落：胶囊不会显示一个空槽。
    /// With no lyric line matched — no document yet, or a position before the first line — the slot falls back rather than showing an empty slot.
    /// </summary>
    [TestMethod]
    public void MissingLyricLineFallsBack()
    {
        var state = CapsuleLyricSlotPolicy.Resolve(
            new AppSettings { LyricsEnabled = true },
            new LyricLineUpdate(string.Empty, string.Empty, string.Empty, string.Empty, false, null),
            Fallback,
            capsuleVisible: true);

        Assert.AreEqual(CapsuleSlotMode.Title, state.Mode);
        Assert.AreEqual(Fallback, state.Text);
    }

    /// <summary>
    /// 胶囊不可见（卡片已展开）时文字槽一律回到歌名模式：胶囊侧的歌词时间轴按 <c>lineVisible = !_isExpanded</c> 停止，槽的模式必须
    /// 与它用同一个可见性输入，否则收拢后会出现"时间轴没跑、槽却停在歌词模式"的错配。
    /// While the capsule is invisible (the card is expanded) the slot always returns to title mode: the capsule's lyric timeline stops on
    /// <c>lineVisible = !_isExpanded</c>, and the slot's mode has to take its visibility from the very same input, or a collapse would leave
    /// the slot in lyric mode with no timeline running behind it.
    /// </summary>
    [TestMethod]
    public void ExpandedCapsuleNeverTakesLyricMode()
    {
        var state = CapsuleLyricSlotPolicy.Resolve(
            new AppSettings { LyricsEnabled = true },
            RealLine("当前句"),
            Fallback,
            capsuleVisible: false);

        Assert.AreEqual(CapsuleSlotMode.Title, state.Mode);
    }
}
