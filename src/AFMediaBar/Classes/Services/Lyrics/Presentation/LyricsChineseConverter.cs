using AFMediaBar.Classes.Settings;
using OpenccNetLib;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 按设置转换歌词呈现文本，不修改歌词文档或取词缓存。
/// 词典在后台构造，呈现调用只读取已发布实例，未就绪时返回原文。
/// </summary>
public static class LyricsChineseConverter
{
    private static readonly object _sync = new();
    private static Opencc? _simplifiedToTraditional;
    private static Opencc? _traditionalToSimplified;
    private static int _warmUpStarted;

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
    /// 在后台完成两个方向的首次转换，避免 UI 线程初始化延迟加载的转换计划。
    /// </summary>
    public static void WarmUp() => RequestWarmUp();

    private static Opencc? TryGetConverter(LyricsChineseConversionMode mode)
    {
        return mode switch
        {
            LyricsChineseConversionMode.SimplifiedToTraditional => Volatile.Read(ref _simplifiedToTraditional),
            LyricsChineseConversionMode.TraditionalToSimplified => Volatile.Read(ref _traditionalToSimplified),
            _ => null
        };
    }

    private static void RequestWarmUp()
    {
        if (Interlocked.CompareExchange(ref _warmUpStarted, 1, 0) != 0)
            return;

        _ = Task.Run(() =>
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
        if (Volatile.Read(ref _simplifiedToTraditional) is not null &&
            Volatile.Read(ref _traditionalToSimplified) is not null)
            return;

        // 构造不持发布锁，呈现线程也不获取这把锁，避免首次加载阻塞 UI。
        var simplifiedToTraditional = new Opencc(OpenccConfig.S2T);
        var traditionalToSimplified = new Opencc(OpenccConfig.T2S);
        // Opencc 的转换计划延迟到实际 Convert 时加载，构造成功不代表可以发布就绪。
        _ = simplifiedToTraditional.Convert("简体歌词转换");
        _ = traditionalToSimplified.Convert("繁體歌詞轉換");
        lock (_sync)
        {
            if (_simplifiedToTraditional is null)
                Volatile.Write(ref _simplifiedToTraditional, simplifiedToTraditional);
            if (_traditionalToSimplified is null)
                Volatile.Write(ref _traditionalToSimplified, traditionalToSimplified);
        }
    }
}
