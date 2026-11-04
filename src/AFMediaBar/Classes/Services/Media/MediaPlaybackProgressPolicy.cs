// 判断“播放器没有给播放进度”这件事要不要向用户解释；纯策略，不读界面也不读设置存储。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 播放进度缺失时的解释策略。
/// </summary>
/// <remarks>
/// 有些播放器通过 SMTC 只上报标题和歌手，位置和时长恒为 0；网易云、酷狗这类由进程内存读取进度，读取失效时也会落到
/// 同一个状态。两种情况的共同表现是：歌词已经取到，但没有位置可以选行，于是歌词区一直是空的。
/// 暂停不改变这个判断：状态没变，提示也应该留在原地，否则一按暂停就闪回歌手，看起来像提示消失了。
/// 这里只回答“该不该解释”，具体文案由界面按来源是否适配过选择。
/// </remarks>
public static class MediaPlaybackProgressPolicy
{
    /// <summary>
    /// 是否应当告诉用户当前播放器不提供播放进度。
    /// </summary>
    /// <param name="snapshot">当前快照。/ Current snapshot.</param>
    /// <param name="lyricsEnabled">用户是否启用了歌词；没启用时歌词区本来就不显示，不需要解释。</param>
    public static bool ExplainsMissingProgress(MediaSnapshot snapshot, bool lyricsEnabled) =>
        lyricsEnabled &&
        snapshot.IsConnected &&
        snapshot.Duration <= 0 &&
        snapshot.Position <= 0 &&
        snapshot.Lyrics?.Document.Lines is { Count: > 0 };
}
