using System.Windows.Media;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 当前播放封面的强调色：应用强调色（"从封面取色"）与封面控件共用这一份结果。
/// Accent colour of the current artwork: the application accent ("from artwork") and the artwork control share this single result.
///
/// 做成服务而不是某个控件里的局部变量，是因为它有两个消费者，而且必须在曲目切换时一起刷新。
/// It is a service rather than a local inside one control because it has two consumers and has to refresh as one when the track changes.
/// </summary>
public sealed class CoverAccentColorService
{
    /// <summary>进程级共享实例：组合根负责喂封面，控件与主题协调器读同一份结果。/ Process-wide instance fed by the composition root; controls and the theme coordinator read the same result.</summary>
    public static CoverAccentColorService Shared { get; } = new();

    private ImageSource? _artwork;
    private Color? _darkThemeAccent;
    private Color? _lightThemeAccent;

    /// <summary>封面变化或清空后触发；不保证在 UI 线程，订阅方自行切回。/ Raised after the artwork changed or was cleared, not necessarily on the UI thread.</summary>
    public event Action? Changed;

    /// <summary>按当前主题取封面强调色；没有封面或取色失败时为 null，由调用方回退到应用强调色。/ Accent for the given theme, or null without a usable cover, leaving the caller to fall back.</summary>
    public Color? Resolve(bool darkTheme) => darkTheme ? _darkThemeAccent : _lightThemeAccent;

    /// <summary>
    /// 换封面；同一张封面重复到达时直接返回，避免每次快照刷新都重算。
    /// Replaces the artwork; repeated deliveries of the same artwork return early so a snapshot refresh does not recompute it.
    /// </summary>
    /// <param name="artwork">新封面；null 表示当前没有封面。/ The new artwork; null means there is none.</param>
    public void Update(ImageSource? artwork)
    {
        if (ReferenceEquals(_artwork, artwork))
            return;

        _artwork = artwork;
        // 两档明暗一次算好：强调色发布与封面控件可能在主题切换前后分别读取，不必为此再取一次色。
        // Both brightness variants are computed up front: the accent publish and the artwork control can read across a theme change
        // without having to extract the colour again.
        _darkThemeAccent = ArtworkAccentExtractor.Extract(artwork, darkTheme: true);
        _lightThemeAccent = ArtworkAccentExtractor.Extract(artwork, darkTheme: false);
        Changed?.Invoke();
    }
}
