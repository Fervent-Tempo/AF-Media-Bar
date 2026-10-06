using AFMediaBar.Classes.Settings;
using OpenccNetLib;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 歌词文本的简繁转换：按设置把**呈现**文本改写成用户想看的字形，字典一侧由 OpenCC 引擎负责。
/// Converts lyric text between Simplified and Traditional Chinese for **presentation** only; the OpenCC engine owns the dictionary side.
///
/// 只改写即将送去呈现引擎的那几个字符串，从不触碰取回的歌词文档：设置一关就回到原文，
/// 因此改变它不需要重新取词（见 <c>LyricsCacheInvalidationPolicy</c>——纯呈现设置不该重新发起网络请求）。
/// Only the strings headed for the presentation engine are rewritten; the retrieved document is never touched. Turning the setting off
/// brings the original text straight back, so changing it never refetches (see <c>LyricsCacheInvalidationPolicy</c>: presentation-only
/// settings must not trigger a request).
///
/// 为什么不再同步等待词典加载：首次构造转换器要把内嵌词典展开（实测约 160 ms），而投影跑在 UI 线程上，
/// 在这条线程上付这笔账会卡掉一帧。这里的做法是——转换时只使用**已经就绪**的转换器，没就绪就在后台加载并
/// 先原样返回；投影每几百毫秒重跑一次，下一帧自然变成转换结果，界面全程不等。
/// Why nothing waits synchronously for the dictionary: constructing the converter for the first time expands the embedded
/// dictionary (measured at roughly 160 ms), and projection runs on the UI thread, which would drop a frame paying that cost here.
/// So conversion only uses a converter that is **already warm**; otherwise it starts loading in the background and returns the text
/// unchanged. Projection re-runs every few hundred milliseconds, so the very next frames carry the converted text and the interface
/// never blocks.
/// </summary>
public static class LyricsChineseConverter
{
    private static readonly object Sync = new();
    private static Opencc? _simplifiedToTraditional;
    private static Opencc? _traditionalToSimplified;
    private static bool _warmUpStarted;

    /// <summary>
    /// 按给定方向转换一段歌词文本。
    /// Converts one piece of lyric text in the given direction.
    /// </summary>
    /// <param name="text">待转换文本，可能是空串或不含中文的歌词 / Text to convert; may be empty or contain no Chinese at all.</param>
    /// <param name="mode">转换方向，<c>None</c> 表示原样返回 / Conversion direction; <c>None</c> returns the text unchanged.</param>
    /// <returns>转换后的文本；转换器尚未就绪时返回原文 / The converted text, or the original while the converter is still warming up.</returns>
    public static string Convert(string text, LyricsChineseConversionMode mode)
    {
        if (mode == LyricsChineseConversionMode.None || string.IsNullOrEmpty(text))
        {
            return text;
        }

        var converter = TryGetConverter(mode);
        if (converter is null)
        {
            RequestWarmUp();
            return text;
        }

        return converter.Convert(text);
    }

    /// <summary>
    /// 提前把两个方向的转换器加载到后台线程：设置刚打开时不用等第一帧自己付这笔账。
    /// Loads both directions on a background thread ahead of time, so enabling the setting does not make the first frame pay for it.
    /// </summary>
    public static void WarmUp() => RequestWarmUp();

    private static Opencc? TryGetConverter(LyricsChineseConversionMode mode)
    {
        lock (Sync)
        {
            return mode switch
            {
                LyricsChineseConversionMode.SimplifiedToTraditional => _simplifiedToTraditional,
                LyricsChineseConversionMode.TraditionalToSimplified => _traditionalToSimplified,
                _ => null
            };
        }
    }

    private static void RequestWarmUp()
    {
        lock (Sync)
        {
            if (_warmUpStarted)
            {
                return;
            }

            _warmUpStarted = true;
        }

        Task.Run(() =>
        {
            try
            {
                TryLoad();
            }
            catch (Exception ex)
            {
                // 词典加载失败时保持现状（显示原文）而不是把异常抛回 UI 线程：歌词不该因为转换引擎而消失。
                // A dictionary failure keeps the current behaviour (original text) rather than throwing back onto the UI thread: lyrics must
                // never disappear because of the conversion engine.
                System.Diagnostics.Debug.WriteLine($"[Lyrics] 繁简转换字典加载失败 / Chinese conversion dictionary failed to load: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 同步加载两个方向的转换器。生产路径一律走后台（见 <see cref="WarmUp"/>），这里只为单元测试提供确定结果——
    /// 测试里若走后台加载，断言会在词典准备好之前跑完，结果就成了随机成败。
    /// Loads both directions synchronously. Production always goes through the background path (see <see cref="WarmUp"/>); this entry
    /// exists so unit tests get a deterministic result — otherwise the assertion would run before the dictionary is ready and pass or
    /// fail at random.
    /// </summary>
    internal static void LoadNow() => TryLoad();

    private static void TryLoad()
    {
        // 词典在包里只存一份，构造第二个方向几乎是零成本；两个方向都备好，切换方向时不再有任何延迟。
        // One dictionary copy ships with the package, so building the second direction costs almost nothing; preparing both means
        // switching direction later has no delay at all.
        lock (Sync)
        {
            _simplifiedToTraditional ??= new Opencc(OpenccConfig.S2T);
            _traditionalToSimplified ??= new Opencc(OpenccConfig.T2S);
        }
    }
}
